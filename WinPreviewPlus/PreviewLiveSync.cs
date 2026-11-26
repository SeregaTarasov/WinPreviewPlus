using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WinPreviewPlus
{
    /// <summary>
    /// Обновляет превью, когда оригинал меняют другие программы,
    /// и перекладывает UI при смене разрешения / монитора.
    /// </summary>
    internal sealed class PreviewLiveSync : IDisposable
    {
        private readonly Control _host;
        private readonly Func<PreviewSource> _getSource;
        private readonly Func<string> _getWatchPath;
        private readonly Action _onFileChanged;
        private readonly Action _onDisplayChanged;
        private readonly Func<bool> _canReload;
        private FileSystemWatcher _watch;
        private Timer _debounce;
        private bool _disposed;

        public static PreviewLiveSync ForSource(
            Control host,
            Func<PreviewSource> getSource,
            Action onFileChanged,
            Action onDisplayChanged = null,
            Func<bool> canReload = null)
        {
            return new PreviewLiveSync(
                host,
                getSource,
                () => getSource()?.OriginalPath ?? getSource()?.Path,
                onFileChanged,
                onDisplayChanged,
                canReload);
        }

        public static PreviewLiveSync ForPath(
            Control host,
            Func<string> getPath,
            Action onFileChanged,
            Action onDisplayChanged = null)
        {
            return new PreviewLiveSync(host, null, getPath, onFileChanged, onDisplayChanged, null);
        }

        private PreviewLiveSync(
            Control host,
            Func<PreviewSource> getSource,
            Func<string> getWatchPath,
            Action onFileChanged,
            Action onDisplayChanged,
            Func<bool> canReload)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _getSource = getSource;
            _getWatchPath = getWatchPath;
            _onFileChanged = onFileChanged;
            _onDisplayChanged = onDisplayChanged;
            _canReload = canReload;

            _debounce = new Timer { Interval = 800 };
            _debounce.Tick += (s, e) =>
            {
                _debounce.Stop();
                FireFile();
            };

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            ArmWatcher();
        }

        public void RebindWatcher()
        {
            if (_disposed) return;
            ArmWatcher();
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e) => QueueDisplay();

        private void QueueDisplay()
        {
            if (_disposed || _onDisplayChanged == null) return;
            Ui(() =>
            {
                try { _onDisplayChanged(); }
                catch (Exception ex) { PreviewLog.Error("PreviewLiveSync display", ex); }
            });
        }

        private void ArmWatcher()
        {
            DropWatcher();
            string path = null;
            try { path = _getWatchPath?.Invoke(); } catch { return; }
            if (string.IsNullOrEmpty(path)) return;

            string dir;
            string filter;
            bool isDir = Directory.Exists(path);
            if (isDir)
            {
                dir = path;
                filter = "*.*";
            }
            else
            {
                dir = Path.GetDirectoryName(path);
                filter = Path.GetFileName(path);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(filter) || !Directory.Exists(dir))
                    return;
            }

            try
            {
                _watch = new FileSystemWatcher(dir)
                {
                    Filter = isDir ? "*.*" : filter,
                    IncludeSubdirectories = false,
                    InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                                   NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
                };
                _watch.Changed += OnFs;
                _watch.Created += OnFs;
                _watch.Deleted += OnFs;
                _watch.Renamed += OnFs;
                _watch.Error += OnWatchError;
                _watch.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                PreviewLog.Error("PreviewLiveSync watch " + path, ex);
            }
        }

        private void OnFs(object sender, FileSystemEventArgs e)
        {
            if (_disposed) return;
            if (IsNoiseName(e?.Name)) return;
            Ui(() =>
            {
                if (_debounce == null) return;
                _debounce.Stop();
                _debounce.Start();
            });
        }

        /// <summary>
        /// При переполнении внутреннего буфера watcher перестаёт слать события —
        /// без этого обработчика live-обновление умирает молча. Пере-вооружаемся.
        /// </summary>
        private void OnWatchError(object sender, ErrorEventArgs e)
        {
            if (_disposed) return;
            try { PreviewLog.Error("PreviewLiveSync watcher error", e?.GetException()); } catch { }
            Ui(ArmWatcher);
        }

        private void FireFile()
        {
            if (_disposed) return;
            if (_canReload != null)
            {
                try { if (!_canReload()) return; }
                catch { return; }
            }

            var src = _getSource?.Invoke();
            if (src != null && !src.RefreshFromOriginal())
            {
                PreviewUi.ShowLoadFailure(_host,
                    new FileNotFoundException("Файл изменён или удалён", src.OriginalPath),
                    "PreviewLiveSync");
                return;
            }

            try { _onFileChanged?.Invoke(); }
            catch (Exception ex) { PreviewLog.Error("PreviewLiveSync reload", ex); }

            ArmWatcher();
        }

        private void Ui(Action a)
        {
            if (_host == null || _host.IsDisposed) return;
            try
            {
                if (_host.IsHandleCreated && _host.InvokeRequired)
                    _host.BeginInvoke(a);
                else
                    a();
            }
            catch { /* disposed */ }
        }

        private static bool IsNoiseName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("~$", StringComparison.Ordinal)) return true;
            return false;
        }

        private void DropWatcher()
        {
            if (_watch == null) return;
            try
            {
                _watch.EnableRaisingEvents = false;
                _watch.Changed -= OnFs;
                _watch.Created -= OnFs;
                _watch.Deleted -= OnFs;
                _watch.Renamed -= OnFs;
                _watch.Error -= OnWatchError;
                _watch.Dispose();
            }
            catch { }
            _watch = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; } catch { }
            if (_debounce != null)
            {
                try { _debounce.Stop(); _debounce.Dispose(); } catch { }
                _debounce = null;
            }
            DropWatcher();
        }
    }
}
