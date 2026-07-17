# Architecture

GitKeyRouter separates reusable domain services from process, filesystem, Git, and OpenSSH adapters. The WinForms and CLI entry points share the same service graph.

The application never runs user input through `cmd.exe` or PowerShell. External tools are invoked by executable path with `ProcessStartInfo.ArgumentList`.
