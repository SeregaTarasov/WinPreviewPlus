using System;
using System.Collections.Generic;

namespace WinPreviewPlus
{
    /// <summary>
    /// CLSID обработчиков и расширения для HKCU shellex.
    /// COM-атрибуты на *Handler остаются (SharpShell), списки установщика — отсюда.
    /// </summary>
    internal static class PreviewCatalog
    {
        public const string PreviewHandlerCategory = "{8895b1c6-b41f-4c1c-a562-0d564250836f}";
        public const string PrevHostAppId = "{6d2b5079-2f0b-48dd-ab7f-97cec514d30b}";

        public const string Arch = "{B7E4A901-2C5D-4F8A-9E31-11A0C3D5E701}";
        public const string Off = "{B7E4A902-2C5D-4F8A-9E31-11A0C3D5E702}";
        public const string Pdf = "{B7E4A903-2C5D-4F8A-9E31-11A0C3D5E703}";
        public const string Txt = "{B7E4A904-2C5D-4F8A-9E31-11A0C3D5E704}";
        public const string Aud = "{B7E4A905-2C5D-4F8A-9E31-11A0C3D5E705}";
        public const string Vid = "{B7E4A906-2C5D-4F8A-9E31-11A0C3D5E706}";
        public const string Img = "{B7E4A907-2C5D-4F8A-9E31-11A0C3D5E707}";
        public const string Dir = "{B7E4A908-2C5D-4F8A-9E31-11A0C3D5E708}";

        public static readonly string[] HandlerIds =
        {
            Arch, Off, Pdf, Txt, Aud, Vid, Img, Dir
        };

        public static readonly string[] DirectoryClasses =
        {
            "Directory", "Folder"
        };

        public static readonly string[] ArchiveExts =
        {
            ".zip", ".7z", ".rar", ".tar", ".tgz", ".gz", ".bz2", ".xz",
            ".epub", ".jar", ".apk", ".nupkg", ".whl", ".vsix", ".cbz", ".cbr",
            ".war", ".ear", ".ipa", ".xpi", ".crx", ".appx", ".msix", ".kmz",
            ".tbz", ".tbz2", ".txz", ".lz", ".cbt"
        };

        public static readonly string[] OfficeExts =
        {
            ".docx", ".docm", ".dotx", ".dotm",
            ".xlsx", ".xlsm", ".xltx", ".xltm", ".xls", ".xlsb", ".xlt",
            ".pptx", ".pptm", ".potx", ".potm", ".ppsx", ".ppsm",
            ".odt", ".ods", ".odp", ".odg"
        };

        public static readonly string[] PdfExts = { ".pdf" };

        public static readonly string[] TextExts =
        {
            ".txt", ".log", ".md", ".markdown", ".mdown", ".mdx", ".mdc",
            ".csv", ".tsv", ".json", ".geojson", ".jsonl", ".ndjson", ".jsonc", ".ipynb",
            ".xml", ".plist", ".mobileconfig", ".entitlements", ".strings", ".xcconfig",
            ".pbxproj", ".podspec", ".ini", ".cfg", ".conf", ".toml", ".env",
            ".yml", ".yaml", ".ps1", ".bat", ".cmd", ".cs", ".js", ".ts", ".tsx", ".jsx",
            ".py", ".sql", ".swift", ".m", ".mm", ".h", ".hpp", ".c", ".cpp", ".rs", ".go",
            ".rb", ".php", ".sh", ".bash", ".zsh", ".html", ".htm", ".css", ".scss", ".less",
            ".editorconfig", ".gpx", ".kml", ".graphql", ".properties", ".gradle",
            ".gitignore", ".dockerignore",
            ".rtf", ".csproj", ".sln", ".vbproj", ".vb", ".fs", ".java", ".kt", ".lua",
            ".psm1", ".psd1", ".vue", ".srt", ".vtt", ".ics", ".diff", ".patch",
            ".proto", ".dart", ".resx", ".config", ".nuspec", ".cshtml",
            ".razor", ".adoc", ".reg",
            ".tf", ".hcl", ".cmake", ".mk", ".asm", ".svelte", ".zig", ".ex", ".exs",
            ".wsdl", ".xsd", ".xsl", ".xslt", ".ps1xml", ".lock", ".npmrc", ".map",
            ".http", ".rst", ".org", ".tex", ".bib", ".fodt", ".fods", ".fodp"
        };

        public static readonly string[] AudioExts =
        {
            ".mp3", ".wav", ".flac", ".m4a", ".m4r", ".aac", ".wma", ".ogg", ".opus",
            ".aiff", ".aif", ".oga", ".mpga", ".weba"
        };

        public static readonly string[] VideoExts =
        {
            ".mp4", ".m4v", ".mov", ".avi", ".wmv", ".mkv", ".webm", ".mpeg", ".mpg",
            ".3gp", ".ogv", ".3g2", ".asf"
        };

        public static readonly string[] ImageExts =
        {
            ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".bmp", ".dib",
            ".tif", ".tiff", ".webp", ".ico", ".cur",
            ".svg", ".svgz", ".emf", ".wmf",
            ".heic", ".heif", ".heics", ".hif", ".avif", ".jxr"
        };

        public static Dictionary<string, string> BuildExtMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Add(map, ArchiveExts, Arch);
            Add(map, OfficeExts, Off);
            Add(map, PdfExts, Pdf);
            Add(map, TextExts, Txt);
            Add(map, AudioExts, Aud);
            Add(map, VideoExts, Vid);
            Add(map, ImageExts, Img);
            return map;
        }

        private static void Add(Dictionary<string, string> map, string[] exts, string handler)
        {
            foreach (var e in exts)
                map[e] = handler;
        }
    }
}
