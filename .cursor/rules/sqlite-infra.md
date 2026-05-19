# SQLite & Infrastructure Rules
GlobPattern: **/Sqlite*.cs | **/*.db

You are an agentic developer working on SQLite storage or infrastructure logic. Follow these dynamic rules:

## 1. Database Thread-Safety & SQLite Performance
- SQLite is a single-writer database. Protect all write operations using an appropriate locking mechanism (e.g., `SemaphoreSlim _writeLock`) to avoid "database is locked" errors during parallel operations.
- Utilize batch writes (`BatchSaveResultsAsync`) using active transactions (`SqliteTransaction`) to speed up database writes and maintain integrity.
- Always dispose connections and commands appropriately (`await using` pattern).

## 2. Infrastructure Testing
- When modifying database or disk discovery operations, always run the dedicated integration tests under `tests\MediaToolsNext.Tests`.
- Run: `scripts\verify-fast.ps1 -Area infra -Filter <TestClassName>` to verify the database behavior.
- Ensure temporary database files are properly created in temporary test directories (using `TestTempDirectory.Create()`) and cleaned up after execution.
