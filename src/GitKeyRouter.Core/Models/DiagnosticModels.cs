namespace GitKeyRouter.Core.Models;

public enum DiagnosticSeverity
{
    Normal,
    Warning,
    Error
}

public sealed class DiagnosticItem
{
    public required string Code { get; init; }

    public required string Category { get; init; }

    public required string Title { get; init; }

    public required string Message { get; init; }

    public required DiagnosticSeverity Severity { get; init; }

    public string? SuggestedAction { get; init; }
}

public sealed class DiagnosticReport
{
    public DateTimeOffset GeneratedAt { get; init; }

    public List<DiagnosticItem> Items { get; } = [];

    public int NormalCount => Items.Count(item => item.Severity == DiagnosticSeverity.Normal);

    public int WarningCount => Items.Count(item => item.Severity == DiagnosticSeverity.Warning);

    public int ErrorCount => Items.Count(item => item.Severity == DiagnosticSeverity.Error);
}
