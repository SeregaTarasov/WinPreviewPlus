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
    [COMServerAssociation(AssociationType.ClassOfExtension, ".epub")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".jar")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".apk")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".nupkg")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".whl")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".vsix")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".cbz")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".cbr")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".war")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".ear")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".ipa")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".xpi")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".crx")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".appx")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".msix")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".kmz")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".tbz")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".tbz2")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".txz")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".lz")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".cbt")]
    [DisplayName("WinPreviewPlus Archive")]
    // Только File — иначе Explorer выбирает Stream и отдаёт копию без пути
    public class ArchivePreviewHandler : FilePreviewHandler, IInitializeWithFile, IPreviewHandler
    {
        protected override long MaxBytes => 512L * 1024 * 1024;

        int IInitializeWithFile.Initialize(string pszFilePath, STGM grfMode) =>
            InitializeFile(pszFilePath);

        protected override PreviewHandlerControl DoPreview() =>
            LoadControl<ArchivePreviewControl>((c, s) => c.LoadArchive(s));

        int IPreviewHandler.TranslateAccelerator(MSG pmsg) =>
            PreviewAccel.Translate(this, pmsg, (key, ctrl) =>
            {
                var archive = PreviewAccel.GetControl(this) as ArchivePreviewControl;
                if (archive == null) return null;
                if (!ctrl && key == System.Windows.Forms.Keys.Back)
                    return archive.TryGoUp() ? WinError.S_OK : (int?)null;
                return null;
            });
    }
}
