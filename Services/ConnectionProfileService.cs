using FTP_WebApp.Data;
using FTP_WebApp.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace FTP_WebApp.Services;

public class ConnectionProfileService : IConnectionProfileService
{
    private readonly ApplicationDbContext _context;
    private readonly IDataProtector _protector;

    public ConnectionProfileService(ApplicationDbContext context, IDataProtectionProvider dataProtectionProvider)
    {
        _context = context;
        _protector = dataProtectionProvider.CreateProtector("FTP_WebApp.ConnectionPasswords.v1");
    }

    public async Task<IReadOnlyList<FtpConnectionProfile>> GetAllAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        return await _context.FtpConnectionProfiles
                             .Where(profile => profile.OwnerId == ownerId)
                             .OrderBy(profile => profile.Name)
                             .AsNoTracking()
                             .ToListAsync(cancellationToken);
    }

    public async Task<FtpConnectionProfile> GetAsync(int id, string ownerId, CancellationToken cancellationToken = default)
    {
        return await _context.FtpConnectionProfiles
                             .Where(profile => profile.Id == id && profile.OwnerId == ownerId)
                             .FirstOrDefaultAsync(cancellationToken)
               ?? throw new KeyNotFoundException("The connection was not found.");
    }

    public async Task<int> SaveAsync(ConnectionProfileInput input, string ownerId, CancellationToken cancellationToken = default)
    {
        var name = input.Name.Trim();
        var host = input.Host.Trim();
        var username = input.Username.Trim();
        var rootPath = NormalizeRoot(input.RootPath);

        var duplicateExists = await _context.FtpConnectionProfiles
                                            .AnyAsync(profile => profile.OwnerId == ownerId && profile.Name == name && profile.Id != input.Id,
                                                cancellationToken);
        if (duplicateExists)
        {
            throw new InvalidOperationException("You already have a connection with this name.");
        }

        FtpConnectionProfile profile;
        if (input.Id.HasValue)
        {
            profile = await GetAsync(input.Id.Value, ownerId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(input.Password))
            {
                profile.ProtectedPassword = _protector.Protect(input.Password);
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(input.Password))
            {
                throw new InvalidOperationException("A password is required for a new connection.");
            }

            profile = new FtpConnectionProfile
            {
                OwnerId = ownerId,
                Name = name,
                Host = host,
                Username = username,
                ProtectedPassword = _protector.Protect(input.Password),
                CreatedAt = DateTimeOffset.UtcNow
            };
            _context.FtpConnectionProfiles.Add(profile);
        }

        profile.Name = name;
        profile.Host = host;
        profile.Port = input.Port;
        profile.Username = username;
        profile.SecurityMode = input.SecurityMode;
        profile.RootPath = rootPath;
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return profile.Id;
    }

    public async Task DeleteAsync(int id, string ownerId, CancellationToken cancellationToken = default)
    {
        var profile = await GetAsync(id, ownerId, cancellationToken);
        _context.FtpConnectionProfiles.Remove(profile);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public string UnprotectPassword(FtpConnectionProfile profile)
    {
        return _protector.Unprotect(profile.ProtectedPassword);
    }

    private static string NormalizeRoot(string rootPath)
    {
        var normalized = "/" + string.Join('/', rootPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length > 1 ? normalized.TrimEnd('/') : normalized;
    }
}
