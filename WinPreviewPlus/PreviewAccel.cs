using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SharpShell.Interop;
using SharpShell.SharpPreviewHandler;

namespace WinPreviewPlus
{
    /// <summary>
    /// Общий TranslateAccelerator: Backspace/Delete в полях ввода не отдаём Проводнику
    /// (иначе «назад» по папкам), но и не глотаем — S_FALSE → один раз в TextBox/RichTextBox.
    /// </summary>
    internal static class PreviewAccel
    {
        private const int WmKeyDown = 0x0100;
        private const int WmSysKeyDown = 0x0104;

        private static readonly FieldInfo ControlField = typeof(SharpPreviewHandler).GetField(
            "previewHandlerControl", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo FrameField = typeof(SharpPreviewHandler).GetField(
            "previewHandlerFrame", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <param name="onKey">
        /// Опционально: вернуть HRESULT если обработали (S_OK / S_FALSE), null — дальше общая логика / хост.
        /// </param>
        public static int Translate(SharpPreviewHandler handler, MSG pmsg, Func<Keys, bool, int?> onKey = null)
        {
            try
            {
                if (pmsg.message == WmKeyDown || pmsg.message == WmSysKeyDown)
                {
                    var key = (Keys)((int)pmsg.wParam & 0xFFFF);
                    bool ctrl = (Control.ModifierKeys & Keys.Control) == Keys.Control;

                    if (onKey != null)
                    {
                        var custom = onKey(key, ctrl);
                        if (custom.HasValue)
                            return custom.Value;
                    }

                    var ctrlHost = GetControl(handler);

                    // Ctrl+F / F3 — не отдаём Проводнику (иначе хост глотает, наш поиск молчит)
                    if (ctrlHost is IPreviewFindHost find)
                    {
                        if (ctrl && key == Keys.F)
                        {
                            find.ShowFind();
                            return WinError.S_OK;
                        }
                        if (key == Keys.F3)
                        {
                            bool shift = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
                            find.FindNext(forward: !shift);
                            return WinError.S_OK;
                        }
                    }

                    if (!ctrl && (key == Keys.Back || key == Keys.Delete))
                    {
                        if (IsTextInputActive(ctrlHost))
                            return WinError.S_FALSE;
                    }
                }
            }
            catch (Exception ex)
            {
                PreviewLog.Error("PreviewAccel.Translate", ex);
            }

            return Forward(handler, pmsg);
        }

        public static PreviewHandlerControl GetControl(SharpPreviewHandler handler)
        {
            return ControlField?.GetValue(handler) as PreviewHandlerControl;
        }

        public static int Forward(SharpPreviewHandler handler, MSG pmsg)
        {
            var frame = FrameField?.GetValue(handler) as IPreviewHandlerFrame;
            if (frame != null)
                return frame.TranslateAccelerator(pmsg);
            return WinError.S_FALSE;
        }

        /// <summary>Фокус в TextBox / RichTextBox (поиск, редактор текста и т.п.).</summary>
        public static bool IsTextInputActive(Control root)
        {
            if (root == null || root.IsDisposed) return false;

            if (root is TextPreviewControl text && text.IsEditorActive)
                return true;

            try
            {
                IntPtr focus = GetFocus();
                if (focus == IntPtr.Zero) return false;

                return HasFocusedTextBox(root, focus);
            }
            catch
            {
                return false;
            }
        }

        private static bool HasFocusedTextBox(Control root, IntPtr focus)
        {
            if (root is TextBoxBase box && box.IsHandleCreated)
            {
                if (focus == box.Handle || IsChild(box.Handle, focus))
                    return true;
            }

            foreach (Control child in root.Controls)
            {
                if (HasFocusedTextBox(child, focus))
                    return true;
            }

            return false;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);
    }
}
