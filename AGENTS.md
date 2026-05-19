## Quota-Efficient Codex Workflow

Use this repo guidance to reduce token, API, and quota usage while preserving quality.

### Repository Shape

- Solution: `MediaToolsNext.slnx`.
- Projects:
  - `src\MediaToolsNext.Core\MediaToolsNext.Core.csproj` for shared domain models, scan options, validation contracts, and pure helpers.
  - `src\MediaToolsNext.Infrastructure\MediaToolsNext.Infrastructure.csproj` for file discovery/actions, validators, SQLite storage, external tool probing, and pipeline orchestration.
  - `src\MediaToolsNext.Cli\MediaToolsNext.Cli.csproj` for command-line entrypoint behavior.
  - `src\MediaToolsNext.Desktop\MediaToolsNext.Desktop.csproj` for .NET MAUI Blazor UI.
  - `tests\MediaToolsNext.Tests\MediaToolsNext.Tests.csproj` for xUnit coverage.
- External tools are optional runtime dependencies: `ffmpeg`, `ffprobe`, ImageMagick `magick`, and `qpdf`.

### Default Strategy

- Inspect only the narrow files, diffs, tests, and logs needed for the current task.
- Prefer `git status --short --branch`, `git diff --name-only`, `git diff --stat`, `rg`, and `rg --files` before reading full files.
- Start with project boundaries: Core changes usually need Core tests; Infrastructure changes usually need the matching test class; Desktop changes usually need the exact `.razor`, `.xaml`, or code-behind file only.
- Reuse context already gathered in the session. Do not reopen files unless they may have changed or a specific line is needed.
- Make the smallest correct change that fits existing patterns.
- Run targeted tests during worker tasks and one final integration verification centrally.

### Token and API Budget

- Use local files, `rg`, and targeted builds before web search, GitHub connectors, MCP servers, or package lookups.
- Do not browse current docs unless the task depends on version-sensitive external behavior or the user asks for current documentation.
- Do not invoke image, browser, network, or GitHub tools for normal code edits in this repo.
- Keep prompts to subagents compact when delegation is explicitly requested: repo path, owned files, exact failure or hypothesis, allowed edits, and one verification command.
- Avoid re-summarizing the repository. Refer back to this file and only inspect changed or task-relevant files.

### MCP, Skills, Plugins, And Extensions

- MCP/connectors: default to none for local code tasks. Use GitHub MCP only for explicit PR/issue/repository operations. Use web/MCP documentation only for version-sensitive APIs or external service behavior.
- Skills: use a skill only when it directly matches the request. For this repo, most tasks should not need image generation, plugin creation, skill creation, or skill installation.
- Plugins/extensions: do not add dependencies, editor plugins, MCP servers, or repo tooling unless they remove a repeated local bottleneck and are scoped to this repo.
- Local editor/LSP tuning lives in ignored `.vscode\settings.json` and `.vscode\extensions.json`; do not assume those files are committed.

### LSP And Editor Efficiency

- Prefer C# Dev Kit or OmniSharp/Roslyn for C# language service. Avoid running multiple C# language servers on the same workspace.
- Exclude generated and heavy folders from search/watch: `bin`, `obj`, `.vs`, `TestResults`, `publish-root`, and SQLite/log artifacts.
- Keep analyzers enabled for open files and build/test commands; do not run full-solution analysis unless the change touches shared contracts or project configuration.
- For MAUI Desktop work on Windows, target `net10.0-windows10.0.19041.0` unless the task explicitly concerns Android, iOS, or MacCatalyst.

### Branch and Workspace Consolidation

- Start with cheap state reads:
  - `git status --short --branch`
  - `git branch --all --verbose --no-abbrev`
  - `git worktree list --porcelain`
  - `git stash list --date=local`
  - `git diff --name-status <base>...<branch>`
  - `git diff --stat <base>...<branch>`
- Do not reset, checkout, delete, or overwrite dirty work without explicit approval.
- When consolidating branches, compare branch-specific files and integrate only confirmed non-regressing improvements.
- Prefer file-scoped manual integration over broad merges when the current worktree already contains newer fixes.

### Subagent Delegation

- Use cheap worker waves of 4-6 agents when delegation is requested.
- Do not fork full history when overriding model/reasoning; provide compact prompts with:
  - repo path
  - owned files
  - exact task or bug hypothesis
  - verification command
  - no commit/push instruction
- Give each worker one file or one narrow module to avoid conflicts.
- Use "verify first; edit only if confirmed" prompts to avoid false positives.
- Close finished agents before launching the next wave.
- Delegate exact compile/test failures as tiny follow-up tasks with error lines and file ownership.

### Verification

- Prefer the smallest useful command first:
  - Core-only changes: `dotnet test tests\MediaToolsNext.Tests\MediaToolsNext.Tests.csproj --no-restore --filter <RelevantTestClassOrName>`
  - Infrastructure-only changes: `dotnet test tests\MediaToolsNext.Tests\MediaToolsNext.Tests.csproj --no-restore --filter <RelevantTestClassOrName>`
  - CLI changes: `dotnet build src\MediaToolsNext.Cli\MediaToolsNext.Cli.csproj --no-restore`
  - Desktop UI changes: `dotnet build src\MediaToolsNext.Desktop\MediaToolsNext.Desktop.csproj -f net10.0-windows10.0.19041.0 --no-restore`
- Workers should run only narrow checks for their owned area.
- The orchestrator should run broader final verification once when the change spans multiple projects or shared contracts:
  - `dotnet test tests\MediaToolsNext.Tests\MediaToolsNext.Tests.csproj --no-restore`
  - `dotnet build src\MediaToolsNext.Cli\MediaToolsNext.Cli.csproj --no-restore`
  - `dotnet build src\MediaToolsNext.Infrastructure\MediaToolsNext.Infrastructure.csproj --no-restore`
  - `dotnet build src\MediaToolsNext.Desktop\MediaToolsNext.Desktop.csproj -f net10.0-windows10.0.19041.0 --no-restore`

### Scope Control

- Do not perform speculative rewrites, broad style churn, dependency additions, or unrelated cleanup.
- Preserve existing behavior unless a test-backed or directly verified bug fix requires changing it.
- Keep final reports concise: changed files, verified commands, remaining blockers, and whether commits/pushes were performed.
