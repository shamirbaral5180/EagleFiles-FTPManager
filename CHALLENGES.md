# EagleFiles Challenges and Solutions

This document records the main implementation and deployment problems resolved while building EagleFiles.

## Application and user interface

- **Responsive file management:** Built desktop and mobile layouts with sidebar navigation, grid and list views, search, sorting, grouping, and configurable thumbnail sizes.
- **Folder links opened the media viewer:** Separated folder navigation from media-preview interactions so folders reliably navigate to their contents.
- **Lost browsing position:** Preserved the folder scroll position and returned users to the last-viewed file after closing media previews.
- **Limited media navigation:** Added fullscreen viewing, previous/next navigation, keyboard controls, and high-resolution zoom.
- **Blurry enlarged images:** Kept small cached thumbnails for the grid while serving original browser-supported images when opened.
- **Unsupported image formats:** Added full-size, 100%-quality JPEG conversion for formats browsers cannot display directly.
- **Slow thumbnail generation:** Added persistent thumbnail caching, file-version cache keys, and bounded concurrent generation.
- **Video thumbnail support:** Added optional FFmpeg integration while retaining safe file-type placeholders when conversion is unavailable.

## Authentication and protected data

- **Per-user FTP profiles:** Added ASP.NET Core Identity ownership so users only access their own saved connections.
- **Stored FTP passwords:** Protected connection passwords with ASP.NET Core Data Protection instead of saving plaintext credentials.
- **Passwords failed after deployment changes:** Persisted Data Protection keys under `App_Data/DataProtectionKeys` so encrypted passwords remain readable after restarts and publishing.

## IIS deployment and storage

- **Published application startup failures:** Added explicit database migration during startup and actionable startup diagnostics.
- **Incorrect SQLite location:** Centralized storage path resolution so IIS consistently uses `App_Data/eaglefiles.db` without duplicating path segments.
- **Publishing could overwrite server data:** Configured the IIS publish profile to exclude `App_Data` and retain existing server files.
- **Lost thumbnails after publishing:** Moved the thumbnail cache under persistent application storage and resolved it through the shared storage-path provider.
- **Unclear folder-permission failures:** Added startup validation that reports when the IIS application-pool identity cannot write to application storage.
- **Unsafe production exception details:** Added a production-safe error page while retaining complete diagnostics in server-side log files.
- **Missing production diagnostics:** Added daily logs under `App_Data/logs` for startup, database, request, and FTP failures.
- **IIS upload-size mismatch:** Matched the IIS request limit to the application's 512 MB upload limit.

## FTP connectivity

- **TLS handshake failed against a plain FTP server:** Added explicit `PlainFtp`, `ExplicitTls`, and `ImplicitTls` modes and confirmed that the private Microsoft FTP server uses `PlainFtp` on port 21.
- **Certificate errors were difficult to distinguish from protocol errors:** Improved the connection error message while retaining certificate validation for FTPS.
- **Port 21 worked but folders did not load:** Added stage-specific logging to distinguish control connection, authentication, and passive directory-listing failures.
- **Passive firewall rules existed but listings still stalled:** Discovered that IIS FTP had an empty `<firewallSupport />` configuration, so it was not allocating ports from the opened range.
- **Passive data connection failure:** Configured IIS FTP to use ports `50000-50100`, matched the Windows Firewall rule, restarted the FTP service, and verified that EagleFiles could list folders from the separate IIS server.

## Development and build reliability

- **Debug builds failed with `MSB3021`/`MSB3027`:** Identified running `FTP_WebApp.exe` processes and Visual Studio debug sessions that locked the build output; stopping the verified project process allowed rebuilding.
- **Production and local behavior differed:** Added environment, content-root, storage-path, FTP security-mode, and connection-stage logging to make deployment differences visible.

## Result

EagleFiles now runs locally and under IIS, retains its database, keys, logs, and thumbnail cache across deployments, and connects from the IIS web server to the separate plain FTP server using a verified passive data-channel configuration.
