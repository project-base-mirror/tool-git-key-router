namespace GitKeyRouter.Core.Models;

public sealed class SshManagedBlock
{
    public required string HostAlias { get; init; }

    public required string RawText { get; init; }

    public int StartIndex { get; init; }

    public int Length { get; init; }
}
