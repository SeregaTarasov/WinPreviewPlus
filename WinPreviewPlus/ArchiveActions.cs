using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace WinPreviewPlus
{
    internal static class ArchiveActions
    {
        private static readonly string[] RiskyExt =
        {
            ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".jse", ".wsf", ".msi", ".scr", ".com", ".cpl", ".reg"
        };

        public static void OpenEntry(string archivePath, string entryKey)
        {
            if (string.IsNullOrWhiteSpace(entryKey) || entryKey.EndsWith("\\") || entryKey.EndsWith("/"))
            {
                MessageBox.Show("Выберите файл, а не папку.", "Архив",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var ext = Path.GetExtension(entryKey);
            if (RiskyExt.Any(r => r.Equals(ext, StringComparison.OrdinalIgnoreCase)))
            {
                var r = MessageBox.Show(
                    "Файл может быть программой:\n" + Path.GetFileName(entryKey) +
                    "\n\nОткрыть всё равно?",
                    "Запуск из архива",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) return;
            }

            var tempRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinPreviewPlus", "open", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var outPath = ExtractOne(archivePath, entryKey, tempRoot);
                if (string.IsNullOrEmpty(outPath) || !File.Exists(outPath))
                    throw new InvalidOperationException("Не удалось извлечь файл.");

                Process.Start(new ProcessStartInfo
                {
                    FileName = outPath,
                    UseShellExecute = true
                });
                PreviewLog.Info("Opened from archive: " + entryKey + " -> " + outPath);
            }
            catch (Exception ex)
            {
                PreviewLog.Error("OpenEntry failed: " + entryKey, ex);
                MessageBox.Show("Не удалось открыть:\n" + ex.Message, "Архив",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public static void ExtractEntries(string archivePath, string[] entryKeys, string destDir)
        {
            Directory.CreateDirectory(destDir);
            using (var archive = ArchiveFactory.Open(archivePath))
            {
                var wanted = new HashSet<string>(
                    entryKeys.Select(NormalizeKey).Where(k => !string.IsNullOrEmpty(k)),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var entry in archive.Entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.Key) || entry.IsDirectory)
                        continue;
                    var key = NormalizeKey(entry.Key);
                    if (wanted.Count > 0 && !wanted.Contains(key) &&
                        !wanted.Any(w => key.StartsWith(w.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var relative = entry.Key.Replace('/', Path.DirectorySeparatorChar);
                    var target = Path.Combine(destDir, relative);
                    var parent = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);
                    using (var fs = File.Create(target))
                    using (var es = entry.OpenEntryStream())
                        es.CopyTo(fs);
                }
            }
        }

        public static void ExtractAll(string archivePath, string destDir)
        {
            Directory.CreateDirectory(destDir);
            using (var archive = ArchiveFactory.Open(archivePath))
            {
                foreach (var entry in archive.Entries.Where(e => e != null && !e.IsDirectory))
                {
                    var relative = entry.Key.Replace('/', Path.DirectorySeparatorChar);
                    var target = Path.Combine(destDir, relative);
                    var parent = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);
                    using (var fs = File.Create(target))
                    using (var es = entry.OpenEntryStream())
                        es.CopyTo(fs);
                }
            }
        }

        public static void RevealInExplorer(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                MessageBox.Show("Исходный файл недоступен (открыт через поток).", "Архив",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Process.Start("explorer.exe", "/select,\"" + path + "\"");
        }

        /// <summary>
        /// Извлекает выбранные файлы во временную папку для OLE FileDrop (как ярлыки с рабочего стола).
        /// </summary>
        public static string[] ExtractForDragDrop(string archivePath, string[] entryKeys)
        {
            if (entryKeys == null || entryKeys.Length == 0)
                return new string[0];

            var tempRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinPreviewPlus", "drag", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();

            using (var archive = ArchiveFactory.Open(archivePath))
            {
                var wanted = new HashSet<string>(
                    entryKeys.Select(NormalizeKey).Where(k => !string.IsNullOrEmpty(k) && !k.EndsWith("\\")),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var entry in archive.Entries)
                {
                    if (entry == null || entry.IsDirectory || string.IsNullOrEmpty(entry.Key))
                        continue;
                    var key = NormalizeKey(entry.Key);
                    if (!wanted.Contains(key))
                        continue;

                    var fileName = Path.GetFileName(entry.Key.Replace('/', '\\'));
                    if (string.IsNullOrEmpty(fileName))
                        continue;

                    var unique = fileName;
                    int n = 2;
                    while (!usedNames.Add(unique))
                    {
                        var stem = Path.GetFileNameWithoutExtension(fileName);
                        var ext = Path.GetExtension(fileName);
                        unique = stem + " (" + n + ")" + ext;
                        n++;
                    }

                    var target = Path.Combine(tempRoot, unique);
                    using (var fs = File.Create(target))
                    using (var es = entry.OpenEntryStream())
                        es.CopyTo(fs);
                    result.Add(target);
                }
            }

            PreviewLog.Info("DragDrop extract: " + result.Count + " -> " + tempRoot);
            return result.ToArray();
        }

        private static string ExtractOne(string archivePath, string entryKey, string destDir)
        {
            var want = NormalizeKey(entryKey);
            using (var archive = ArchiveFactory.Open(archivePath))
            {
                var entry = archive.Entries.FirstOrDefault(e =>
                    e != null && !e.IsDirectory &&
                    NormalizeKey(e.Key).Equals(want, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                    return null;

                var fileName = Path.GetFileName(entry.Key.Replace('/', '\\'));
                var target = Path.Combine(destDir, fileName);
                using (var fs = File.Create(target))
                using (var es = entry.OpenEntryStream())
                    es.CopyTo(fs);
                return target;
            }
        }

        private static string NormalizeKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            return key.Replace('/', '\\').TrimEnd('\\');
        }
    }
}
