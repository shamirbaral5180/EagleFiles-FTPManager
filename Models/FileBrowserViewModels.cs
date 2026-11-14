namespace FTP_WebApp.Models;

public class FileBrowserViewModel
{
    public required FtpConnectionProfile Connection { get; init; }
    public string Path { get; init; } = string.Empty;
    public string ParentPath { get; init; } = string.Empty;
    public string Search { get; init; } = string.Empty;
    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; init; } = [];
    public IReadOnlyList<RemoteFileItem> Items { get; init; } = [];
}

public record BreadcrumbItem(string Name, string Path);

public record RemoteFileItem(string Name, string Path, bool IsDirectory, long Size, DateTime Created, DateTime Modified,
    string Extension)
{
    public string DisplaySize => IsDirectory ? "Folder" : FormatSize(Size);
    public string Category => FileTypeCatalog.GetCategory(Extension, IsDirectory);
    public string TypeLabel => FileTypeCatalog.GetTypeLabel(Extension, IsDirectory);
    public bool CanPreview => FileTypeCatalog.CanPreview(Extension);

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)Math.Max(bytes, 0);
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.#} {units[unit]}";
    }
}

public class FilePreviewViewModel
{
    public required FtpConnectionProfile Connection { get; init; }
    public required string Path { get; init; }
    public required string FileName { get; init; }
    public required string ParentPath { get; init; }
    public required string Category { get; init; }
    public required string TypeLabel { get; init; }
    public bool CanPreview { get; init; }
    public string Version { get; init; } = string.Empty;
}

public static class FileTypeCatalog
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".ico", ".avif", ".tif", ".tiff", ".heic",
            ".heif", ".dng", ".cr2", ".nef", ".arw", ".psd", ".psb", ".jp2", ".j2k", ".jxl", ".exr", ".hdr", ".tga",
            ".pcx", ".ppm", ".pgm", ".pbm", ".dds", ".xcf" };
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".webm", ".ogg", ".mov", ".m4v", ".avi", ".mkv", ".wmv", ".flv", ".mpeg", ".mpg", ".3gp", ".ts",
            ".mts", ".m2ts" };
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".ogg", ".m4a", ".aac", ".flac" };
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".md", ".json", ".xml", ".csv", ".log", ".yaml", ".yml", ".css", ".js", ".html", ".htm" };
    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".rtf" };
    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".zip", ".rar", ".7z", ".tar", ".gz" };

    public static string GetCategory(string extension, bool isDirectory = false)
    {
        if (isDirectory)
        {
            return "folder";
        }
        if (ImageExtensions.Contains(extension))
        {
            return "image";
        }
        if (VideoExtensions.Contains(extension))
        {
            return "video";
        }
        if (AudioExtensions.Contains(extension))
        {
            return "audio";
        }
        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return "pdf";
        }
        if (TextExtensions.Contains(extension))
        {
            return "text";
        }
        if (DocumentExtensions.Contains(extension))
        {
            return "document";
        }
        if (ArchiveExtensions.Contains(extension))
        {
            return "archive";
        }
        return "other";
    }

    public static string GetTypeLabel(string extension, bool isDirectory = false)
    {
        if (isDirectory)
        {
            return "Folder";
        }
        var category = GetCategory(extension);
        return category switch
        {
            "image" => "Image",
            "video" => "Video",
            "audio" => "Audio",
            "pdf" => "PDF document",
            "text" => "Text file",
            "document" => "Document",
            "archive" => "Archive",
            _ => string.IsNullOrWhiteSpace(extension) ? "File" : $"{extension.TrimStart('.').ToUpperInvariant()} file"
        };
    }

    public static bool CanPreview(string extension)
    {
        var category = GetCategory(extension);
        return category is "image" or "video" or "audio" or "pdf" or "text";
    }
}

public record DownloadedRemoteFile(Stream Stream, string FileName, string ContentType);
