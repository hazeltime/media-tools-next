# C# & .NET 10 Coding Rules
GlobPattern: **/*.cs

You are an agentic developer working on C# code. Follow these dynamic rules to maximize efficiency and minimize tokens:

## 1. Environment & LSP
- Always leverage the Roslyn Language Server / C# Dev Kit. Do NOT run multiple language servers.
- Rely on background Roslyn analyzers. Fix syntax and code-style issues locally before asking for guidance.

## 2. Token Efficiency
- **Search First:** Use `grep_search` to find symbols, classes, or methods. 
- **Narrow Reads:** Only read the specific lines you need using `view_file` with rad-intervals. Never read full files unless necessary.
- **Incremental Edits:** Use `replace_file_content` for small single-block edits, or `multi_replace_file_content` for non-contiguous edits. Avoid overwriting entire files.

## 3. Compilation & Verification
- Compile and test locally before proposing changes.
- Use `scripts\verify-fast.ps1 -Area core` or `-Area infra` to test.
- Always use the `--no-restore` flag on `dotnet` commands to prevent NuGet restoration delays and huge terminal outputs.
- Target `net10.0` and focus unit tests using the `--filter` parameter to run only the relevant test class.
