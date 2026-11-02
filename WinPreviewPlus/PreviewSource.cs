using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using SharpShell.SharpPreviewHandler;

namespace WinPreviewPlus
{
    /// <summary>
    /// Путь к файлу для превью. Предпочитаем оригинал с диска;
    /// temp из IStream — только если пути нет (редко).
    /// </summary>
    public sealed class PreviewSource : IDisposable
    {
        public string Path { get; private set; }
        /// <summary>Оригинальный путь в Проводнике (для сохранения / «в проводнике»).</summary>
        public string OriginalPath { get; private set; }
        public string DisplayName { get; private set; }
        public bool IsTemp { get; private set; }

        /// <summary>Открыть оригинальный файл по пути (предпочтительно).</summary>
        public static PreviewSource FromFilePath(string filePath, long maxBytes)
        {
            var src = new PreviewSource();
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                    throw new FileNotFoundException("Файл не найден", filePath);

                var info = new FileInfo(filePath);
                if (info.Length > maxBytes)
                    throw new InvalidOperationException("Файл слишком большой для превью (" + info.Length + " байт).");

                if (!CanOpenSharedRead(filePath))
                    throw new IOException("Файл занят или недоступен для чтения: " + filePath);

                TryClearMotw(filePath);
                src.Path = filePath;
                src.OriginalPath = filePath;
                src.DisplayName = info.Name;
                src.IsTemp = false;
                PreviewLog.Info("Using ORIGINAL file: " + src.Path);
                return src;
            }
            catch (Exception ex)
            {
                src.Dispose();
                PreviewLog.Error("PreviewSource.FromFilePath failed", ex);
                throw;
            }
        }

        public static PreviewSource FromHandler(
            SharpPreviewHandler handler,
            IStream comStream,
            string preferredExtension,
            long maxBytes)
        {
            var selected = handler?.SelectedFilePath;
            if (!string.IsNullOrWhiteSpace(selected) && File.Exists(selected))
                return FromFilePath(selected, maxBytes);

            var src = new PreviewSource();
            try
            {
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    src.OriginalPath = selected;
                    src.DisplayName = System.IO.Path.GetFileName(selected);
                }

                // Fallback: поток → temp (нет локального пути)
                if (comStream != null)
                {
                    src.Path = CopyComStreamToTemp(comStream, preferredExtension, selected, maxBytes);
                    src.IsTemp = true;
                    if (string.IsNullOrEmpty(src.DisplayName))
                        src.DisplayName = System.IO.Path.GetFileName(src.Path);
                    PreviewLog.Info("Using TEMP from IStream (no local path): " + src.Path +
                        " display=" + src.DisplayName);
                    return src;
                }

                throw new InvalidOperationException("Нет пути к файлу и нет потока.");
            }
            catch (Exception ex)
            {
                src.Dispose();
                PreviewLog.Error("PreviewSource failed", ex);
                throw;
            }
        }

        private static bool CanOpenSharedRead(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    return fs.CanRead;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string CopyComStreamToTemp(IStream comStream, string preferredExtension, string hintPath, long maxBytes)
        {
            var tempDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinPreviewPlus", "tmp");
            Directory.CreateDirectory(tempDir);
            var tempBase = System.IO.Path.Combine(tempDir, Guid.NewGuid().ToString("N"));
            var tempBin = tempBase + ".bin";

            using (var fs = File.Create(tempBin))
            {
                var buffer = new byte[81920];
                long total = 0;
                var pcbRead = Marshal.AllocCoTaskMem(sizeof(int));
                try
                {
                    while (true)
                    {
                        Marshal.WriteInt32(pcbRead, 0);
                        comStream.Read(buffer, buffer.Length, pcbRead);
                        var read = Marshal.ReadInt32(pcbRead);
                        if (read <= 0)
                            break;

                        total += read;
                        if (total > maxBytes)
                            throw new InvalidOperationException("Stream exceeded preview size limit.");
                        fs.Write(buffer, 0, read);
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pcbRead);
                }
            }

            var ext = ResolveExtension(tempBin, preferredExtension, hintPath);
            var tempPath = tempBase + ext;
            if (!string.Equals(tempBin, tempPath, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                File.Move(tempBin, tempPath);
            }

            return tempPath;
        }

        private static string ResolveExtension(string tempBin, string preferredExtension, string hintPath)
        {
            var sniffed = SniffExtension(tempBin);
            if (!string.IsNullOrEmpty(sniffed))
                return sniffed;

            if (!string.IsNullOrEmpty(preferredExtension) &&
                !preferredExtension.Equals(".docx", StringComparison.OrdinalIgnoreCase) &&
                !preferredExtension.Equals(".bin", StringComparison.OrdinalIgnoreCase))
                return preferredExtension.StartsWith(".") ? preferredExtension : "." + preferredExtension;

            if (!string.IsNullOrEmpty(hintPath))
            {
                var fromHint = System.IO.Path.GetExtension(hintPath);
                if (!string.IsNullOrEmpty(fromHint))
                    return fromHint;
            }

            if (!string.IsNullOrEmpty(preferredExtension))
                return preferredExtension.StartsWith(".") ? preferredExtension : "." + preferredExtension;

            return ".bin";
        }

        private static string SniffExtension(string path)
        {
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    if (fs.Length < 4) return null;
                    var sig = new byte[4];
                    if (fs.Read(sig, 0, 4) < 4) return null;

                    if (sig[0] == 0x50 && sig[1] == 0x4B)
                    {
                        fs.Position = 0;
                        using (var zip = new ZipArchive(fs, ZipArchiveMode.Read, true))
                        {
                            if (zip.GetEntry("xl/workbook.xml") != null) return ".xlsx";
                            if (zip.GetEntry("word/document.xml") != null) return ".docx";
                            if (zip.GetEntry("ppt/presentation.xml") != null) return ".pptx";
                        }
                        return ".zip";
                    }

                    // OLE Compound Document — .xls/.doc/.ppt; без разбора хранилища не отличить
                    if (sig[0] == 0xD0 && sig[1] == 0xCF && sig[2] == 0x11 && sig[3] == 0xE0)
                        return null;

                    if (sig[0] == 0x37 && sig[1] == 0x7A && sig[2] == 0xBC && sig[3] == 0xAF)
                        return ".7z";
                    if (sig[0] == 0x52 && sig[1] == 0x61 && sig[2] == 0x72 && sig[3] == 0x21)
                        return ".rar";
                    if (sig[0] == 0x1F && sig[1] == 0x8B)
                        return ".gz";
                    if (sig[0] == 0x25 && sig[1] == 0x50 && sig[2] == 0x44 && sig[3] == 0x46)
                        return ".pdf";
                }
            }
            catch (Exception ex)
            {
                PreviewLog.Error("SniffExtension failed", ex);
            }
            return null;
        }

        private static void TryClearMotw(string path)
        {
            try
            {
                var zone = path + ":Zone.Identifier";
                if (File.Exists(zone))
                {
                    File.Delete(zone);
                    PreviewLog.Info("Cleared MOTW: " + path);
                }
            }
            catch (Exception ex)
            {
                PreviewLog.Error("Clear MOTW failed (non-fatal): " + path, ex);
            }
        }

        public void Dispose()
        {
            if (!IsTemp || string.IsNullOrEmpty(Path))
                return;
            try
            {
                if (File.Exists(Path))
                    File.Delete(Path);
            }
            catch
            {
                // ignore
            }
            Path = null;
            IsTemp = false;
        }
    }
}
