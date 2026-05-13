## Quota-Efficient Codex Workflow

Use this repo guidance to reduce token, API, and quota usage while preserving quality.

### Default Strategy

- Inspect only the narrow files, diffs, tests, and logs needed for the current task.
- Prefer `rg`, `rg --files`, `git status --short`, `git diff --name-only`, and `git diff --stat` before reading full files.
- Reuse context already gathered in the session. Do not reopen files unless they may have changed or a specific line is needed.
- Make the smallest correct change that fits existing patterns.
- Run targeted tests during worker tasks and one final integration verification centrally.

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

- Workers should run only narrow checks for their owned area.
- The orchestrator should run final verification once:
  - `dotnet test tests\MediaToolsNext.Tests\MediaToolsNext.Tests.csproj --no-restore`
  - `dotnet build src\MediaToolsNext.Cli\MediaToolsNext.Cli.csproj --no-restore`
  - `dotnet build src\MediaToolsNext.Infrastructure\MediaToolsNext.Infrastructure.csproj --no-restore`
  - `dotnet build src\MediaToolsNext.Desktop\MediaToolsNext.Desktop.csproj -f net10.0-windows10.0.19041.0 --no-restore`

### Scope Control

- Do not perform speculative rewrites, broad style churn, dependency additions, or unrelated cleanup.
- Preserve existing behavior unless a test-backed or directly verified bug fix requires changing it.
- Keep final reports concise: changed files, verified commands, remaining blockers, and whether commits/pushes were performed.
