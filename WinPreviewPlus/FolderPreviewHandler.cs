using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SharpShell.Attributes;
using SharpShell.Interop;
using SharpShell.SharpPreviewHandler;

namespace WinPreviewPlus
{
    [ComVisible(true)]
    [Guid("B7E4A908-2C5D-4F8A-9E31-11A0C3D5E708")]
    [ClassInterface(ClassInterfaceType.None)]
    [PreviewHandler(DisableLowILProcessIsolation = true)]
    [COMServerAssociation(AssociationType.Directory)]
    [COMServerAssociation(AssociationType.Folder)]
    [DisplayName("WinPreviewPlus Folder")]
    public class FolderPreviewHandler : SharpPreviewHandler, IInitializeWithFile, IPreviewHandler
    {
        private string _folderPath;

        static FolderPreviewHandler()
        {
            PreviewAssemblyResolve.Ensure();
            PreviewSource.PurgeStaleTemp();
        }

        int IInitializeWithFile.Initialize(string pszFilePath, STGM grfMode)
        {
            PreviewAssemblyResolve.Ensure();
            _folderPath = pszFilePath;
            PreviewLog.Info("FolderPreviewHandler IInitializeWithFile: " + pszFilePath);
            return WinError.S_OK;
        }

        protected override PreviewHandlerControl DoPreview()
        {
            PreviewAssemblyResolve.Ensure();
            var control = new FolderPreviewControl();
            try
            {
                var path = !string.IsNullOrEmpty(_folderPath) ? _folderPath : SelectedFilePath;
                control.LoadFolder(path);
            }
            catch (Exception ex)
            {
                PreviewUi.ShowLoadFailure(control, ex, "FolderPreviewHandler.DoPreview");
            }
            return control;
        }

        int IPreviewHandler.TranslateAccelerator(MSG pmsg) =>
            PreviewAccel.Translate(this, pmsg, (key, ctrl) =>
            {
                var folder = PreviewAccel.GetControl(this) as FolderPreviewControl;
                if (folder == null) return null;
                if (!ctrl && key == Keys.Back)
                    return folder.TryGoUp() ? WinError.S_OK : (int?)null;
                return null;
            });
    }
}
