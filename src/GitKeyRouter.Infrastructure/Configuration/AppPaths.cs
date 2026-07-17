using GitKeyRouter.Core.Abstractions;

namespace GitKeyRouter.Infrastructure.Configuration;

public sealed class AppPaths : IAppPaths
{
    public AppPaths()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        UserProfileDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        AppDataDirectory = Path.Combine(appData, "GitKeyRouter");
        ConfigPath = Path.Combine(AppDataDirectory, "config.json");
        BackupRootDirectory = Path.Combine(AppDataDirectory, "backups");
        SshDirectory = Path.Combine(UserProfileDirectory, ".ssh");
        SshConfigPath = Path.Combine(SshDirectory, "config");
        LegacySshConfigBackupPath = Path.Combine(SshDirectory, "config.gitkeyrouter.bak");
    }

    public string AppDataDirectory { get; }

    public string ConfigPath { get; }

    public string BackupRootDirectory { get; }

    public string UserProfileDirectory { get; }

    public string SshDirectory { get; }

    public string SshConfigPath { get; }

    public string LegacySshConfigBackupPath { get; }
}
