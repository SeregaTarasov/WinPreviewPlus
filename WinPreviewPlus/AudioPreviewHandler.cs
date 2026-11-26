using System.Runtime.InteropServices;
using System.Windows.Forms;
using SharpShell.Attributes;
using SharpShell.Interop;
using SharpShell.SharpPreviewHandler;

namespace WinPreviewPlus
{
    [ComVisible(true)]
    [Guid("B7E4A905-2C5D-4F8A-9E31-11A0C3D5E705")]
    [ClassInterface(ClassInterfaceType.None)]
    [PreviewHandler(DisableLowILProcessIsolation = true)]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".mp3")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".wav")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".flac")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".m4a")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".m4r")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".aac")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".wma")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".ogg")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".opus")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".aiff")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".aif")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".oga")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".mpga")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".weba")]
    [DisplayName("WinPreviewPlus Audio")]
    public class AudioPreviewHandler : FilePreviewHandler, IInitializeWithFile, IPreviewHandler
    {
        protected override long MaxBytes => 512L * 1024 * 1024;

        int IInitializeWithFile.Initialize(string pszFilePath, STGM grfMode) =>
            InitializeFile(pszFilePath);

        protected override PreviewHandlerControl DoPreview() =>
            LoadControl<AudioPreviewControl>((c, s) => c.LoadAudio(s));

        int IPreviewHandler.TranslateAccelerator(MSG pmsg) =>
            PreviewAccel.Translate(this, pmsg, (key, ctrl) =>
            {
                var audio = PreviewAccel.GetControl(this) as AudioPreviewControl;
                if (audio == null) return null;
                if (!ctrl && key == Keys.Space)
                {
                    audio.TogglePlayPause();
                    return WinError.S_OK;
                }
                if (!ctrl && (key == Keys.Left || key == Keys.Right))
                {
                    audio.SeekBySeconds(key == Keys.Right ? 5 : -5);
                    return WinError.S_OK;
                }
                return null;
            });
    }
}
