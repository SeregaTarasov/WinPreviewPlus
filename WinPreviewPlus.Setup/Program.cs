using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace WinPreviewPlus.Setup
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ShowFatal(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                ShowFatal(e.ExceptionObject as Exception ?? new Exception(Convert.ToString(e.ExceptionObject)));
            };

            if (TryRelaunchIfNeeded(args))
                return 0;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool silent = Has(args, "/silent") || Has(args, "-silent") || Has(args, "/S");
            bool uninstall = Has(args, "/uninstall") || Has(args, "-uninstall") || Has(args, "/u");
            bool install = Has(args, "/install") || Has(args, "-install") || Has(args, "/i") ||
                           (!uninstall && silent);

            if (silent && uninstall)
            {
                try
                {
                    Installer.Log = Console.WriteLine;
                    Installer.Uninstall(restartExplorer: true);
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    return 1;
                }
            }

            if (silent && install)
            {
                try
                {
                    Installer.Log = Console.WriteLine;
                    Installer.Install(
                        restartExplorer: true,
                        ensureNet48: true,
                        ensureWebView2: true,
                        silentDeps: true);
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    return 1;
                }
            }

            try
            {
                Application.Run(new MainForm(
                    silentInstall: install && !uninstall,
                    silentUninstall: uninstall));
                return 0;
            }
            catch (Exception ex)
            {
                ShowFatal(ex);
                return 1;
            }
        }

        /// <summary>
        /// Запуск не из Загрузок/Desktop/TEMP/папки продукта — копируем в Загрузки и
        /// перезапускаем оттуда (обходит блокировку записи из папки сборки/проекта).
        /// </summary>
        private static bool TryRelaunchIfNeeded(string[] args)
        {
            if (Has(args, "/norelaunch") || Has(args, "-norelaunch"))
                return false;

            string loc;
            try { loc = Assembly.GetExecutingAssembly().Location; }
            catch { return false; }
            if (string.IsNullOrEmpty(loc) || !File.Exists(loc))
                return false;

            string locFull;
            try { locFull = Path.GetFullPath(loc); }
            catch { return false; }

            if (IsSafeLaunchLocation(locFull))
                return false;

            string tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            string dest = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                "WinPreviewPlus-Setup.exe");
            string destUnique = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                "WinPreviewPlus-Setup-" + tag + ".exe");

            string argLine = (BuildArgLine(args) + " /norelaunch").Trim();
            Exception last = null;

            foreach (var target in new[] { dest, destUnique })
            {
                try
                {
                    if (RelaunchViaCmd(locFull, target, argLine))
                        return true;
                }
                catch (Exception ex) { last = ex; }

                try
                {
                    string dir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    try
                    {
                        if (File.Exists(target))
                        {
                            File.SetAttributes(target, FileAttributes.Normal);
                            File.Delete(target);
                        }
                    }
                    catch { }

                    File.Copy(locFull, target, true);
                    if (!WaitForFile(target, 10000))
                        throw new IOException("copy incomplete");

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = target,
                        Arguments = argLine,
                        UseShellExecute = true,
                        WorkingDirectory = dir
                    });
                    return true;
                }
                catch (Exception ex) { last = ex; }
            }

            // Desktop fallback
            try
            {
                string desk = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "WinPreviewPlus-Setup.exe");
                if (RelaunchViaCmd(locFull, desk, argLine))
                    return true;
            }
            catch (Exception ex) { last = ex; }

            ShowFatal(new InvalidOperationException(
                "Не удалось подготовить установщик.\n\n" +
                "Скопируйте WinPreviewPlus-Setup.exe вручную в папку «Загрузки»\n" +
                "или на Рабочий стол и запустите оттуда.\n\n" +
                MaskPath(last?.Message ?? ""), last));
            return true;
        }

        private static bool RelaunchViaCmd(string src, string dest, string argLine)
        {
            string dir = Path.GetDirectoryName(dest) ?? "";
            // Только копирование через cmd (обход песочницы). Старт — из нашего процесса.
            string cmd =
                "/c mkdir \"" + dir + "\" 2>nul & " +
                "copy /Y \"" + src + "\" \"" + dest + "\" & " +
                "if not exist \"" + dest + "\" exit /b 1";

            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = cmd,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (p == null) return false;
            if (!p.WaitForExit(120000))
            {
                try { p.Kill(); } catch { }
                return false;
            }
            if (p.ExitCode != 0 || !WaitForFile(dest, 5000))
                return false;

            try
            {
                // Снять MOTW, иначе SmartScreen может тихо блокировать
                var zi = dest + ":Zone.Identifier";
                try { if (File.Exists(zi)) File.Delete(zi); } catch { }
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -Command \"Unblock-File -LiteralPath '" + dest.Replace("'", "''") + "'\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    })?.WaitForExit(15000);
                }
                catch { }
            }
            catch { }

            Process.Start(new ProcessStartInfo
            {
                FileName = dest,
                Arguments = argLine,
                UseShellExecute = true,
                WorkingDirectory = dir
            });
            return true;
        }

        private static bool WaitForFile(string path, int ms)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                try
                {
                    if (File.Exists(path) && new FileInfo(path).Length > 1000)
                        return true;
                }
                catch { }
                System.Threading.Thread.Sleep(50);
            }
            try { return File.Exists(path) && new FileInfo(path).Length > 1000; }
            catch { return false; }
        }

        private static bool IsSafeLaunchLocation(string locFull)
        {
            try
            {
                string[] roots =
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Path.GetTempPath(),
                    Installer.InstallRoot,
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")
                };
                foreach (var r in roots)
                {
                    if (string.IsNullOrEmpty(r)) continue;
                    string full = Path.GetFullPath(r)
                        .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (locFull.StartsWith(full, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static string BuildArgLine(string[] args)
        {
            var sb = new StringBuilder();
            if (args == null) return "";
            foreach (var a in args)
            {
                if (string.IsNullOrWhiteSpace(a)) continue;
                if (string.Equals(a, "/norelaunch", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "-norelaunch", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(QuoteArg(a));
            }
            return sb.ToString();
        }

        private static string QuoteArg(string a)
        {
            if (a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
            return "\"" + a.Replace("\"", "\\\"") + "\"";
        }

        private static string MaskPath(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(home))
                    s = s.Replace(home, "%USERPROFILE%");
            }
            catch { }
            return s;
        }

        private static void ShowFatal(Exception ex)
        {
            try
            {
                MessageBox.Show(
                    MaskPath(ex?.Message ?? "Unknown error"),
                    "WinPreviewPlus Setup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch
            {
                try { Console.Error.WriteLine(ex); } catch { }
            }
        }

        private static bool Has(string[] args, string flag)
        {
            if (args == null) return false;
            foreach (var a in args)
            {
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
