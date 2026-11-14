namespace FTP_WebApp.Services;

public interface IStoragePathProvider
{
    string RootPath { get; }

    string DataProtectionKeyPath { get; }

    string LogPath { get; }

    bool UsingFallbackPath { get; }

    string ResolvePath(string path);
}
