namespace FTP_WebApp.Models;

public class MediaOptions
{
    public string ThumbnailCachePath { get; set; } = "ThumbnailCache";
    public string FfmpegPath { get; set; } = "ffmpeg";
    public int ThumbnailQuality { get; set; } = 78;
    public int PreviewQuality { get; set; } = 100;
    public int MaxConcurrentGenerators { get; set; } = 8;
}

public record ThumbnailFile(string FilePath, string ContentType, string ETag);
