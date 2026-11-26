using System;
using System.Runtime.InteropServices;
using SharpShell.Interop;
using SharpShell.SharpPreviewHandler;

namespace WinPreviewPlus
{
    /// <summary>
    /// Общий IInitializeWithFile + загрузка файла: семь COM-классов отличаются только
    /// GUID/ассоциациями и вызовом Load* на контроле.
    /// </summary>
    [ComVisible(false)]
    public abstract class FilePreviewHandler : SharpPreviewHandler
    {
        private string _filePath;

        protected FilePreviewHandler()
        {
            PreviewAssemblyResolve.Ensure();
        }

        protected abstract long MaxBytes { get; }

        protected string FilePath =>
            !string.IsNullOrEmpty(_filePath) ? _filePath : SelectedFilePath;

        protected int InitializeFile(string pszFilePath)
        {
            PreviewAssemblyResolve.Ensure();
            _filePath = pszFilePath;
            PreviewLog.Info(GetType().Name + " IInitializeWithFile: " + pszFilePath);
            return WinError.S_OK;
        }

        protected PreviewHandlerControl LoadControl<TControl>(Action<TControl, PreviewSource> load)
            where TControl : PreviewHandlerControl, new()
        {
            PreviewAssemblyResolve.Ensure();
            var control = new TControl();
            try
            {
                var source = PreviewSource.FromFilePath(FilePath, MaxBytes);
                load(control, source);
            }
            catch (Exception ex)
            {
                PreviewUi.ShowLoadFailure(control, ex, GetType().Name + ".DoPreview");
            }
            return control;
        }
    }
}
