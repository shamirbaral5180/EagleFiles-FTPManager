using FTP_WebApp.Models;

namespace FTP_WebApp.Services;

public interface IRemoteFileService
{
    Task TestConnectionAsync(FtpConnectionProfile profile, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemoteFileItem>> ListAsync(FtpConnectionProfile profile, string? path,
        CancellationToken cancellationToken = default);
    Task<DownloadedRemoteFile> DownloadAsync(FtpConnectionProfile profile, string path,
        CancellationToken cancellationToken = default);
    Task UploadAsync(FtpConnectionProfile profile, string? path, string fileName, Stream stream,
        CancellationToken cancellationToken = default);
    Task CreateFolderAsync(FtpConnectionProfile profile, string? path, string folderName,
        CancellationToken cancellationToken = default);
    Task RenameAsync(FtpConnectionProfile profile, string path, string newName,
        CancellationToken cancellationToken = default);
    Task MoveAsync(FtpConnectionProfile profile, string sourcePath, string destinationFolder, bool isDirectory,
        CancellationToken cancellationToken = default);
    Task DeleteAsync(FtpConnectionProfile profile, string path, bool isDirectory,
        CancellationToken cancellationToken = default);
}
