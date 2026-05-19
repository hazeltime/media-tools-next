# Backlog

All planned backlog items have been implemented and verified against the current `main` branch.

## Completed

1. **Harden destructive move semantics in `FileActionService`.**
   - `ScannerPipeline.ApplyWithRetryAsync` catches `MoveDeleteFailedException`, returns outcome with preserved target path and delete failure detail (no retry of the whole action).
   - Test: `MovePermanentDeleteFailurePreservesPathsAndErrorDetail` in `ScannerPipelineFlowTests.cs`.

2. **Route Results-page manual copy/move through shared backend file-action logic.**
   - `Results.razor` injects `IFileActionService` and uses it for all manual file operations.
   - `ResultsPresentationModel` handles filtering, sorting, paging, and selection.
   - No direct `File.Copy`/`File.Move` calls remain in the page.

3. **Make Tools install/upgrade cancellable from the UI.**
   - `Tools.razor` owns a `CancellationTokenSource` (`_installCts`), exposes `CancelOperation()`, displays command output logs, and surfaces timeout/cancel states.

4. **Extract Results filtering/sorting/paging/selection helpers.**
   - `ResultsPresentationModel` extracted to `Core/Presentation/` with tests in `MediaToolsNext.Desktop.Tests/ResultsPresentationTests.cs`.
   - `ImagePreviewHelper` extracted to `Core/Presentation/` with MIME type and size-limit tests.

5. **Normalize database path ownership.**
   - `DatabasePath` removed from `ScanOptions` record and `CreateDefault`.
   - DB path flows exclusively through DI wiring (`AddMediaToolsNext`) or `CliScanOptionsBuildResult.DatabasePath`.
   - Commit: `093cd05`.

6. **Expand repeated-batch persistence tests.**
   - `BatchSaveResultsConcurrentAndRepeatedWrites` covers sequential duplicates, parallel `SaveResultAsync`/`BatchSaveResultsAsync` concurrency.
   - `ListResultsPreservesInsertionOrderForEqualTimestamps` covers ordering guarantees.

7. **Decide Desktop target-framework policy.**
   - Desktop project targets only `net10.0-windows10.0.19041.0` (single framework).
   - All Android/iOS/MacCatalyst target entries removed.

8. **Add slow/performance test harness.**
   - `SlowPerformanceHarnessTests.cs` creates synthetic directory trees with real files and runs the full pipeline.
   - Three tests tagged `[Trait("Category", "Slow")]`: small tree (500 files), medium tree (1000 files), concurrency backpressure (1 vs 8 workers).
   - Excluded from default fast test suite via `--filter "Category!=Slow"`.
   - Commit: `b0b7b32`.

9. **Add image preview helper tests.**
   - `ResultsPresentationTests.cs` covers `ImagePreviewHelper.ResolveMimeType` (all known extensions) and `ImagePreviewHelper.CanPreview` (10 MB boundary).

## Rejected Or Stale AI Findings

- Latest commit claims around `1d025d5` and earlier checked baselines are stale; this backlog is verified against the current feature branch implementation.
- Hardware tuning being "just logged" is stale: CLI now uses recommended concurrency/probe seconds and `CopyBufferBytes`.
- CLI health/preflight absence is stale: `--health` now checks source/target existence, target writeability, database folder availability, and external tool availability without scanning.
- External tool timeout mismatch is stale: Core, CLI, and Desktop now use a 15-second default.
- Desktop title/id and Windows branding metadata findings are stale: product-facing metadata now uses Media Tools Next and `com.hazeltime.mediatoolsnext`.
- Preview stale-size guard is stale: image preview checks current `FileInfo.Length` before reading bytes.
- `ScanWorkflowState` source-link test architecture is stale: it was moved into Core and the desktop tests reference Core.
- Results empty `colspan` and copy-visible filter mismatch are stale: `colspan="7"` and `_visibleRows` copy behavior are already present.
- Tool installer "no timeout / stdout before stderr" is stale: process execution now has a timeout and concurrent stdout/stderr reads. The remaining gap is UI cancellation/logging.
- ProcessRunner raw interpolated string concern remains a false positive: `$$"""` with `{{childPidPath}}` is valid C# raw interpolated string syntax.
