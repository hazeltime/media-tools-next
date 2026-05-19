# PowerShell Scripting Rules
GlobPattern: **/*.ps1

You are an agentic developer working on PowerShell scripts or terminal automation. Follow these dynamic rules:

## 1. Syntax & Compatibility
- Target Windows PowerShell/PowerShell Core. Do not use Bash or Unix-specific shell commands unless running in a WSL/Linux environment.
- Use explicit error action preferences: `$ErrorActionPreference = "Stop"` at the top of your scripts to ensure errors fail fast.
- Leverage clean param blocks and explicit types.

## 2. Token-Efficient Script Modifications
- Only read the specific lines or functions of the script you need to update.
- Always perform syntax checks and dry runs locally using `run_command` with PowerShell Bypass execution policies if needed.
- Avoid introducing heavy dependencies; keep utility scripts lightweight.
