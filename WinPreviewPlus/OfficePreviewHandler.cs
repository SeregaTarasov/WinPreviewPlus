using System;
using System.Runtime.InteropServices;
using SharpShell.Attributes;
using SharpShell.Interop;
using SharpShell.SharpPreviewHandler;

namespace WinPreviewPlus
{
    [ComVisible(true)]
    [Guid("B7E4A902-2C5D-4F8A-9E31-11A0C3D5E702")]
    [ClassInterface(ClassInterfaceType.None)]
    [PreviewHandler(DisableLowILProcessIsolation = true)]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".docx")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".xlsx")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".pptx")]
    [DisplayName("WinPreviewPlus Office")]
    public class OfficePreviewHandler : SharpPreviewHandler, IInitializeWithFile, IPreviewHandler
    {
        private const long MaxBytes = 128L * 1024 * 1024;
        private string _filePath;

        static OfficePreviewHandler()
        {
            PreviewAssemblyResolve.Ensure();
        }

        int IInitializeWithFile.Initialize(string pszFilePath, STGM grfMode)
        {
            PreviewAssemblyResolve.Ensure();
            _filePath = pszFilePath;
            PreviewLog.Info("OfficePreviewHandler IInitializeWithFile: " + pszFilePath);
            return WinError.S_OK;
        }

        protected override PreviewHandlerControl DoPreview()
        {
            PreviewAssemblyResolve.Ensure();
            var control = new OfficePreviewControl();
            try
            {
                var path = !string.IsNullOrEmpty(_filePath) ? _filePath : SelectedFilePath;
                var source = PreviewSource.FromFilePath(path, MaxBytes);
                control.LoadOffice(source);
            }
            catch (Exception ex)
            {
                PreviewLog.Error("OfficePreviewHandler.DoPreview", ex);
            }
            return control;
        }

        int IPreviewHandler.TranslateAccelerator(MSG pmsg) =>
            PreviewAccel.Translate(this, pmsg, (key, ctrl) =>
            {
                var office = PreviewAccel.GetControl(this) as OfficePreviewControl;
                if (office == null) return null;

                // Ctrl+F / F3 — явно (иначе Проводник глотает до ProcessCmdKey)
                if (ctrl && key == System.Windows.Forms.Keys.F)
                {
                    office.ShowFind();
                    return WinError.S_OK;
                }
                if (key == System.Windows.Forms.Keys.F3)
                {
                    bool shift = (System.Windows.Forms.Control.ModifierKeys
                                  & System.Windows.Forms.Keys.Shift) == System.Windows.Forms.Keys.Shift;
                    office.FindNext(forward: !shift);
                    return WinError.S_OK;
                }
                if (ctrl && key == System.Windows.Forms.Keys.C)
                    return office.TryCopySelection() ? WinError.S_OK : (int?)null;
                return null;
            });
    }
}
