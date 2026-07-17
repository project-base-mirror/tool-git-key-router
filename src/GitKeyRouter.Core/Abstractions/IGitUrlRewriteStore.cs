using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Abstractions;

public interface IGitUrlRewriteStore
{
    Task<IReadOnlyList<GitUrlRewriteRule>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<OperationResult<IReadOnlyList<string>>> GetGlobalConfigOriginsAsync(CancellationToken cancellationToken = default);

    Task<ProcessResult> AddAsync(GitUrlRewriteRule rule, CancellationToken cancellationToken = default);

    Task<ProcessResult> RemoveAllAsync(GitUrlRewriteRule rule, CancellationToken cancellationToken = default);

    Task<ProcessResult> TestRemoteAsync(string originalUrl, CancellationToken cancellationToken = default);

    string? GitExecutablePath { get; }
}
