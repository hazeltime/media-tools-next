# Backlog

Verified against `main` after `f16aa1c`.

## High Priority

1. Harden destructive move semantics in `FileActionService`.
   - Current move flow copies to target and then deletes the source.
   - If delete fails with a retryable `IOException`, `ScannerPipeline.ApplyWithRetryAsync` can retry the whole action and create another suffixed target copy.
   - Definition of done: copy/delete phases do not duplicate target files on delete failure; result preserves target path and delete failure detail; tests cover locked-source/delete-failure behavior.

2. Route Results-page manual copy/move through shared backend file-action logic.
   - `Results.razor` still uses synchronous `File.Copy` / `File.Move`, local `GetSafePath`, and UI-specific path building.
   - It does not get the same `FileActionService` path validation, `CreateNew` collision handling, buffer sizing, retry behavior, or error result shape as scan actions.
   - Definition of done: manual selected-row actions use shared async service/helper, report copied/moved/skipped/failed counts, and tests cover duplicate flat names, traversal, missing sources, and move failure.

3. Make Tools install/upgrade cancellable from the UI.
   - `ToolInstallService` now has timeout-aware process execution and tests, but `Tools.razor` still passes `CancellationToken.None` and exposes no cancel action.
   - Definition of done: Tools page owns a `CancellationTokenSource`, exposes Cancel while busy, surfaces timeout/cancel states clearly, and writes full command output to a log file when output is long.

## Medium Priority

4. Extract Results filtering/sorting/paging/selection helpers.
   - `Results.razor` owns filtering, sorting, detail grouping, paging, selection, preview navigation, exports, and manual file actions.
   - Definition of done: pure helper/view-model covers status tab, search, detail group, sort modes, paging, selection, and preview navigation; tests live in `MediaToolsNext.Desktop.Tests`.

5. Normalize configuration ownership and defaults.
   - `ScanOptions.ExternalToolTimeoutSeconds` defaults to `20`, while CLI and Desktop pass `15`.
   - `ScanOptions.DatabasePath` is stored in options, but actual persistence is owned by injected `SqliteScanStore`.
   - Definition of done: timeout defaults are aligned and tested; DB path ownership is either removed from `ScanOptions` or made authoritative through a store factory/pipeline construction path.

6. Expand persistence and history tests.
   - Existing batch-save coverage covers mixed valid/error/unknown summary, but not repeated batches, timestamp ordering, corrupt status, or session limit ordering.
   - Definition of done: tests cover `BatchSaveResultsAsync` repeated calls/order guarantees, all summary statuses, multiple sessions, descending session order, and `ListSessionsAsync(take)` cap.

7. Improve Desktop packaging metadata and platform surface.
   - Desktop project still uses `ApplicationTitle=MediaToolsNext.Desktop` and `ApplicationId=com.companyname.mediatoolsnext.desktop`.
   - It also declares Android/iOS/MacCatalyst target frameworks even though repo docs and publish script focus on Windows desktop plus Linux CLI.
   - Definition of done: product decision documented; app title/id updated; target frameworks match supported platforms or docs explicitly describe mobile targets as future/experimental.

8. Add CLI health/preflight mode.
   - No `--health` command exists.
   - Definition of done: CLI can check source/target existence, target writeability, database path, external tool availability, and profile/options summary without scanning.

## Lower Priority

9. Add slow/performance test harness.
   - Useful for validating `HardwareTuner`, `ScannerPipeline` backpressure, SQLite batching, and concurrency choices.
   - Definition of done: `[Trait("Category", "Slow")]` or standalone harness creates synthetic trees and reports files/s and MB/s for standard scenarios.

10. Make image preview size checks use current file metadata.
    - `Results.razor` gates preview by `row.Candidate.SizeBytes`, which can be stale, then calls `File.ReadAllBytesAsync`.
    - Definition of done: preview checks `FileInfo.Length` immediately before reading, handles file changes gracefully, and has tests for stale/missing/oversized image rows if extracted to a helper.

## Rejected Or Stale AI Findings

- Latest commit claims around `1d025d5` are stale; current checked baseline was `f16aa1c`.
- Hardware tuning being "just logged" is stale: CLI now uses recommended concurrency/probe seconds and `CopyBufferBytes`.
- `ScanWorkflowState` source-link test architecture is stale: it was moved into Core and the desktop tests reference Core.
- Results empty `colspan` and copy-visible filter mismatch are stale: `colspan="7"` and `_visibleRows` copy behavior are already present.
- Tool installer "no timeout / stdout before stderr" is stale: process execution now has a timeout and concurrent stdout/stderr reads. The remaining gap is UI cancellation/logging.
- ProcessRunner raw interpolated string concern remains a false positive: `$$"""` with `{{childPidPath}}` is valid C# raw interpolated string syntax.
