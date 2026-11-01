using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SharpShell.Attributes;
using SharpShell.Interop;
using SharpShell.SharpPreviewHandler;

namespace WinPreviewPlus
{
    /// <summary>
    /// COM-ассоциации только «безопасные» расширения (HKCR без админа).
    /// Остальные (.html, .xml, .ps1, …) вешает установщик в HKCU.
    /// </summary>
    [ComVisible(true)]
    [Guid("B7E4A904-2C5D-4F8A-9E31-11A0C3D5E704")]
    [ClassInterface(ClassInterfaceType.None)]
    [PreviewHandler(DisableLowILProcessIsolation = true)]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".txt")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".log")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".md")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".csv")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".json")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".ini")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".cfg")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".conf")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".yml")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".yaml")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".editorconfig")]
    [DisplayName("WinPreviewPlus Text")]
    public class TextPreviewHandler : SharpPreviewHandler, IInitializeWithFile, IPreviewHandler
    {
        private const long MaxBytes = 32L * 1024 * 1024;
        private string _filePath;

        static TextPreviewHandler()
        {
            PreviewAssemblyResolve.Ensure();
        }

        int IInitializeWithFile.Initialize(string pszFilePath, STGM grfMode)
        {
            PreviewAssemblyResolve.Ensure();
            _filePath = pszFilePath;
            PreviewLog.Info("TextPreviewHandler IInitializeWithFile: " + pszFilePath);
            return WinError.S_OK;
        }

        protected override PreviewHandlerControl DoPreview()
        {
            PreviewAssemblyResolve.Ensure();
            var control = new TextPreviewControl();
            try
            {
                var path = !string.IsNullOrEmpty(_filePath) ? _filePath : SelectedFilePath;
                var source = PreviewSource.FromFilePath(path, MaxBytes);
                control.LoadText(source);
            }
            catch (Exception ex)
            {
                PreviewLog.Error("TextPreviewHandler.DoPreview", ex);
            }
            return control;
        }

        int IPreviewHandler.TranslateAccelerator(MSG pmsg)
        {
            return PreviewAccel.Translate(this, pmsg, (key, ctrl) =>
            {
                var text = PreviewAccel.GetControl(this) as TextPreviewControl;
                if (text == null) return null;

                if (ctrl && key == Keys.S)
                {
                    text.TrySaveFromHost();
                    return WinError.S_OK;
                }
                if (ctrl && key == Keys.A)
                {
                    if (text.TrySelectAllFromHost())
                        return WinError.S_OK;
                }
                return null;
            });
        }
    }
}
