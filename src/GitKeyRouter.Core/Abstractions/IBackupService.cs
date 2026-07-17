using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Abstractions;

public interface IBackupService
{
    Task<BackupManifest> CreateSnapshotAsync(string reason, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackupManifest>> ListAsync(CancellationToken cancellationToken = default);

    Task<BackupSnapshot> ReadAsync(string backupDirectory, CancellationToken cancellationToken = default);

    Task<OperationResult> RestoreAppConfigAsync(string backupDirectory, CancellationToken cancellationToken = default);

    Task<OperationResult> RestoreSshConfigAsync(string backupDirectory, CancellationToken cancellationToken = default);

    Task<OperationResult> RestoreGitRewritesAsync(string backupDirectory, CancellationToken cancellationToken = default);
}
