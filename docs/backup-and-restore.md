# Backup and restore

Every application, SSH Config, or Git URL rewrite mutation requests a snapshot first.

A snapshot records whether the original application and SSH files existed. Restoring a snapshot where a file did not exist removes the current file after creating a new safety snapshot.

Git rewrite capture can fail when Git is unavailable. The manifest records the error and the UI prevents restoring an unreliable Git snapshot.

The legacy convenience backup `%USERPROFILE%\.ssh\config.gitkeyrouter.bak` is also refreshed immediately before an SSH Config write, while timestamped backups remain the authoritative recovery history.
