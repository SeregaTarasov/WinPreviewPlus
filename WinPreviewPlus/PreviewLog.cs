using System;
using System.IO;

namespace WinPreviewPlus
{
    internal static class PreviewLog
    {
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
                    File.AppendAllText(Path.Combine(Dir, "preview.log"), line);
                }
            }
            catch
            {
                // never throw from logger inside prevhost
            }
        }
    }
}
