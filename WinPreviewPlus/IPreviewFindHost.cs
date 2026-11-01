namespace WinPreviewPlus
{
    /// <summary>Ctrl+F / F3 через IPreviewHandler.TranslateAccelerator.</summary>
    internal interface IPreviewFindHost
    {
        void ShowFind();
        void FindNext(bool forward);
    }
}
