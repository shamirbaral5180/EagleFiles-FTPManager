using FTP_WebApp.Models;

namespace FTP_WebApp.Services;

public interface IThumbnailService
{
    Task<ThumbnailFile?> GetAsync(FtpConnectionProfile profile, string path, int width, string? version,
        CancellationToken cancellationToken = default);
}
