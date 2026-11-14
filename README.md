# EagleFiles

EagleFiles is a responsive ASP.NET Core 10 MVC file manager for FTP and FTPS servers. It uses an original eagle-themed interface designed for desktop and mobile screens.

## Current features

- ASP.NET Core Identity accounts
- Per-user FTP/FTPS connection profiles
- Encrypted saved passwords using ASP.NET Core Data Protection
- Root-folder boundary for every connection
- Folder browsing, search, list view, and media-focused grid view
- Small, medium, large, and very large thumbnail sizes saved per device
- Image thumbnails plus in-app image, video, audio, PDF, and text previews
- File-type filters for folders, images, videos, and documents
- Upload (up to 512 MB) and icon-based downloads
- Create folders, rename, move, and delete files or folders
- Responsive desktop sidebar and mobile navigation
- Friendly connection-error state

## Run locally

```powershell
dotnet restore
dotnet ef database update
dotnet run
```

Open the URL printed by `dotnet run`, register an account, and add a connection.

## Publish to IIS

1. Install IIS and the **ASP.NET Core 10 Hosting Bundle** on the server.
2. Publish the project:

   ```powershell
   dotnet publish FTP_WebApp.csproj -c Release -o C:\inetpub\EagleFiles
   ```

3. In IIS Manager, create an application pool named `EagleFiles` with **No Managed Code**.
4. Create a website pointed to `C:\inetpub\EagleFiles` and assign the `EagleFiles` application pool.
5. Create the data directory and grant the application pool read access to the site plus modify access to its private data:

   ```powershell
   New-Item -ItemType Directory -Force C:\inetpub\EagleFiles\App_Data
   icacls C:\inetpub\EagleFiles /grant "IIS AppPool\EagleFiles:(OI)(CI)RX"
   icacls C:\inetpub\EagleFiles\App_Data /grant "IIS AppPool\EagleFiles:(OI)(CI)M"
   ```
6. Add an HTTPS binding with a trusted certificate. Do not expose account or FTP credentials over HTTP.
7. Start the website and register its first user.

The application creates and migrates `App_Data/eaglefiles.db` at startup. Data Protection keys are persisted in `App_Data/DataProtectionKeys`, so saved FTP passwords and login cookies remain readable after an application restart. Back up the complete `App_Data` directory and restrict it to the application-pool identity and administrators.

The included `web.config` enables the same 512 MB upload limit configured by the MVC upload action.

### Video thumbnails on IIS

Image thumbnails are converted to cached JPEG files automatically. Video poster thumbnails require FFmpeg because browsers and IIS cannot decode every remote video format themselves.

1. Install a trusted FFmpeg build on the IIS server.
2. Set the executable path in the production `appsettings.json`:

   ```json
   "Media": {
     "ThumbnailCachePath": "App_Data/ThumbnailCache",
     "FfmpegPath": "C:\\Tools\\ffmpeg\\bin\\ffmpeg.exe",
     "ThumbnailQuality": 78,
     "PreviewQuality": 100,
     "MaxConcurrentGenerators": 2
   }
   ```

3. Grant `IIS AppPool\EagleFiles` Read & Execute permission on the FFmpeg directory.
4. Recycle the EagleFiles application pool.

Generated image and video thumbnails are cached under `App_Data/ThumbnailCache`. A changed file receives a new cache key based on its remote size and modified timestamp. If a format is not supported by ImageMagick or FFmpeg, EagleFiles displays its file-type tile instead of a broken preview.

## Connection security

Use **Explicit TLS** or **Implicit TLS** whenever the server supports it. Plain FTP sends credentials and file data without transport encryption and should only be used on a trusted private network.

The application validates TLS certificates by default. Connection passwords are encrypted at rest, but production deployments must persist and protect the ASP.NET Core Data Protection key ring.

## Next provider options

The current release targets FTP and FTPS. SFTP, SMB, WebDAV, cloud storage, favorites, recents, and background transfer queues can be introduced as separate providers without exposing provider credentials to the browser.
