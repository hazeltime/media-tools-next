# Media Tools Next

Consolidated .NET 10 media integrity scanner and sorter.

## What It Does

- Scans images, video, audio, and documents.
- Validates files with lightweight header checks plus external tools where available.
- Defaults to dry-run mode.
- Can copy sorted results into `valid`, `corrupt`, `unknown`, and `error` folders.
- Can mirror sorted output to a backup target.
- Stores scan sessions and results in SQLite.
- Provides both a .NET MAUI Blazor desktop app and a CLI entrypoint.

## External Tools

Install:

```powershell
choco install ffmpeg imagemagick qpdf -y
dotnet workload install maui
```

Verify:

```powershell
ffmpeg -version
ffprobe -version
qpdf --version
```

ImageMagick is also probed under `Program Files` when `magick` is not on `PATH`.

## Build and Test

```powershell
dotnet test tests\MediaToolsNext.Tests\MediaToolsNext.Tests.csproj
dotnet test tests\MediaToolsNext.Desktop.Tests\MediaToolsNext.Desktop.Tests.csproj
dotnet build src\MediaToolsNext.Cli\MediaToolsNext.Cli.csproj
dotnet build src\MediaToolsNext.Desktop\MediaToolsNext.Desktop.csproj -f net10.0-windows10.0.19041.0
```

Collect coverage:

```powershell
dotnet test tests\MediaToolsNext.Tests\MediaToolsNext.Tests.csproj --collect:"XPlat Code Coverage"
dotnet test tests\MediaToolsNext.Desktop.Tests\MediaToolsNext.Desktop.Tests.csproj --collect:"XPlat Code Coverage"
```

## CLI

```powershell
dotnet run --project src\MediaToolsNext.Cli -- <source> <target>
dotnet run --project src\MediaToolsNext.Cli -- <source> <target> --preview
dotnet run --project src\MediaToolsNext.Cli -- <source> <target> --live
dotnet run --project src\MediaToolsNext.Cli -- <source> <target> --backup <backup> --live
dotnet run --project src\MediaToolsNext.Cli -- <source> <target> --concurrency <1-32>
dotnet run --project src\MediaToolsNext.Cli -- <source> <target> --probe-seconds <10-600>
dotnet run --project src\MediaToolsNext.Cli -- <source> <target> --tool-timeout-seconds <5-600>
dotnet run --project src\MediaToolsNext.Cli -- <source> <target> --export results.csv
```

Without `--live`, no files are copied.
If `--concurrency` is omitted, the CLI uses the hardware tuner recommendation. Explicit values are clamped to the 1-32 range.
If `--probe-seconds` is omitted, the CLI uses the hardware tuner recommendation.
If `--profile` is omitted, the CLI uses `deep-images`, which performs the in-depth image check. The external tool timeout defaults to 15 seconds unless overridden.
Use `--flat`, `--move`, and `--group-category` to change write layout and operation in live modes.

## Desktop

The desktop app starts at `MainPage` and walks through the scan workflow in the app itself. By default it stores the SQLite database under `%LocalAppData%\media-tools-next\media-tools-next.db`.
