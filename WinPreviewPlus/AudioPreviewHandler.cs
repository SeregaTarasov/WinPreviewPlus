using System;
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
    [DisplayName("WinPreviewPlus Audio")]
    public class AudioPreviewHandler : SharpPreviewHandler, IInitializeWithFile, IPreviewHandler
    {
        private const long MaxBytes = 512L * 1024 * 1024;
        private string _filePath;

        static AudioPreviewHandler()
        {
            PreviewAssemblyResolve.Ensure();
        }

        int IInitializeWithFile.Initialize(string pszFilePath, STGM grfMode)
        {
            PreviewAssemblyResolve.Ensure();
            _filePath = pszFilePath;
            PreviewLog.Info("AudioPreviewHandler IInitializeWithFile: " + pszFilePath);
            return WinError.S_OK;
        }

        protected override PreviewHandlerControl DoPreview()
        {
            PreviewAssemblyResolve.Ensure();
            var control = new AudioPreviewControl();
            try
            {
                var path = !string.IsNullOrEmpty(_filePath) ? _filePath : SelectedFilePath;
                var source = PreviewSource.FromFilePath(path, MaxBytes);
                control.LoadAudio(source);
            }
            catch (Exception ex)
            {
                PreviewLog.Error("AudioPreviewHandler.DoPreview", ex);
            }
            return control;
        }

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
                return null;
            });
    }
}
