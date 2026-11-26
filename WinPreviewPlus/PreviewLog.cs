using System;
using System.IO;

namespace WinPreviewPlus
{
    internal static class PreviewLog
    {
        private const long MaxLogBytes = 1024 * 1024;
        private static readonly object Gate = new object();
        private static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinPreviewPlus");

        public static void Info(string message) => Write("INFO", message);

        public static void Error(string message, Exception ex = null)
        {
            if (ex == null)
                Write("ERROR", message);
            else
                Write("ERROR", message + " | " + ex);
        }

        private static void Write(string level, string message)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + message + Environment.NewLine;
                lock (Gate)
                {
                    var path = Path.Combine(Dir, "preview.log");
                    RotateIfNeeded(path);
                    File.AppendAllText(path, line);
                }
            }
            catch
            {
                // never throw from logger inside prevhost
            }
        }

        private static void RotateIfNeeded(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxLogBytes) return;
            var bak = path + ".1";
            if (File.Exists(bak))
                File.Delete(bak);
            File.Move(path, bak);
        }
    }
}
