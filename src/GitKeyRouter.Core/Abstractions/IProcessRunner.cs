using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Abstractions;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default);
}
