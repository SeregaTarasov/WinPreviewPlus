using System.Runtime.InteropServices;
using SharpShell.Attributes;
using SharpShell.Interop;
using SharpShell.SharpPreviewHandler;

namespace WinPreviewPlus
{
    [ComVisible(true)]
    [Guid("B7E4A902-2C5D-4F8A-9E31-11A0C3D5E702")]
    [ClassInterface(ClassInterfaceType.None)]
    [PreviewHandler(DisableLowILProcessIsolation = true)]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".docx")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".docm")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".dotx")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".dotm")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".xlsx")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".xlsm")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".xltx")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".xltm")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".xls")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".xlsb")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".xlt")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".pptx")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".pptm")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".potx")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".potm")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".ppsx")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".ppsm")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".odt")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".ods")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".odp")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".odg")]
    [DisplayName("WinPreviewPlus Office")]
    public class OfficePreviewHandler : FilePreviewHandler, IInitializeWithFile, IPreviewHandler
    {
        protected override long MaxBytes => 128L * 1024 * 1024;

        int IInitializeWithFile.Initialize(string pszFilePath, STGM grfMode) =>
            InitializeFile(pszFilePath);

        protected override PreviewHandlerControl DoPreview() =>
            LoadControl<OfficePreviewControl>((c, s) => c.LoadOffice(s));

        int IPreviewHandler.TranslateAccelerator(MSG pmsg) =>
            PreviewAccel.Translate(this, pmsg, (key, ctrl) =>
            {
                var office = PreviewAccel.GetControl(this) as OfficePreviewControl;
                if (office == null) return null;

                if (ctrl && key == System.Windows.Forms.Keys.F)
                {
                    office.ShowFind();
                    return WinError.S_OK;
                }
                if (key == System.Windows.Forms.Keys.F3)
                {
                    bool shift = (System.Windows.Forms.Control.ModifierKeys
                                  & System.Windows.Forms.Keys.Shift) == System.Windows.Forms.Keys.Shift;
                    office.FindNext(forward: !shift);
                    return WinError.S_OK;
                }
                if (ctrl && key == System.Windows.Forms.Keys.C)
                    return office.TryCopySelection() ? WinError.S_OK : (int?)null;
                return null;
            });
    }
}
