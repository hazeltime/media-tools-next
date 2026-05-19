# Media Tools Next

Consolidated .NET 10 media integrity scanner and sorter.

## What It Does

- Scans images, video, audio, and documents.
- Validates files with lightweight header checks plus external tools where available.
- Defaults to dry-run mode.
- Can copy sorted results into `valid`, `corrupt`, `unknown`, and `error` folders.
- Can mirror sorted output to a backup target.
- Stores scan sessions and results in SQLite.
- Provides a Windows .NET MAUI Blazor desktop app and cross-platform CLI entrypoint.

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

CI runs the same checks on `windows-latest` for pushes and pull requests to `main`.

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
dotnet run --project src\MediaToolsNext.Cli -- <source> <target> --health
```

Without `--live`, no files are copied.
If `--concurrency` is omitted, the CLI uses the hardware tuner recommendation. Explicit values are clamped to the 1-32 range.
If `--probe-seconds` is omitted, the CLI uses the hardware tuner recommendation.
If `--profile` is omitted, the CLI uses `deep-images`, which performs the in-depth image check. The external tool timeout defaults to 15 seconds unless overridden.
Use `--flat`, `--move`, and `--group-category` to change write layout and operation in live modes.
Use `--health` to check source, target, database folder, and external tools without scanning.

## Desktop

The supported desktop build is Windows (`net10.0-windows10.0.19041.0`). The MAUI project still contains the template mobile and MacCatalyst target entries, but release publishing currently covers Windows desktop and CLI outputs only.

The desktop app starts at `MainPage` and walks through the scan workflow in the app itself. By default it stores the SQLite database under `%LocalAppData%\media-tools-next\media-tools-next.db`.

## Publish

Create the Windows desktop payload and repository-root launcher:

```powershell
.\publish-root-exe.ps1
```

Create an Ubuntu/Linux CLI build:

```powershell
.\publish-root-exe.ps1 -Target LinuxCli -Configuration Release
```

Create both outputs:

```powershell
.\publish-root-exe.ps1 -Target All -Configuration Release
```

Linux output is written to `publish-linux-cli\<runtime>`. The default runtime is `linux-x64`; pass `-LinuxRuntime linux-arm64` for ARM64 Linux. The Linux target publishes the CLI only because the MAUI desktop app has no Linux desktop target.

## Development Checkpoints

Prefer small checkpoints that can be verified independently:

```powershell
git status --short --branch
dotnet test tests\MediaToolsNext.Tests\MediaToolsNext.Tests.csproj --no-restore
dotnet test tests\MediaToolsNext.Desktop.Tests\MediaToolsNext.Desktop.Tests.csproj --no-restore
dotnet build src\MediaToolsNext.Desktop\MediaToolsNext.Desktop.csproj -f net10.0-windows10.0.19041.0 --no-restore
git diff --check
git commit -m "<focused change>"
git push origin main
```

Run narrower tests first when a change is local to one component, then run the broader checks before pushing.
