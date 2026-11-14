using FTP_WebApp.Models;

namespace FTP_WebApp.Services;

public interface IConnectionProfileService
{
    Task<IReadOnlyList<FtpConnectionProfile>> GetAllAsync(string ownerId, CancellationToken cancellationToken = default);
    Task<FtpConnectionProfile> GetAsync(int id, string ownerId, CancellationToken cancellationToken = default);
    Task<int> SaveAsync(ConnectionProfileInput input, string ownerId, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, string ownerId, CancellationToken cancellationToken = default);
    string UnprotectPassword(FtpConnectionProfile profile);
}
