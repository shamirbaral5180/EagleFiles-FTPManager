using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using FTP_WebApp.Data;
using FTP_WebApp.Models;
using FTP_WebApp.Services;

var builder = WebApplication.CreateBuilder(args);

var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
var keyDirectory = Path.Combine(dataDirectory, "DataProtectionKeys");
Directory.CreateDirectory(dataDirectory);
Directory.CreateDirectory(keyDirectory);

var configuredConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                                 ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
var connectionStringBuilder = new SqliteConnectionStringBuilder(configuredConnectionString);
if (!Path.IsPathRooted(connectionStringBuilder.DataSource))
{
    connectionStringBuilder.DataSource = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath,
        connectionStringBuilder.DataSource));
}

builder.Services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
                .SetApplicationName("EagleFiles");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionStringBuilder.ConnectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

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

await using (var scope = app.Services.CreateAsyncScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await context.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
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
