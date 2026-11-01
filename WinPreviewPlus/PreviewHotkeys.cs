using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinPreviewPlus
{
    /// <summary>
    /// Ctrl+F / F3 в prevhost: ProcessCmdKey часто не срабатывает — фильтр по hwnd сообщения.
    /// </summary>
    internal sealed class PreviewHotkeys : IMessageFilter, IDisposable
    {
        private const int WmKeyDown = 0x0100;
        private const int WmSysKeyDown = 0x0104;
        private const int GaRoot = 2;

        private readonly Control _root;
        private readonly Action _onFind;
        private readonly Action<bool> _onFindNext;
        private readonly Action _onEscape;
        /// <summary>Вернуть true — съесть клавишу (напр. Backspace, чтобы Проводник не ушёл «назад»).</summary>
        private readonly Func<Keys, bool> _handleKey;
        private bool _installed;

        public PreviewHotkeys(
            Control root,
            Action onFind = null,
            Action<bool> onFindNext = null,
            Action onEscape = null,
            Func<Keys, bool> handleKey = null)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _onFind = onFind;
            _onFindNext = onFindNext;
            _onEscape = onEscape;
            _handleKey = handleKey;
            _root.HandleCreated += (s, e) => Install();
            _root.HandleDestroyed += (s, e) => Uninstall();
            if (_root.IsHandleCreated) Install();
        }

        private void Install()
        {
            if (_installed) return;
            Application.AddMessageFilter(this);
            _installed = true;
            PreviewLog.Info("PreviewHotkeys installed");
        }

        private void Uninstall()
        {
            if (!_installed) return;
            Application.RemoveMessageFilter(this);
            _installed = false;
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WmKeyDown && m.Msg != WmSysKeyDown) return false;
            if (_root.IsDisposed || !_root.IsHandleCreated || !_root.Visible) return false;
            if (!IsMessageForUs(m.HWnd)) return false;

            var key = (Keys)((int)m.WParam & 0xFFFF);
            bool ctrl = (Control.ModifierKeys & Keys.Control) == Keys.Control;
            bool shift = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;

            if (ctrl && key == Keys.F && _onFind != null)
            {
                PreviewLog.Info("Hotkey Ctrl+F");
                _onFind.Invoke();
                return true;
            }
            if (key == Keys.F3 && _onFindNext != null)
            {
                _onFindNext.Invoke(!shift);
                return true;
            }
            if (key == Keys.Escape && _onEscape != null)
            {
                _onEscape.Invoke();
                return true;
            }
            // Backspace и пр. — до Проводника (иначе «назад» по истории папок)
            if (_handleKey != null && _handleKey(key))
                return true;
            return false;
        }

        private bool IsMessageForUs(IntPtr hwnd)
        {
            try
            {
                if (hwnd != IntPtr.Zero)
                {
                    if (hwnd == _root.Handle || IsChild(_root.Handle, hwnd))
                        return true;
                }

                IntPtr focus = GetFocus();
                if (focus != IntPtr.Zero && (focus == _root.Handle || IsChild(_root.Handle, focus)))
                    return true;

                // Фокус в том же top-level окне, что и наш превью (prevhost frame)
                if (_root.Visible && focus != IntPtr.Zero)
                {
                    IntPtr ourRoot = GetAncestor(_root.Handle, GaRoot);
                    IntPtr focusRoot = GetAncestor(focus, GaRoot);
                    if (ourRoot != IntPtr.Zero && ourRoot == focusRoot)
                        return true;
                }

                return _root.ContainsFocus;
            }
            catch
            {
                return false;
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, int gaFlags);

        public void Dispose() => Uninstall();
    }
}
