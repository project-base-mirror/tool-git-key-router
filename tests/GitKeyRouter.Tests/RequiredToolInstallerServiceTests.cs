using GitKeyRouter.Core.Abstractions;
using GitKeyRouter.Core.Models;
using GitKeyRouter.Infrastructure.ProcessExecution;
using GitKeyRouter.Tests.TestSupport;

namespace GitKeyRouter.Tests;

public sealed class RequiredToolInstallerServiceTests
{
    [Fact]
    public async Task BuildPlanOffersWingetForMissingGit()
    {
        var toolchain = new SequenceToolchainService(
            Tools(git: false, ssh: true, keygen: true, winget: true));
        var runner = new StubProcessRunner(_ => Success("winget.exe"));
        var service = new RequiredToolInstallerService(toolchain, runner, () => "powershell.exe");

        var plan = await service.BuildPlanAsync();

        var item = Assert.Single(plan.MissingTools);
        Assert.Equal(RequiredToolKind.Git, item.Kind);
        Assert.True(item.CanInstallAutomatically);
        Assert.Contains("Windows Package Manager", item.InstallMethod, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallsGitWithExactTrustedWingetPackageAndRechecksTools()
    {
        var toolchain = new SequenceToolchainService(
            Tools(git: false, ssh: false, keygen: false, winget: true),
            Tools(git: true, ssh: true, keygen: true, winget: true));
        var runner = new StubProcessRunner(request => Success(request.ExecutablePath));
        var service = new RequiredToolInstallerService(toolchain, runner, () => "powershell.exe");

        var result = await service.InstallMissingAsync();

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.AllRequiredToolsAvailable);
        var request = Assert.Single(runner.Requests);
        Assert.Equal("winget.exe", request.ExecutablePath);
        Assert.Equal(
            [
                "install",
                "--id", "Git.Git",
                "--exact",
                "--source", "winget",
                "--silent",
                "--accept-package-agreements",
                "--accept-source-agreements",
                "--disable-interactivity"
            ],
            request.Arguments);
    }

    [Fact]
    public async Task InstallsOpenSshCapabilityThroughElevatedPowerShell()
    {
        var toolchain = new SequenceToolchainService(
            Tools(git: true, ssh: false, keygen: false, winget: true),
            Tools(git: true, ssh: true, keygen: true, winget: true));
        var runner = new StubProcessRunner(request => Success(request.ExecutablePath));
        var service = new RequiredToolInstallerService(toolchain, runner, () => "powershell-test.exe");

        var result = await service.InstallMissingAsync();

        Assert.True(result.Success);
        var request = Assert.Single(runner.Requests);
        Assert.Equal("powershell-test.exe", request.ExecutablePath);
        Assert.Contains("Start-Process", request.Arguments[^1], StringComparison.Ordinal);
        Assert.Contains("-Verb RunAs", request.Arguments[^1], StringComparison.Ordinal);
        Assert.Contains("OpenSSH.Client~~~~0.0.1.0", request.Arguments[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesAutomaticGitInstallWhenWingetIsUnavailable()
    {
        var toolchain = new SequenceToolchainService(
            Tools(git: false, ssh: true, keygen: true, winget: false));
        var runner = new StubProcessRunner(request => Success(request.ExecutablePath));
        var service = new RequiredToolInstallerService(toolchain, runner, () => "powershell.exe");

        var plan = await service.BuildPlanAsync();
        var result = await service.InstallMissingAsync();

        Assert.False(plan.CanInstallAllAutomatically);
        Assert.False(result.Success);
        Assert.Empty(runner.Requests);
        Assert.Contains("Windows Package Manager", result.Message, StringComparison.Ordinal);
    }

    private static ToolchainInfo Tools(bool git, bool ssh, bool keygen, bool winget)
        => new()
        {
            Git = Executable("git.exe", git),
            Ssh = Executable("ssh.exe", ssh),
            SshKeygen = Executable("ssh-keygen.exe", keygen),
            Winget = Executable("winget.exe", winget)
        };

    private static ExecutableInfo Executable(string name, bool exists)
        => new()
        {
            Name = name,
            Exists = exists,
            SelectedPath = exists ? name : null,
            Version = exists ? "test" : null
        };

    private static ProcessResult Success(string executablePath)
        => new()
        {
            ExecutablePath = executablePath,
            ExitCode = 0
        };

    private sealed class SequenceToolchainService : IToolchainService
    {
        private readonly Queue<ToolchainInfo> _values;
        private ToolchainInfo _last;

        public SequenceToolchainService(params ToolchainInfo[] values)
        {
            _values = new Queue<ToolchainInfo>(values);
            _last = values[^1];
        }

        public Task<ToolchainInfo> InspectAsync(CancellationToken cancellationToken = default)
        {
            if (_values.Count > 0)
            {
                _last = _values.Dequeue();
            }

            return Task.FromResult(_last);
        }
    }
}
