# CT008 Monitor: source code handover

Provided by EIC on 23 October 2026 for integration of the monitor's functionality into the client's own application.

| | |
|---|---|
| Application | CT008 Monitor, proof of concept, version 0.1.0 |
| Reads | The CT008 live SQLite database, schema version 3 (see the included integration guide) |
| Platform | Windows, .NET Framework 4.8, WPF |
| Language | C# 7.3 |
| Project format | SDK-style `.csproj`, NuGet PackageReference |
| Third-party code | System.Data.SQLite.Core 1.0.119 (NuGet). System.Data.SQLite and the SQLite engine are released to the public domain by their authors. |

## Contents of this package

```
CT008 Monitor Source\
    README.md                                  this file
    CT008 Database Integration Guide.pdf       the database contract the monitor implements
    CT008Monitor.sln                           Visual Studio solution
    CT008Monitor\
        CT008Monitor.csproj                    project file; the only dependency is declared here
        App.config                             poll interval, stale threshold, default database path
        App.xaml, App.xaml.cs                  WPF application shell
        MainWindow.xaml, MainWindow.xaml.cs    the window: grids, warning banner, Sample button handler
        MonitorViewModel.cs                    polling, cursor, status and staleness, 5-second samples, warning
        DatabaseReader.cs                      read-only access to the database
        Models.cs                              row models for the grids and the sample results
```

## Building

Either open `CT008Monitor.sln` in Visual Studio 2022 and build, or from a command prompt with the .NET SDK (6.0 or later) installed:

```
dotnet build CT008Monitor\CT008Monitor.csproj -c Release
```

The first build restores the NuGet package from nuget.org. Output goes to `CT008Monitor\bin\Release\net48\`. The native SQLite library is copied into `x86\` and `x64\` subfolders of the output; those two folders must ship with the executable.

## What is reusable, and what is not

The code separates cleanly into three layers. The first two have no dependency on WPF and are the parts worth lifting into another application.

**`DatabaseReader.cs`** is a self-contained read-only data access class over System.Data.SQLite. It opens the database without creating it when missing, sets the busy timeout, and provides: the current status rows, the newest reading per detector, and readings after a given id in id order (the cursor read). It depends only on `System.Data.SQLite`.

**`MonitorViewModel.cs`** holds all the behaviour, driven by a `Poll()` method the host calls once per second:

- opening and transparently re-opening the database, and waiting for it if the file does not exist yet;
- the id cursor, seeded at the newest reading on open, with a guard that follows a recreated database;
- detector rows kept up to date in place, with the stale rule (heartbeat older than 5 seconds) and the "logging but no data" rule;
- the response time check (expected 1 second) and the warning text;
- the 5-second sample: `StartAggregation(serial)` discards the first reading after the call and averages the next 5; results carry first and last reading time, coverage in seconds, reading count and completeness;
- the recent-readings list.

It uses `ObservableCollection` and `INotifyPropertyChanged` so a WPF or WinForms host can bind to it directly, but nothing in it requires a UI; a service could drive `Poll()` from a timer and read the collections.

**`MainWindow.xaml` and `MainWindow.xaml.cs`** are the WPF presentation only: three grids, the warning banner, the Browse and Reconnect buttons, and the click handler that calls `StartAggregation`. This is the part the client's application would replace with its own screens.

**`Models.cs`** holds plain row classes (`DetectorRow`, `SampleRow`, `AggregateResult`) and the device type name table.

The `SettingsStore` class at the bottom of `MonitorViewModel.cs` remembers the last database path per Windows user in `%LocalAppData%\CT008Monitor\database-path.txt`. It exists only for the standalone monitor and can be deleted when integrating.

## Behaviour that must be preserved when integrating

These rules come from the database contract in the integration guide and keep the client from interfering with the CT008 instances that write the file:

1. Never write to the database, never change `journal_mode`, never create the file.
2. Set a busy timeout and keep each query a short autocommit statement.
3. Read new data by `id` with `WHERE id > last_seen ORDER BY id`; never by timestamp.
4. Treat a status row whose `updated_ms` is older than about 5 seconds as a dead instance, except rows in the `disconnected` state.
5. For 5-second samples, discard the first reading after the trigger and average the next 5; see section 8 of the guide for why.
6. Warn when a live meter's `response_time` is not 1.

## Configuration (`App.config`)

| Key | Default | Meaning |
|---|---|---|
| `DefaultDatabasePath` | relative path to EIC's development build | Database to open when the user has not chosen one. Set this to the CT008 installation's `Database\ct008-log.sqlite` for a deployment. |
| `PollIntervalMs` | 1000 | How often `Poll()` runs. The CT008 writer commits once a second, so polling faster gains nothing. |
| `StaleAfterMs` | 5000 | Heartbeat age after which a detector is shown as stale. |

## Known limitations of the proof of concept

- Polling and database access run on the UI thread. Each poll is a few indexed reads and takes milliseconds, which is fine for the detector counts involved, but a service-style integration may prefer a background timer.
- Completed 5-second samples are held in memory only (the newest 100) and are not written anywhere.
- The device type table lists all EIC instrument types; the client's deployment uses MicroR meters (`device_type` 14) only.
- Text is English only; numbers and times on screen follow the PC's regional settings.

## Terms

This source is provided by EIC to the client for the purpose stated above. Redistribution terms are as agreed between EIC and the client.
