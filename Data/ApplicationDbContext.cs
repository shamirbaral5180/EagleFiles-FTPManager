using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using FTP_WebApp.Models;

namespace FTP_WebApp.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
    public DbSet<FtpConnectionProfile> FtpConnectionProfiles => Set<FtpConnectionProfile>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<FtpConnectionProfile>(entity =>
        {
            entity.HasIndex(profile => new { profile.OwnerId, profile.Name }).IsUnique();
            entity.Property(profile => profile.SecurityMode).HasConversion<string>().HasMaxLength(30);
            entity.HasOne<Microsoft.AspNetCore.Identity.IdentityUser>()
                  .WithMany()
                  .HasForeignKey(profile => profile.OwnerId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
