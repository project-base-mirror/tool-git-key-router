# Architecture

## Layers

### GitKeyRouter.Core

Contains models, validation, pure SSH managed-block editing, URL rewrite comparison, backup contracts, diagnostics, and application services. It has no WinForms dependency and no direct `Process.Start` calls.

### GitKeyRouter.Infrastructure

Contains physical filesystem access, atomic JSON writes, process execution, executable discovery, Git configuration access, safe logging, and backup persistence.

### GitKeyRouter.App

Contains the WinForms shell, pages, dialogs, CLI dispatcher, and the manual service composition root. GUI and CLI use the same `ApplicationServices` instance graph.

## Process safety

All Git/OpenSSH invocations use:

```csharp
var startInfo = new ProcessStartInfo
{
    FileName = executablePath,
    UseShellExecute = false,
    RedirectStandardOutput = true,
    RedirectStandardError = true
};

startInfo.ArgumentList.Add("config");
startInfo.ArgumentList.Add("--global");
```

User-controlled Owner, HostAlias and paths are never interpolated into a shell command.

## SSH Config editing

Automated edits are based on exact marker pairs:

```text
# BEGIN GitKeyRouter managed block: <alias>
...
# END GitKeyRouter managed block: <alias>
```

The service locates the exact block range and replaces or removes only that range. A duplicate marker pair is treated as an error rather than guessed.

## Git URL rewrite reconciliation

Expected rules are generated from enabled `OwnerRoute` records and their identities. Current rules are read with `git config --global --get-regexp` and classified as correct, missing, duplicate, conflict or extra.

Removal uses `--fixed-value --unset-all` to avoid regular-expression interpretation of URLs.

## Persistence

`config.json` uses `System.Text.Json`. Saving writes a UTF-8 temporary file, flushes it, and atomically moves it over the target. A malformed existing file is never automatically replaced.

## Restore semantics

Application config and SSH Config are restored independently. Git URL rewrite restoration removes current exact URL rewrite pairs and re-adds the snapshot pairs through `git config`; it never copies a complete `.gitconfig` file.
