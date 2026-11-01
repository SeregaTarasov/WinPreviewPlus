using System;
using System.Runtime.InteropServices;
using SharpShell.Attributes;
using SharpShell.Interop;
using SharpShell.SharpPreviewHandler;

namespace WinPreviewPlus
{
    [ComVisible(true)]
    [Guid("B7E4A901-2C5D-4F8A-9E31-11A0C3D5E701")]
    [ClassInterface(ClassInterfaceType.None)]
    [PreviewHandler(DisableLowILProcessIsolation = true)]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".zip")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".7z")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".rar")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".tar")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".tgz")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".gz")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".bz2")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".xz")]
    [DisplayName("WinPreviewPlus Archive")]
    // Только File — иначе Explorer выбирает Stream и отдаёт копию без пути
    public class ArchivePreviewHandler : SharpPreviewHandler, IInitializeWithFile, IPreviewHandler
    {
        private const long MaxBytes = 512L * 1024 * 1024;
        private string _filePath;

        static ArchivePreviewHandler()
        {
            PreviewAssemblyResolve.Ensure();
        }

        int IInitializeWithFile.Initialize(string pszFilePath, STGM grfMode)
        {
            PreviewAssemblyResolve.Ensure();
            _filePath = pszFilePath;
            PreviewLog.Info("ArchivePreviewHandler IInitializeWithFile: " + pszFilePath);
            return WinError.S_OK;
        }

        protected override PreviewHandlerControl DoPreview()
        {
            PreviewAssemblyResolve.Ensure();
            var control = new ArchivePreviewControl();
            try
            {
                var path = !string.IsNullOrEmpty(_filePath) ? _filePath : SelectedFilePath;
                var source = PreviewSource.FromFilePath(path, MaxBytes);
                control.LoadArchive(source);
            }
            catch (Exception ex)
            {
                PreviewLog.Error("ArchivePreviewHandler.DoPreview", ex);
            }
            return control;
        }

        int IPreviewHandler.TranslateAccelerator(MSG pmsg) =>
            PreviewAccel.Translate(this, pmsg);
    }
}
