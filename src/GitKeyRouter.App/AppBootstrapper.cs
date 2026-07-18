using GitKeyRouter.Core.Abstractions;
using GitKeyRouter.Core.Services;
using GitKeyRouter.Infrastructure.Backup;
using GitKeyRouter.Infrastructure.Configuration;
using GitKeyRouter.Infrastructure.FileSystem;
using GitKeyRouter.Infrastructure.Git;
using GitKeyRouter.Infrastructure.Logging;
using GitKeyRouter.Infrastructure.ProcessExecution;

namespace GitKeyRouter.App;

public static class AppBootstrapper
{
    public static ApplicationServices CreateServices()
    {
        IAppPaths paths = new AppPaths();
        IFileSystem fileSystem = new PhysicalFileSystem();
        IClock clock = new SystemClock();
        IProcessRunner processRunner = new ProcessRunner();
        IToolchainService toolchainService = new ToolchainService(processRunner);
        var gitStore = new GitUrlRewriteStore(processRunner, toolchainService);
        IBackupService backupService = new BackupService(paths, fileSystem, gitStore, clock);
        IAppConfigStore configStore = new JsonAppConfigStore(paths, fileSystem);
        ISafeLogger logger = new SafeFileLogger(paths);

        var identityService = new IdentityService(configStore, backupService, clock);
        var ownerRouteService = new OwnerRouteService(configStore, backupService);
        var sshConfigService = new SshConfigService(fileSystem, paths, backupService);
        var gitUrlRewriteService = new GitUrlRewriteService(configStore, gitStore, backupService);
        var sshKeyService = new SshKeyService(fileSystem, processRunner, toolchainService, clock);
        var sshKeyRenameService = new SshKeyRenameService(
            fileSystem,
            configStore,
            backupService,
            sshConfigService,
            sshKeyService,
            clock);
        var diagnosticService = new DiagnosticService(
            configStore,
            paths,
            fileSystem,
            toolchainService,
            sshConfigService,
            gitUrlRewriteService,
            clock);

        return new ApplicationServices
        {
            Paths = paths,
            FileSystem = fileSystem,
            ConfigStore = configStore,
            ToolchainService = toolchainService,
            BackupService = backupService,
            IdentityService = identityService,
            OwnerRouteService = ownerRouteService,
            SshKeyService = sshKeyService,
            SshKeyRenameService = sshKeyRenameService,
            SshConfigService = sshConfigService,
            GitUrlRewriteService = gitUrlRewriteService,
            DiagnosticService = diagnosticService,
            Logger = logger
        };
    }
}
