using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Abstractions;

public interface IToolchainService
{
    Task<ToolchainInfo> InspectAsync(CancellationToken cancellationToken = default);
}
