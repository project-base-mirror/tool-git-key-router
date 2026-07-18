using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Abstractions;

public interface IRequiredToolInstallerService
{
    Task<RequiredToolInstallPlan> BuildPlanAsync(CancellationToken cancellationToken = default);

    Task<OperationResult<RequiredToolInstallResult>> InstallMissingAsync(
        CancellationToken cancellationToken = default);
}
