using FluentFTP;
using FTP_WebApp.Models;
using Microsoft.AspNetCore.StaticFiles;

namespace FTP_WebApp.Services;

public class RemoteFileService : IRemoteFileService
{
    private readonly IConnectionProfileService _profiles;
    private readonly ILogger<RemoteFileService> _logger;
    private readonly FileExtensionContentTypeProvider _contentTypes = new();

    public RemoteFileService(IConnectionProfileService profiles, ILogger<RemoteFileService> logger)
    {
        _profiles = profiles;
        _logger = logger;
    }

    public async Task TestConnectionAsync(FtpConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        await using var client = CreateClient(profile);
        await client.Connect(cancellationToken);
    }

    public async Task<IReadOnlyList<RemoteFileItem>> ListAsync(FtpConnectionProfile profile, string? path,
        CancellationToken cancellationToken = default)
    {
        var relativePath = NormalizeRelativePath(path);
        var remotePath = ToRemotePath(profile, relativePath);
        await using var client = CreateClient(profile);
        var stage = "connection/login";
        try
        {
            _logger.LogInformation("FTP connection starting. Profile {ProfileId}; host {Host}; port {Port}; security {SecurityMode}; encryption {EncryptionMode}.",
                profile.Id, profile.Host, profile.Port, profile.SecurityMode, client.Config.EncryptionMode);
            await client.Connect(cancellationToken);
            stage = "passive directory listing";
            _logger.LogInformation("FTP login succeeded for profile {ProfileId}. Starting passive directory listing.", profile.Id);
            var listing = await client.GetListing(remotePath, FtpListOption.Modify | FtpListOption.Size, cancellationToken);
            _logger.LogInformation("FTP listing succeeded for profile {ProfileId}: {ItemCount} entries.", profile.Id, listing.Length);

            return listing.Where(item => item.Type is FtpObjectType.File or FtpObjectType.Directory)
                      .Select(item => new RemoteFileItem(item.Name, CombineRelative(relativePath, item.Name),
                          item.Type == FtpObjectType.Directory, item.Size, item.Created, item.Modified,
                          Path.GetExtension(item.Name).ToLowerInvariant()))
                      .OrderByDescending(item => item.IsDirectory)
                       .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                       .ToList();
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or System.Net.Sockets.SocketException
            or System.Security.Authentication.AuthenticationException or FluentFTP.Exceptions.FtpException)
        {
            _logger.LogError(exception, "FTP profile {ProfileId} failed during {Stage}. Host {Host}; port {Port}; security {SecurityMode}.",
                profile.Id, stage, profile.Host, profile.Port, profile.SecurityMode);
            throw;
        }
    }

    public async Task<DownloadedRemoteFile> DownloadAsync(FtpConnectionProfile profile, string path,
        CancellationToken cancellationToken = default)
    {
        var relativePath = NormalizeRelativePath(path, false);
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"ftp-web-{Guid.NewGuid():N}.tmp");
        var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);

        try
        {
            await using var client = CreateClient(profile);
            await client.Connect(cancellationToken);
            var downloaded = await client.DownloadStream(stream, ToRemotePath(profile, relativePath), token: cancellationToken);
            if (!downloaded)
            {
                throw new IOException("The remote file could not be downloaded.");
            }

            stream.Position = 0;
            var fileName = GetName(relativePath);
            var contentType = _contentTypes.TryGetContentType(fileName, out var detected) ? detected : "application/octet-stream";
            return new DownloadedRemoteFile(stream, fileName, contentType);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    public async Task UploadAsync(FtpConnectionProfile profile, string? path, string fileName, Stream stream,
        CancellationToken cancellationToken = default)
    {
        ValidateName(fileName);
        var destination = ToRemotePath(profile, CombineRelative(NormalizeRelativePath(path), fileName));
        await using var client = CreateClient(profile);
        await client.Connect(cancellationToken);
        var status = await client.UploadStream(stream, destination, FtpRemoteExists.Overwrite, true, token: cancellationToken);
        if (status != FtpStatus.Success)
        {
            throw new IOException("The file could not be uploaded.");
        }
    }

    public async Task CreateFolderAsync(FtpConnectionProfile profile, string? path, string folderName,
        CancellationToken cancellationToken = default)
    {
        ValidateName(folderName);
        var destination = ToRemotePath(profile, CombineRelative(NormalizeRelativePath(path), folderName));
        await using var client = CreateClient(profile);
        await client.Connect(cancellationToken);
        await client.CreateDirectory(destination, cancellationToken);
    }

    public async Task RenameAsync(FtpConnectionProfile profile, string path, string newName,
        CancellationToken cancellationToken = default)
    {
        ValidateName(newName);
        var source = NormalizeRelativePath(path, false);
        var destination = CombineRelative(GetParent(source), newName);
        await using var client = CreateClient(profile);
        await client.Connect(cancellationToken);
        await client.Rename(ToRemotePath(profile, source), ToRemotePath(profile, destination), cancellationToken);
    }

    public async Task MoveAsync(FtpConnectionProfile profile, string sourcePath, string destinationFolder, bool isDirectory,
        CancellationToken cancellationToken = default)
    {
        var source = NormalizeRelativePath(sourcePath, false);
        var destination = CombineRelative(NormalizeRelativePath(destinationFolder), GetName(source));
        await using var client = CreateClient(profile);
        await client.Connect(cancellationToken);

        if (isDirectory)
        {
            await client.MoveDirectory(ToRemotePath(profile, source), ToRemotePath(profile, destination), FtpRemoteExists.Skip,
                cancellationToken);
        }
        else
        {
            await client.MoveFile(ToRemotePath(profile, source), ToRemotePath(profile, destination), FtpRemoteExists.Skip,
                cancellationToken);
        }
    }

    public async Task DeleteAsync(FtpConnectionProfile profile, string path, bool isDirectory,
        CancellationToken cancellationToken = default)
    {
        var relativePath = NormalizeRelativePath(path, false);
        await using var client = CreateClient(profile);
        await client.Connect(cancellationToken);

        if (isDirectory)
        {
            await client.DeleteDirectory(ToRemotePath(profile, relativePath), cancellationToken);
        }
        else
        {
            await client.DeleteFile(ToRemotePath(profile, relativePath), cancellationToken);
        }
    }

    private AsyncFtpClient CreateClient(FtpConnectionProfile profile)
    {
        var config = new FtpConfig
        {
            EncryptionMode = profile.SecurityMode switch
            {
                FtpSecurityMode.ExplicitTls => FtpEncryptionMode.Explicit,
                FtpSecurityMode.ImplicitTls => FtpEncryptionMode.Implicit,
                _ => FtpEncryptionMode.None
            },
            DataConnectionType = FtpDataConnectionType.AutoPassive,
            ConnectTimeout = 15000,
            ReadTimeout = 30000,
            ValidateAnyCertificate = false
        };
        return new AsyncFtpClient(profile.Host, profile.Username, _profiles.UnprotectPassword(profile), profile.Port, config);
    }

    private static string ToRemotePath(FtpConnectionProfile profile, string relativePath)
    {
        return relativePath.Length == 0 ? profile.RootPath : $"{profile.RootPath.TrimEnd('/')}/{relativePath}";
    }

    private static string NormalizeRelativePath(string? path, bool allowEmpty = true)
    {
        var segments = (path ?? string.Empty).Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidOperationException("The requested path is invalid.");
        }

        foreach (var segment in segments)
        {
            ValidateName(segment);
        }

        var normalized = string.Join('/', segments);
        if (!allowEmpty && normalized.Length == 0)
        {
            throw new InvalidOperationException("A file or folder path is required.");
        }

        return normalized;
    }

    private static string CombineRelative(string path, string name) => path.Length == 0 ? name : $"{path}/{name}";

    private static string GetName(string path) => path.Split('/')[^1];

    private static string GetParent(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? string.Empty : path[..separator];
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(['/', '\\', '\0']) >= 0)
        {
            throw new InvalidOperationException("The file or folder name is invalid.");
        }
    }
}
