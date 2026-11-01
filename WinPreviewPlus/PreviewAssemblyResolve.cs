using System;
using System.IO;
using System.Reflection;

namespace WinPreviewPlus
{
    /// <summary>
    /// prevhost.exe has no binding redirects for our NuGet deps.
    /// Resolve any requested version from our deploy folder.
    /// </summary>
    internal static class PreviewAssemblyResolve
    {
        private static bool _hooked;
        private static readonly object Gate = new object();

        public static void Ensure()
        {
            lock (Gate)
            {
                if (_hooked) return;
                _hooked = true;
                AppDomain.CurrentDomain.AssemblyResolve += OnResolve;
                PreviewLog.Info("AssemblyResolve hooked; base=" +
                    Path.GetDirectoryName(typeof(PreviewAssemblyResolve).Assembly.Location));
            }
        }

        private static Assembly OnResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                var requested = new AssemblyName(args.Name);
                if (string.IsNullOrEmpty(requested.Name))
                    return null;

                // Don't recurse on resources
                if (requested.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                    return null;

                var dir = Path.GetDirectoryName(typeof(PreviewAssemblyResolve).Assembly.Location);
                if (string.IsNullOrEmpty(dir))
                    return null;

                var path = Path.Combine(dir, requested.Name + ".dll");
                if (!File.Exists(path))
                    return null;

                PreviewLog.Info("Resolve " + requested.Name + " -> " + path);
                return Assembly.LoadFrom(path);
            }
            catch (Exception ex)
            {
                PreviewLog.Error("AssemblyResolve failed for " + args.Name, ex);
                return null;
            }
        }
    }
}
