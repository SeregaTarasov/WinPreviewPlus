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
    [COMServerAssociation(AssociationType.ClassOfExtension, ".jpe")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".jfif")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".png")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".gif")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".bmp")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".dib")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".tif")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".tiff")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".webp")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".ico")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".cur")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".svg")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".svgz")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".emf")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".wmf")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".heic")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".heif")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".heics")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".hif")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".avif")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".jxr")]
    [DisplayName("WinPreviewPlus Image")]
    public class ImagePreviewHandler : FilePreviewHandler, IInitializeWithFile, IPreviewHandler
    {
        protected override long MaxBytes => 256L * 1024 * 1024;

        int IInitializeWithFile.Initialize(string pszFilePath, STGM grfMode) =>
            InitializeFile(pszFilePath);

        protected override PreviewHandlerControl DoPreview() =>
            LoadControl<ImagePreviewControl>((c, s) => c.LoadImage(s));

        int IPreviewHandler.TranslateAccelerator(MSG pmsg) =>
            PreviewAccel.Translate(this, pmsg, (key, ctrl) =>
            {
                var img = PreviewAccel.GetControl(this) as ImagePreviewControl;
                if (img == null) return null;
                return img.TryHandleKey(key, ctrl) ? WinError.S_OK : (int?)null;
            });
    }
}
