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
dotnet build src\MediaToolsNext.Cli\MediaToolsNext.Cli.csproj
dotnet build src\MediaToolsNext.Desktop\MediaToolsNext.Desktop.csproj -f net10.0-windows10.0.19041.0
```

## CLI

```powershell
dotnet run --project src\MediaToolsNext.Cli -- <source> <target>
dotnet run --project src\MediaToolsNext.Cli -- <source> <target> --live
dotnet run --project src\MediaToolsNext.Cli -- <source> <target> --backup <backup> --live
```

Without `--live`, no files are copied.

