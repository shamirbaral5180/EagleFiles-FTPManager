using System.ComponentModel.DataAnnotations;

namespace FTP_WebApp.Models;

public class FtpConnectionProfile
{
    public int Id { get; set; }

    [MaxLength(450)]
    public required string OwnerId { get; set; }

    [MaxLength(80)]
    public required string Name { get; set; }

    [MaxLength(255)]
    public required string Host { get; set; }

    public int Port { get; set; } = 21;

    [MaxLength(150)]
    public required string Username { get; set; }

    public required string ProtectedPassword { get; set; }

    public FtpSecurityMode SecurityMode { get; set; } = FtpSecurityMode.ExplicitTls;

    [MaxLength(500)]
    public string RootPath { get; set; } = "/";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public enum FtpSecurityMode
{
    PlainFtp,
    ExplicitTls,
    ImplicitTls
}
