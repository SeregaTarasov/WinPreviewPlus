using System;
using SharpShell;
using SharpShell.ServerRegistration;
using WinPreviewPlus;

namespace WinPreviewPlus.Register
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var uninstall = args != null && args.Length > 0 &&
                            string.Equals(args[0], "uninstall", StringComparison.OrdinalIgnoreCase);

            try
            {
                var servers = new ISharpShellServer[]
                {
                    new ArchivePreviewHandler(),
                    new OfficePreviewHandler(),
                    new PdfPreviewHandler(),
                    new TextPreviewHandler(),
                    new AudioPreviewHandler(),
                    new VideoPreviewHandler(),
                    new ImagePreviewHandler()
                };

                foreach (var server in servers)
                {
                    if (uninstall)
                    {
                        Console.WriteLine("Uninstall " + server.GetType().Name);
                        ServerRegistrationManager.UninstallServer(server, RegistrationType.OS64Bit);
                        try { ServerRegistrationManager.UninstallServer(server, RegistrationType.OS32Bit); } catch { /* ignore */ }
                    }
                    else
                    {
                        Console.WriteLine("Install " + server.GetType().Name);
                        ServerRegistrationManager.InstallServer(server, RegistrationType.OS64Bit, codeBase: true);
                    }
                }

                Console.WriteLine(uninstall ? "Uninstall done." : "Install done.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }
    }
}
