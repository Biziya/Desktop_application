# SPP CT007

SPP CT007 (CT007 Measurement Studio) is a Windows desktop app for logging CT007 gamma measurements along a drillhole.
The operator picks a drillhole, right-clicks to capture a few seconds of readings from the local Gamma Guard (CT008)
SQLite database, and each capture becomes one depth interval in the table. Measurements are kept in a local SQLite file,
exported to CSV, and uploaded to a central SQL Server database through a small ASP.NET Core API.

## Folders

| Folder | Contents |
|---|---|
| `Frontend/` | WPF desktop app and its installer script |
| `Backend/` | ASP.NET Core API (drillhole list, measurement upload, uploaded data) |
| `Tests/` | Console test harness for both projects |
| `tools/` | `check-db.ps1`, a read-only inspector for the app's `measurements.db` |
| `Images/` | Screenshots |
| `SHIBA/` | CT008 Monitor source handed over by EIC, kept for reference |

`release/` folders inside `Frontend/` and `Backend/` are build output and are not committed.

## Prerequisites

- Windows 10 version 2004 (build 19041) or later
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- For the installer only: the Velopack CLI, `dotnet tool install -g vpk --version 1.2.161`

## Common commands

Run these from the repository root unless noted.

```powershell
# Build everything
dotnet build CT007.slnx

# Run the desktop app (Debug, reads Frontend\.env.local)
dotnet run --project Frontend

# Run the API (Swagger at http://localhost:5002/swagger)
cd Backend; dotnet run

# Run the tests
dotnet run --project Tests

# Build the installer (output in Frontend\release\installer)
cd Frontend; .\build-installer.ps1

# Publish the API (output in Backend\release)
cd Backend; .\build-backend.ps1
```

If PowerShell blocks the scripts, run them as `powershell -ExecutionPolicy Bypass -File .\build-installer.ps1`.

## More detail

- [Frontend/README.md](Frontend/README.md): pages, configuration, local data, installer
- [Backend/README.md](Backend/README.md): endpoints, connection string, database setup, deployment
- [Tests/README.md](Tests/README.md): running and selecting tests
