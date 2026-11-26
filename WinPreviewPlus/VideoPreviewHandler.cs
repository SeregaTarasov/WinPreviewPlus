using System.Runtime.InteropServices;
using System.Windows.Forms;
using SharpShell.Attributes;
using SharpShell.Interop;
using SharpShell.SharpPreviewHandler;

namespace WinPreviewPlus
{
    [ComVisible(true)]
    [Guid("B7E4A906-2C5D-4F8A-9E31-11A0C3D5E706")]
    [ClassInterface(ClassInterfaceType.None)]
    [PreviewHandler(DisableLowILProcessIsolation = true)]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".mp4")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".m4v")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".mov")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".avi")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".wmv")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".mkv")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".webm")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".mpeg")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".mpg")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".3gp")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".ogv")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".3g2")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".asf")]
    [DisplayName("WinPreviewPlus Video")]
    public class VideoPreviewHandler : FilePreviewHandler, IInitializeWithFile, IPreviewHandler
    {
        protected override long MaxBytes => 8L * 1024 * 1024 * 1024;

        int IInitializeWithFile.Initialize(string pszFilePath, STGM grfMode) =>
            InitializeFile(pszFilePath);

        protected override PreviewHandlerControl DoPreview() =>
            LoadControl<VideoPreviewControl>((c, s) => c.LoadVideo(s));

        int IPreviewHandler.TranslateAccelerator(MSG pmsg) =>
            PreviewAccel.Translate(this, pmsg, (key, ctrl) =>
            {
                var video = PreviewAccel.GetControl(this) as VideoPreviewControl;
                if (video == null) return null;
                if (!ctrl && key == Keys.Space)
                {
                    video.TogglePlayPause();
                    return WinError.S_OK;
                }
                if (!ctrl && (key == Keys.Left || key == Keys.Right))
                {
                    video.SeekBySeconds(key == Keys.Right ? 5 : -5);
                    return WinError.S_OK;
                }
                return null;
            });
    }
}
