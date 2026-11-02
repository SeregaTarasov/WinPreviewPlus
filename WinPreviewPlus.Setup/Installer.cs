using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace WinPreviewPlus.Setup
{
    /// <summary>
    /// Полный цикл продукта: установка (файлы + COM + HKCU shellex + меню + Apps)
    /// и симметричное удаление без остатков в реестре/диске.
    /// </summary>
    internal static class Installer
    {
        public const string ProductName = "WinPreviewPlus";
        public const string Version = "1.1.0";

        public static readonly string InstallRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductName);
        public static readonly string BinDir = Path.Combine(InstallRoot, "bin");
        public static readonly string SetupCopyPath = Path.Combine(InstallRoot, "WinPreviewPlus-Setup.exe");

        private const string PreviewGuid = "{8895b1c6-b41f-4c1c-a562-0d564250836f}";
        private const string PrevHostAppId = "{6d2b5079-2f0b-48dd-ab7f-97cec514d30b}";
        private const string Arch = "{B7E4A901-2C5D-4F8A-9E31-11A0C3D5E701}";
        private const string Off = "{B7E4A902-2C5D-4F8A-9E31-11A0C3D5E702}";
        private const string Pdf = "{B7E4A903-2C5D-4F8A-9E31-11A0C3D5E703}";
        private const string Txt = "{B7E4A904-2C5D-4F8A-9E31-11A0C3D5E704}";
        private const string Aud = "{B7E4A905-2C5D-4F8A-9E31-11A0C3D5E705}";
        private const string Vid = "{B7E4A906-2C5D-4F8A-9E31-11A0C3D5E706}";
        private const string Img = "{B7E4A907-2C5D-4F8A-9E31-11A0C3D5E707}";

        private static readonly string[] HandlerIds =
        {
            Arch, Off, Pdf, Txt, Aud, Vid, Img
        };

        public static Action<string> Log = _ => { };

        public static bool HasPayload()
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var n in asm.GetManifestResourceNames())
                if (n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public static bool IsInstalled()
        {
            try
            {
                if (File.Exists(Path.Combine(BinDir, "WinPreviewPlus.dll")))
                    return true;
                using (var k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WinPreviewPlus"))
                {
                    return k != null;
                }
            }
            catch { return false; }
        }

        public static bool IsNet48Installed()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    if (k == null) return false;
                    object raw = k.GetValue("Release");
                    if (raw == null) return false;
                    int release = Convert.ToInt32(raw);
                    return release >= 528040;
                }
            }
            catch { return false; }
        }

        public static bool IsWebView2Installed()
        {
            try
            {
                foreach (var path in new[]
                {
                    @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                    @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
                })
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(path))
                    {
                        if (k?.GetValue("pv") is string pv && !string.IsNullOrEmpty(pv) && pv != "0.0.0.0")
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // Evergreen Bootstrapper (маленький) — сам скачает Runtime
        private const string WebView2BootstrapperUrl =
            "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
        // .NET Framework 4.8 web installer
        private const string Net48WebInstallerUrl =
            "https://go.microsoft.com/fwlink/?LinkId=2085155";

        public static void OpenWebView2Download()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = WebView2BootstrapperUrl,
                UseShellExecute = true
            });
        }

        public static void OpenNet48Download()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://dotnet.microsoft.com/download/dotnet-framework/net48",
                UseShellExecute = true
            });
        }

        /// <summary>
        /// Скачивает и ставит отмеченные отсутствующие зависимости, затем можно продолжать Install.
        /// .NET 4.8 и WebView2 обычно требуют UAC.
        /// </summary>
        public static void EnsureDependencies(bool installNet48, bool installWebView2, bool silent)
        {
            try
            {
                ServicePointManager.SecurityProtocol |=
                    SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
            catch { }

            if (installNet48 && !IsNet48Installed())
            {
                Log("Скачивание .NET Framework 4.8…");
                var setup = DownloadFile(Net48WebInstallerUrl, "ndp48-web-installer.exe");
                Log("Установка .NET Framework 4.8 (подтвердите UAC)…");
                // /q — тихий; /norestart — не перезагружать сразу
                RunSetupExe(setup, silent ? "/q /norestart" : "/norestart", silent);
                System.Threading.Thread.Sleep(800);
                if (!IsNet48Installed())
                {
                    throw new InvalidOperationException(
                        ".NET Framework 4.8 не установился. Откройте страницу загрузки вручную " +
                        "или перезагрузите ПК, если установщик просил reboot, и повторите.");
                }
                Log(".NET Framework 4.8: OK");
            }

            if (installWebView2 && !IsWebView2Installed())
            {
                Log("Скачивание WebView2 Runtime…");
                var setup = DownloadFile(WebView2BootstrapperUrl, "MicrosoftEdgeWebview2Setup.exe");
                Log("Установка WebView2 Runtime (подтвердите UAC)…");
                RunSetupExe(setup, silent ? "/silent /install" : "/install", silent);
                System.Threading.Thread.Sleep(800);
                if (!IsWebView2Installed())
                {
                    throw new InvalidOperationException(
                        "WebView2 Runtime не установился. Без него видео/SVG могут не открыться. " +
                        "Попробуйте кнопку «WebView2…» или повторите позже.");
                }
                Log("WebView2 Runtime: OK");
            }
        }

        private static string DownloadFile(string url, string fileName)
        {
            Directory.CreateDirectory(InstallRoot);
            var dest = Path.Combine(InstallRoot, fileName);
            try { if (File.Exists(dest)) File.Delete(dest); } catch { dest = Path.Combine(Path.GetTempPath(), fileName); }

            using (var wc = new WebClient())
            {
                long lastPct = -1;
                wc.DownloadProgressChanged += (s, e) =>
                {
                    if (e.ProgressPercentage == lastPct) return;
                    if (e.ProgressPercentage % 10 == 0 || e.ProgressPercentage == 100)
                    {
                        lastPct = e.ProgressPercentage;
                        Log("  " + fileName + ": " + e.ProgressPercentage + "%");
                    }
                };
                try
                {
                    // Синхронно — проще в фоне Task.Run
                    wc.DownloadFile(url, dest);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "Не удалось скачать " + fileName + " (" + url + "): " + ex.Message, ex);
                }
            }

            if (!File.Exists(dest) || new FileInfo(dest).Length < 1024)
                throw new InvalidOperationException("Загрузка повреждена: " + fileName);
            return dest;
        }

        private static void RunSetupExe(string exePath, string args, bool silent)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args ?? "",
                UseShellExecute = true,
                // UAC elevation — зависимости ставятся в систему
                Verb = "runas"
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    if (p == null)
                        throw new InvalidOperationException("Не удалось запустить " + Path.GetFileName(exePath));
                    p.WaitForExit(30 * 60 * 1000);
                    if (!p.HasExited)
                    {
                        try { p.Kill(); } catch { }
                        throw new TimeoutException("Установка длится слишком долго: " + Path.GetFileName(exePath));
                    }
                    // 0 = OK; 3010 = success reboot required (.NET)
                    if (p.ExitCode != 0 && p.ExitCode != 3010)
                    {
                        throw new InvalidOperationException(
                            Path.GetFileName(exePath) + " завершился с кодом " + p.ExitCode +
                            (silent ? "" : ". Если отменили UAC — повторите и подтвердите запрос."));
                    }
                    if (p.ExitCode == 3010)
                        Log("Установщик просит перезагрузку (код 3010) — продолжаем, если компонент уже виден.");
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // 1223 = cancelled UAC
                throw new InvalidOperationException(
                    "Нужны права администратора для установки зависимости. " +
                    "Подтвердите UAC или поставьте компонент вручную. (" + ex.Message + ")", ex);
            }
        }

        public static void Install(bool restartExplorer, bool ensureNet48 = true, bool ensureWebView2 = true, bool silentDeps = false)
        {
            EnsureDependencies(ensureNet48, ensureWebView2, silentDeps);

            if (!IsNet48Installed())
                throw new InvalidOperationException(
                    ".NET Framework 4.8 не найден. Отметьте галочку и нажмите «Установить», " +
                    "либо поставьте .NET 4.8 с сайта Microsoft.");

            EnsureInstallRootWritable();

            // prevhost держит DLL — сначала снять блокировки, потом подменить каталог bin
            Log("Остановка prevhost…");
            KillPrevhost();
            System.Threading.Thread.Sleep(400);

            Log("Извлечение файлов…");
            DeployPayloadToBin();
            EnsureNativeBesideDll();

            // Копия установщика в профиль — удаление из «Параметры → Приложения» всегда работает
            Log("Копия установщика в профиль…");
            CopySetupIntoInstallRoot();

            KillPrevhost();

            var dll = Path.Combine(BinDir, "WinPreviewPlus.dll");
            if (!File.Exists(dll))
                throw new FileNotFoundException("В пакете нет WinPreviewPlus.dll", dll);

            var regasm = FindRegAsm();
            Log("RegAsm /codebase…");
            Run(regasm, "/codebase \"" + dll + "\"", BinDir);

            var regExe = Path.Combine(BinDir, "WinPreviewPlus.Register.exe");
            if (!File.Exists(regExe))
                throw new FileNotFoundException("WinPreviewPlus.Register.exe не найден", regExe);

            Log("Регистрация preview handlers (COM)…");
            Run(regExe, "", BinDir);

            Log("Ассоциации расширений (HKCU)…");
            WriteAssociations();

            Log("Запись в список приложений Windows…");
            WriteUninstallInfo();
            CreateStartMenuEntries();

            try { ShellNotify.SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); } catch { }

            KillPrevhost();

            if (restartExplorer)
            {
                Log("Перезапуск Проводника…");
                RestartExplorer();
            }

            if (!IsWebView2Installed())
                Log("Замечание: WebView2 Runtime не найден — видео/SVG могут не открыться. Кнопка «WebView2…» в установщике.");

            Log("Готово. Проводник → файл → Alt+P.");
        }

        public static void Uninstall(bool restartExplorer)
        {
            Log("Остановка prevhost…");
            KillPrevhost();

            // COM / SharpShell — пока файлы ещё на месте
            var regExe = Path.Combine(BinDir, "WinPreviewPlus.Register.exe");
            if (File.Exists(regExe))
            {
                try
                {
                    Log("Снятие SharpShell / COM…");
                    Run(regExe, "uninstall", BinDir);
                }
                catch (Exception ex) { Log("SharpShell uninstall: " + ex.Message); }
            }

            var dll = Path.Combine(BinDir, "WinPreviewPlus.dll");
            var regasm = FindRegAsm();
            if (File.Exists(dll) && File.Exists(regasm))
            {
                try
                {
                    Log("RegAsm /unregister…");
                    Run(regasm, "/unregister \"" + dll + "\"", BinDir);
                }
                catch (Exception ex) { Log("RegAsm unregister: " + ex.Message); }
            }

            Log("Очистка ассоциаций и CLSID…");
            ClearAssociations();

            Log("Меню Пуск / Apps…");
            RemoveStartMenuEntries();
            RemoveUninstallInfo();

            KillPrevhost();

            Log("Удаление файлов…");
            TryDeleteDirectory(BinDir);
            // лог и прочее в корне
            TryDeleteFile(Path.Combine(InstallRoot, "preview.log"));

            if (restartExplorer)
            {
                Log("Перезапуск Проводника…");
                RestartExplorer();
            }

            try { ShellNotify.SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); } catch { }

            // Сам Setup.exe в InstallRoot может быть заблокирован (мы из него запущены) —
            // дочищаем через отложенную cmd.
            ScheduleDeleteInstallRoot();

            Log("Удаление завершено.");
        }

        private static string FindRegAsm()
        {
            var regasm = @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe";
            if (!File.Exists(regasm))
                throw new FileNotFoundException("RegAsm.exe не найден (нужен .NET 4.x x64)", regasm);
            return regasm;
        }

        private static void CopySetupIntoInstallRoot()
        {
            try
            {
                var src = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(src) || !File.Exists(src)) return;
                // не копируем сами в себя
                if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(SetupCopyPath),
                        StringComparison.OrdinalIgnoreCase))
                    return;
                File.Copy(src, SetupCopyPath, true);
            }
            catch (Exception ex)
            {
                Log("Копия Setup: " + ex.Message);
            }
        }

        private static void EnsureNativeBesideDll()
        {
            try
            {
                var x64Pdf = Path.Combine(BinDir, "x64", "pdfium.dll");
                var rootPdf = Path.Combine(BinDir, "pdfium.dll");
                if (File.Exists(x64Pdf) && !File.Exists(rootPdf))
                    File.Copy(x64Pdf, rootPdf, true);

                var x64Wv = Path.Combine(BinDir, "x64", "WebView2Loader.dll");
                var rootWv = Path.Combine(BinDir, "WebView2Loader.dll");
                if (File.Exists(x64Wv) && !File.Exists(rootWv))
                    File.Copy(x64Wv, rootWv, true);
            }
            catch (Exception ex) { Log("Native copy: " + ex.Message); }
        }

        /// <summary>
        /// Распаковка в %TEMP%, затем подмена bin\ — не пишем staging рядом с Setup.exe в профиле
        /// (AV / блокировка каталога часто давали «Отказано в доступе» на bin_staging_*).
        /// </summary>
        private static void DeployPayloadToBin()
        {
            EnsureInstallRootWritable();

            var stagingRoot = Path.Combine(
                Path.GetTempPath(),
                "WinPreviewPlus-setup-" + Guid.NewGuid().ToString("N"));
            var staging = Path.Combine(stagingRoot, "bin");
            // old bin уводим тоже в TEMP — меньше шансов упереться в ACL профиля
            var oldBin = Path.Combine(
                Path.GetTempPath(),
                "WinPreviewPlus-bin-old-" + Guid.NewGuid().ToString("N"));

            try
            {
                Log("Распаковка во временную папку…");
                ExtractPayload(staging);

                KillPrevhost();
                System.Threading.Thread.Sleep(350);

                if (Directory.Exists(BinDir))
                {
                    try
                    {
                        foreach (var f in Directory.GetFiles(BinDir, "*", SearchOption.AllDirectories))
                        {
                            try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                        }
                    }
                    catch { }

                    Exception renameEx = null;
                    for (int attempt = 1; attempt <= 6; attempt++)
                    {
                        try
                        {
                            KillPrevhost();
                            if (Directory.Exists(oldBin))
                                TryDeleteDirectory(oldBin);
                            Directory.Move(BinDir, oldBin);
                            renameEx = null;
                            break;
                        }
                        catch (Exception ex)
                        {
                            renameEx = ex;
                            Log("Каталог bin занят (" + attempt + "/6)…");
                            System.Threading.Thread.Sleep(300 * attempt);
                        }
                    }

                    if (renameEx != null)
                    {
                        Log("Подмена каталога не удалась — копирование поверх…");
                        CopyDirectoryOverwrite(staging, BinDir);
                        return;
                    }
                }

                try
                {
                    Directory.Move(staging, BinDir);
                }
                catch (IOException)
                {
                    // Разные тома TEMP / LocalAppData — копируем
                    Log("Копирование из TEMP в профиль…");
                    Directory.CreateDirectory(Path.GetDirectoryName(BinDir) ?? InstallRoot);
                    CopyDirectoryOverwrite(staging, BinDir);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException(
                    "Нет доступа к папке установки. Закройте превью Explorer (Alt+P), " +
                    "при необходимости удалите папку %LOCALAPPDATA%\\WinPreviewPlus и повторите. " +
                    ex.Message, ex);
            }
            finally
            {
                TryDeleteDirectory(stagingRoot);
                ScheduleDeleteDirectory(oldBin);
            }
        }

        private static void EnsureInstallRootWritable()
        {
            try
            {
                if (Directory.Exists(InstallRoot))
                {
                    try
                    {
                        var di = new DirectoryInfo(InstallRoot);
                        if ((di.Attributes & FileAttributes.ReadOnly) != 0)
                            di.Attributes &= ~FileAttributes.ReadOnly;
                    }
                    catch { }
                }

                Directory.CreateDirectory(InstallRoot);
                // Мягкая проверка — без фатального падения на probe
                var probe = Path.Combine(InstallRoot, ".wpp_write_test_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                try
                {
                    File.WriteAllText(probe, "ok");
                    try { File.Delete(probe); } catch { }
                    return;
                }
                catch
                {
                    // продолжаем — DeployPayloadToBin пишет из TEMP; если и там отказ — упадём там
                    Log("Предупреждение: ограниченная запись в папку установки, пробуем продолжить…");
                }
            }
            catch (Exception ex)
            {
                Log("EnsureInstallRootWritable: " + ex.Message);
                try { Directory.CreateDirectory(InstallRoot); } catch { }
            }
        }

        private static void ExtractPayload(string destDir)
        {
            var asm = Assembly.GetExecutingAssembly();
            string resName = null;
            foreach (var n in asm.GetManifestResourceNames())
            {
                if (n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase))
                {
                    resName = n;
                    break;
                }
            }
            if (resName == null)
                throw new InvalidOperationException(
                    "В установщике нет payload. Пересоберите packaging\\Build-WinPreviewPlus.ps1");

            if (Directory.Exists(destDir))
                TryDeleteDirectory(destDir);
            Directory.CreateDirectory(destDir);

            using (var s = asm.GetManifestResourceStream(resName))
            {
                if (s == null)
                    throw new InvalidOperationException("Не удалось открыть встроенный payload.zip");

                using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    foreach (var entry in zip.Entries)
                    {
                        var name = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        var full = Path.GetFullPath(Path.Combine(destDir, name));
                        var rootFull = Path.GetFullPath(destDir)
                            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Некорректный путь в payload: " + entry.FullName);

                        if (name.EndsWith(Path.DirectorySeparatorChar.ToString()) ||
                            string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(full);
                            continue;
                        }

                        var parent = Path.GetDirectoryName(full);
                        if (!string.IsNullOrEmpty(parent))
                            Directory.CreateDirectory(parent);

                        using (var es = entry.Open())
                        using (var fs = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.Read))
                            es.CopyTo(fs);
                        try { File.SetAttributes(full, FileAttributes.Normal); } catch { }
                    }
                }
            }
        }

        private static void CopyDirectoryOverwrite(string src, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            {
                var rel = dir.Substring(src.Length).TrimStart('\\', '/');
                Directory.CreateDirectory(Path.Combine(dest, rel));
            }

            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                var rel = file.Substring(src.Length).TrimStart('\\', '/');
                var target = Path.Combine(dest, rel);
                var parent = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);

                Exception last = null;
                for (int attempt = 1; attempt <= 5; attempt++)
                {
                    try
                    {
                        if (File.Exists(target))
                        {
                            try { File.SetAttributes(target, FileAttributes.Normal); } catch { }
                            try { File.Delete(target); } catch { }
                        }
                        File.Copy(file, target, true);
                        last = null;
                        break;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        last = ex;
                        KillPrevhost();
                        System.Threading.Thread.Sleep(250 * attempt);
                    }
                }
                if (last != null)
                    throw new IOException(
                        "Не удалось обновить " + rel +
                        " (закройте панель превью Explorer / Alt+P и повторите). " + last.Message, last);
            }
        }

        private static void ScheduleDeleteDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c ping 127.0.0.1 -n 4 >nul & rmdir /s /q \"" + path + "\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch { }
        }

        private static Dictionary<string, string> BuildExtMap()
        {
            var extMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in new[] { ".zip", ".7z", ".rar", ".tar", ".tgz", ".gz", ".bz2", ".xz" })
                extMap[e] = Arch;
            foreach (var e in new[] { ".docx", ".xlsx", ".xls", ".pptx" })
                extMap[e] = Off;
            extMap[".pdf"] = Pdf;
            foreach (var e in new[]
            {
                ".txt", ".log", ".md", ".csv", ".tsv", ".json", ".geojson", ".jsonl", ".ndjson",
                ".xml", ".plist", ".mobileconfig", ".entitlements", ".strings", ".xcconfig",
                ".pbxproj", ".podspec", ".ini", ".cfg", ".conf", ".toml", ".env",
                ".yml", ".yaml", ".ps1", ".bat", ".cmd", ".cs", ".js", ".ts", ".tsx", ".jsx",
                ".py", ".sql", ".swift", ".m", ".mm", ".h", ".hpp", ".c", ".cpp", ".rs", ".go",
                ".rb", ".php", ".sh", ".bash", ".zsh", ".html", ".htm", ".css", ".scss", ".less",
                ".editorconfig", ".gpx", ".kml", ".graphql", ".properties", ".gradle",
                ".gitignore", ".dockerignore"
            })
                extMap[e] = Txt;
            foreach (var e in new[]
            {
                ".mp3", ".wav", ".flac", ".m4a", ".m4r", ".aac", ".wma", ".ogg", ".opus", ".aiff", ".aif"
            })
                extMap[e] = Aud;
            foreach (var e in new[]
            {
                ".mp4", ".m4v", ".mov", ".avi", ".wmv", ".mkv", ".webm", ".mpeg", ".mpg", ".3gp"
            })
                extMap[e] = Vid;
            foreach (var e in new[]
            {
                ".jpg", ".jpeg", ".jfif", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".ico",
                ".svg", ".heic", ".heif", ".heics", ".avif"
            })
                extMap[e] = Img;
            return extMap;
        }

        private static void WriteAssociations()
        {
            var pairs = new Dictionary<string, string>
            {
                [Arch] = "WinPreviewPlus Archive",
                [Off] = "WinPreviewPlus Office",
                [Pdf] = "WinPreviewPlus PDF",
                [Txt] = "WinPreviewPlus Text",
                [Aud] = "WinPreviewPlus Audio",
                [Vid] = "WinPreviewPlus Video",
                [Img] = "WinPreviewPlus Image"
            };

            var extMap = BuildExtMap();

            foreach (var kv in new Dictionary<string, string>
            {
                ["Applications\\7zFM.exe"] = Arch,
                ["ASC.Sheet.12"] = Off,
                ["ASC.Document.12"] = Off,
                ["ASC.Presentation.12"] = Off,
                ["ASC.Sheet.1"] = Off,
                ["ASC.Document.1"] = Off,
                ["Excel.Sheet.8"] = Off,
                ["Excel.Sheet.12"] = Off
            })
            {
                SetShellex(@"Software\Classes\" + kv.Key + @"\shellex\" + PreviewGuid, kv.Value);
            }

            foreach (var kv in extMap)
            {
                SetShellex(@"Software\Classes\" + kv.Key + @"\shellex\" + PreviewGuid, kv.Value);
                SetShellex(@"Software\Classes\SystemFileAssociations\" + kv.Key + @"\shellex\" + PreviewGuid, kv.Value);
            }

            foreach (var p in pairs)
            {
                TuneClsid(Registry.CurrentUser, p.Key, p.Value);
                try { TuneClsid(Registry.LocalMachine, p.Key, p.Value); } catch { }

                using (var cat = Registry.CurrentUser.CreateSubKey(
                    @"Software\Classes\CLSID\" + p.Key + @"\Implemented Categories\" + PreviewGuid))
                { }

                using (var ph = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\PreviewHandlers"))
                {
                    ph.SetValue(p.Key, p.Value);
                }
            }
        }

        private static void ClearAssociations()
        {
            var our = new HashSet<string>(HandlerIds, StringComparer.OrdinalIgnoreCase);
            var extMap = BuildExtMap();

            foreach (var ext in extMap.Keys)
            {
                RemoveOurShellex(@"Software\Classes\" + ext + @"\shellex\" + PreviewGuid, our);
                RemoveOurShellex(
                    @"Software\Classes\SystemFileAssociations\" + ext + @"\shellex\" + PreviewGuid, our);
            }

            foreach (var prog in new[]
            {
                @"Software\Classes\Applications\7zFM.exe\shellex\" + PreviewGuid,
                @"Software\Classes\ASC.Sheet.12\shellex\" + PreviewGuid,
                @"Software\Classes\ASC.Document.12\shellex\" + PreviewGuid,
                @"Software\Classes\ASC.Presentation.12\shellex\" + PreviewGuid,
                @"Software\Classes\ASC.Sheet.1\shellex\" + PreviewGuid,
                @"Software\Classes\ASC.Document.1\shellex\" + PreviewGuid
            })
            {
                RemoveOurShellex(prog, our);
            }

            foreach (var id in HandlerIds)
            {
                TryDeleteKey(Registry.CurrentUser, @"Software\Classes\CLSID\" + id);
                TryDeleteKey(Registry.CurrentUser, @"Software\Classes\AppID\" + id);
                try
                {
                    using (var ph = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\PreviewHandlers", true))
                    {
                        ph?.DeleteValue(id, false);
                    }
                }
                catch { }

                // HKLM — только если когда-то писали с правами
                try { TryDeleteKey(Registry.LocalMachine, @"Software\Classes\CLSID\" + id); } catch { }
                try
                {
                    using (var ph = Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\PreviewHandlers", true))
                    {
                        ph?.DeleteValue(id, false);
                    }
                }
                catch { }
            }
        }

        private static void RemoveOurShellex(string relativePath, HashSet<string> ourIds)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(relativePath, true))
                {
                    if (k == null) return;
                    var val = k.GetValue(null) as string;
                    if (val != null && ourIds.Contains(val))
                    {
                        // удаляем весь ключ shellex\{PreviewGuid}
                    }
                    else if (val != null)
                    {
                        return; // чужой handler
                    }
                }
                Registry.CurrentUser.DeleteSubKeyTree(relativePath, false);
            }
            catch { }
        }

        private static void TryDeleteKey(RegistryKey hive, string path)
        {
            try { hive.DeleteSubKeyTree(path, false); } catch { }
        }

        private static void SetShellex(string relativePath, string clsid)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(relativePath))
            {
                k.SetValue(null, clsid);
            }
        }

        private static void TuneClsid(RegistryKey hive, string clsid, string name)
        {
            using (var root = hive.OpenSubKey(@"Software\Classes\CLSID\" + clsid, true))
            {
                if (root == null) return;
                root.SetValue("AppID", PrevHostAppId);
                root.SetValue("DisableLowILProcessIsolation", 1, RegistryValueKind.DWord);
                root.SetValue("AutomaticallyPreviewUntrustedFiles", 1, RegistryValueKind.DWord);
                root.SetValue("EnablePreviewHandler", 1, RegistryValueKind.DWord);
                using (var inproc = root.OpenSubKey("InprocServer32", true))
                {
                    inproc?.SetValue("ThreadingModel", "Apartment");
                }
            }
        }

        private static void WriteUninstallInfo()
        {
            var setup = File.Exists(SetupCopyPath)
                ? SetupCopyPath
                : Assembly.GetExecutingAssembly().Location;

            long sizeKb = 0;
            try
            {
                if (Directory.Exists(InstallRoot))
                {
                    foreach (var f in Directory.GetFiles(InstallRoot, "*", SearchOption.AllDirectories))
                        sizeKb += new FileInfo(f).Length;
                    sizeKb = sizeKb / 1024;
                }
            }
            catch { }

            using (var k = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WinPreviewPlus"))
            {
                k.SetValue("DisplayName", ProductName);
                k.SetValue("Publisher", ProductName);
                k.SetValue("InstallLocation", InstallRoot);
                k.SetValue("DisplayVersion", Version);
                k.SetValue("DisplayIcon", setup);
                k.SetValue("UninstallString", "\"" + setup + "\" /uninstall");
                k.SetValue("QuietUninstallString", "\"" + setup + "\" /uninstall /silent");
                k.SetValue("ModifyPath", "\"" + setup + "\"");
                k.SetValue("NoModify", 0, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 0, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, sizeKb), RegistryValueKind.DWord);
            }
        }

        private static void RemoveUninstallInfo()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WinPreviewPlus", false);
            }
            catch { }
        }

        private static void CreateStartMenuEntries()
        {
            try
            {
                var programs = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    ProductName);
                Directory.CreateDirectory(programs);
                var setup = File.Exists(SetupCopyPath)
                    ? SetupCopyPath
                    : Assembly.GetExecutingAssembly().Location;

                File.WriteAllText(
                    Path.Combine(programs, "WinPreviewPlus.cmd"),
                    "@echo off\r\nstart \"\" \"" + setup + "\"\r\n",
                    Encoding.ASCII);
                File.WriteAllText(
                    Path.Combine(programs, "Uninstall WinPreviewPlus.cmd"),
                    "@echo off\r\nstart \"\" \"" + setup + "\" /uninstall\r\n",
                    Encoding.ASCII);
                File.WriteAllText(
                    Path.Combine(programs, "Repair WinPreviewPlus.cmd"),
                    "@echo off\r\nstart \"\" \"" + setup + "\" /install\r\n",
                    Encoding.ASCII);
            }
            catch (Exception ex) { Log("Start Menu: " + ex.Message); }
        }

        private static void RemoveStartMenuEntries()
        {
            try
            {
                var programs = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    ProductName);
                if (Directory.Exists(programs))
                    Directory.Delete(programs, true);
            }
            catch { }
        }

        private static void TryDeleteDirectory(string path)
        {
            if (!Directory.Exists(path)) return;
            try
            {
                foreach (var f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch { }
                }
                Directory.Delete(path, true);
            }
            catch (Exception ex) { Log("Удаление " + path + ": " + ex.Message); }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
            }
            catch { }
        }

        private static void ScheduleDeleteInstallRoot()
        {
            if (!Directory.Exists(InstallRoot)) return;
            try
            {
                // Удаляем каталог после выхода процесса (чтобы снять lock с Setup.exe)
                var cmd = "/c ping 127.0.0.1 -n 3 >nul & " +
                          "rmdir /s /q \"" + InstallRoot + "\"";
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = cmd,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                Log("Отложена очистка папки установки.");
            }
            catch (Exception ex) { Log("Schedule delete: " + ex.Message); }
        }

        private static void KillPrevhost()
        {
            foreach (var name in new[] { "prevhost", "PrevHost" })
            {
                try
                {
                    foreach (var p in Process.GetProcessesByName(name))
                    {
                        try { p.Kill(); } catch { }
                    }
                }
                catch { }
            }
        }

        private static void RestartExplorer()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("explorer"))
                {
                    try { p.Kill(); } catch { }
                }
                System.Threading.Thread.Sleep(700);
                Process.Start("explorer.exe");
            }
            catch (Exception ex) { Log("Explorer restart: " + ex.Message); }
        }

        private static void Run(string file, string args, string cwd)
        {
            // RegAsm и др. пишут в OEM (CP866 на RU) — без явной кодировки в логе кракозябры
            Encoding oem;
            try { oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
            catch { oem = Encoding.GetEncoding(866); }

            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                WorkingDirectory = cwd,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = oem,
                StandardErrorEncoding = oem
            };
            using (var p = Process.Start(psi))
            {
                var stdout = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(120000);
                LogConsoleOutput(stdout);
                LogConsoleOutput(stderr);
                if (p.ExitCode != 0)
                    throw new InvalidOperationException(
                        Path.GetFileName(file) + " exit " + p.ExitCode);
            }
        }

        private static void LogConsoleOutput(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                // шум копирайта RegAsm
                if (line.IndexOf("Microsoft (R)", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (line.IndexOf("Copyright (C)", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (line.StartsWith("для Microsoft .NET", StringComparison.OrdinalIgnoreCase)) continue;
                Log(line);
            }
        }

        private static class ShellNotify
        {
            [DllImport("shell32.dll")]
            public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
        }
    }
}
