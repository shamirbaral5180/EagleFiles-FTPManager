using System.ComponentModel.DataAnnotations;

namespace FTP_WebApp.Models;

public class ConnectionProfileInput
{
    public int? Id { get; set; }

    [Required, StringLength(80)]
    [Display(Name = "Connection name")]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(255)]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 21;

    [Required, StringLength(150)]
    public string Username { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Connection security")]
    public FtpSecurityMode SecurityMode { get; set; } = FtpSecurityMode.ExplicitTls;

    [Required, StringLength(500)]
    [Display(Name = "Starting folder")]
    public string RootPath { get; set; } = "/";
}

public class ConnectionsIndexViewModel
{
    public IReadOnlyList<FtpConnectionProfile> Connections { get; init; } = [];
}
