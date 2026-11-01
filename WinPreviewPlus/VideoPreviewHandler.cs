using System;
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
    [DisplayName("WinPreviewPlus Video")]
    public class VideoPreviewHandler : SharpPreviewHandler, IInitializeWithFile, IPreviewHandler
    {
        private const long MaxBytes = 8L * 1024 * 1024 * 1024; // 8 GB
        private string _filePath;

        static VideoPreviewHandler()
        {
            PreviewAssemblyResolve.Ensure();
        }

        int IInitializeWithFile.Initialize(string pszFilePath, STGM grfMode)
        {
            PreviewAssemblyResolve.Ensure();
            _filePath = pszFilePath;
            PreviewLog.Info("VideoPreviewHandler IInitializeWithFile: " + pszFilePath);
            return WinError.S_OK;
        }

        protected override PreviewHandlerControl DoPreview()
        {
            PreviewAssemblyResolve.Ensure();
            var control = new VideoPreviewControl();
            try
            {
                var path = !string.IsNullOrEmpty(_filePath) ? _filePath : SelectedFilePath;
                var source = PreviewSource.FromFilePath(path, MaxBytes);
                control.LoadVideo(source);
            }
            catch (Exception ex)
            {
                PreviewLog.Error("VideoPreviewHandler.DoPreview", ex);
            }
            return control;
        }

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
                return null;
            });
    }
}
