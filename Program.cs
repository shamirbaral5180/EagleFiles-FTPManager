using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using FTP_WebApp.Data;
using FTP_WebApp.Models;
using FTP_WebApp.Services;

var builder = WebApplication.CreateBuilder(args);

var configuredConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                                 ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
var storageOptions = builder.Configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions();
var storagePaths = new StoragePathProvider(builder.Environment, Options.Create(storageOptions),
    NullLogger<StoragePathProvider>.Instance);

builder.Services.AddSingleton<IStoragePathProvider>(storagePaths);
builder.Logging.AddProvider(new FileLoggerProvider(storagePaths.LogPath, "eaglefiles"));

var connectionStringBuilder = new SqliteConnectionStringBuilder(configuredConnectionString);
if (!Path.IsPathRooted(connectionStringBuilder.DataSource))
{
    connectionStringBuilder.DataSource = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath,
        connectionStringBuilder.DataSource));
}
Directory.CreateDirectory(Path.GetDirectoryName(connectionStringBuilder.DataSource)!);

builder.Services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(storagePaths.DataProtectionKeyPath))
                .SetApplicationName("EagleFiles");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionStringBuilder.ConnectionString));

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDatabaseDeveloperPageExceptionFilter();
}

builder.Services.AddDefaultIdentity<IdentityUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequiredLength = 8;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddControllersWithViews();
builder.Services.Configure<MediaOptions>(builder.Configuration.GetSection("Media"));
builder.Services.AddScoped<IConnectionProfileService, ConnectionProfileService>();
builder.Services.AddScoped<IRemoteFileService, RemoteFileService>();
builder.Services.AddSingleton<ThumbnailGenerationCoordinator>();
builder.Services.AddScoped<IThumbnailService, ThumbnailService>();

var app = builder.Build();

var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("EagleFiles.Startup");
startupLogger.LogInformation("EagleFiles starting in the {Environment} environment. Data path: {DataPath}.", app.Environment.EnvironmentName,
    storagePaths.RootPath);

try
{
    await using var scope = app.Services.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await context.Database.MigrateAsync();
    startupLogger.LogInformation("The database is up to date.");
}
catch (Exception exception)
{
    startupLogger.LogCritical(exception,
        "The database could not be prepared. Check that the application pool identity can modify {DataPath}.", storagePaths.RootPath);
    throw;
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

app.Run();
