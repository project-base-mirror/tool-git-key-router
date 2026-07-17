namespace GitKeyRouter.Core.Abstractions;

public interface IAppPaths
{
    string AppDataDirectory { get; }

    string ConfigPath { get; }

    string BackupRootDirectory { get; }

    string UserProfileDirectory { get; }

    string SshDirectory { get; }

    string SshConfigPath { get; }

    string LegacySshConfigBackupPath { get; }
}
