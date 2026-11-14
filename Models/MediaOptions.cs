namespace FTP_WebApp.Models;

public class MediaOptions
{
    public string ThumbnailCachePath { get; set; } = "App_Data/ThumbnailCache";
    public string FfmpegPath { get; set; } = "ffmpeg";
    public int ThumbnailQuality { get; set; } = 78;
    public int MaxConcurrentGenerators { get; set; } = 2;
}

public record ThumbnailFile(string FilePath, string ContentType, string ETag);
