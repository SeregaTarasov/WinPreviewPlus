using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SharpShell.Attributes;
using SharpShell.Interop;
using SharpShell.SharpPreviewHandler;

namespace WinPreviewPlus
{
    [ComVisible(true)]
    [Guid("B7E4A907-2C5D-4F8A-9E31-11A0C3D5E707")]
    [ClassInterface(ClassInterfaceType.None)]
    [PreviewHandler(DisableLowILProcessIsolation = true)]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".jpg")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".jpeg")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".jfif")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".png")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".gif")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".bmp")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".tif")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".tiff")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".webp")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".ico")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".svg")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".heic")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".heif")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".avif")]
    [DisplayName("WinPreviewPlus Image")]
    public class ImagePreviewHandler : SharpPreviewHandler, IInitializeWithFile, IPreviewHandler
    {
        // JPEG/PNG/HEIC; даунскейл больших растров. SVG — через WebView2.
        private const long MaxBytes = 256L * 1024 * 1024;
        private string _filePath;

        static ImagePreviewHandler()
        {
            PreviewAssemblyResolve.Ensure();
        }

        int IInitializeWithFile.Initialize(string pszFilePath, STGM grfMode)
        {
            PreviewAssemblyResolve.Ensure();
            _filePath = pszFilePath;
            PreviewLog.Info("ImagePreviewHandler IInitializeWithFile: " + pszFilePath);
            return WinError.S_OK;
        }

        protected override PreviewHandlerControl DoPreview()
        {
            PreviewAssemblyResolve.Ensure();
            var control = new ImagePreviewControl();
            try
            {
                var path = !string.IsNullOrEmpty(_filePath) ? _filePath : SelectedFilePath;
                var source = PreviewSource.FromFilePath(path, MaxBytes);
                control.LoadImage(source);
            }
            catch (Exception ex)
            {
                PreviewLog.Error("ImagePreviewHandler.DoPreview", ex);
            }
            return control;
        }

        int IPreviewHandler.TranslateAccelerator(MSG pmsg) =>
            PreviewAccel.Translate(this, pmsg, (key, ctrl) =>
            {
                var img = PreviewAccel.GetControl(this) as ImagePreviewControl;
                if (img == null) return null;
                // клавиши зума обрабатывает контрол через кнопки; колесо — MouseWheel
                return null;
            });
    }
}
