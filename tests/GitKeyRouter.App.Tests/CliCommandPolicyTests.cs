using GitKeyRouter.App.Cli;

namespace GitKeyRouter.App.Tests;

public sealed class CliCommandPolicyTests
{
    [Theory]
    [InlineData("version")]
    [InlineData("--version")]
    [InlineData("-V")]
    [InlineData("help")]
    [InlineData("--help")]
    [InlineData("-H")]
    public void BootstrapCommandsRunWithoutApplicationServices(string command)
    {
        Assert.True(CliApplication.CanRunWithoutServices([command]));
        Assert.False(CliApplication.RequiresExclusiveInstance([command]));
    }

    [Theory]
    [InlineData("diagnose")]
    [InlineData("list-services")]
    [InlineData("list-identities")]
    [InlineData("list-profiles")]
    [InlineData("list-routes")]
    [InlineData("parse-url")]
    [InlineData("resolve-profile")]
    [InlineData("test-service")]
    [InlineData("test-route")]
    [InlineData("test-ssh")]
    [InlineData("gh-login")]
    [InlineData("gh-logout")]
    [InlineData("gh-status")]
    [InlineData("gh-resolve")]
    [InlineData("gh")]
    [InlineData("apply")]
    [InlineData("apply-profiles")]
    [InlineData("ssh-backend")]
    [InlineData("trust-host")]
    public void ReadOnlyAndPreviewCommandsDoNotTakeExclusiveInstanceLock(string command)
    {
        Assert.False(CliApplication.CanRunWithoutServices([command]));
        Assert.False(CliApplication.RequiresExclusiveInstance([command]));
    }

    [Theory]
    [InlineData("save-service")]
    [InlineData("SAVE-IDENTITY")]
    [InlineData("save-route")]
    [InlineData("save-profile")]
    [InlineData("save-profile-rule")]
    [InlineData("delete-service")]
    [InlineData("delete-identity")]
    [InlineData("delete-route")]
    [InlineData("delete-profile")]
    [InlineData("delete-profile-rule")]
    [InlineData("apply")]
    [InlineData("APPLY-PROFILES")]
    [InlineData("SSH-BACKEND")]
    [InlineData("TRUST-HOST")]
    public void ConfirmedMutatingCommandsTakeExclusiveInstanceLock(string command)
    {
        Assert.True(CliApplication.RequiresExclusiveInstance([command, "--YES"]));
    }

    [Fact]
    public void GuiStartupTakesExclusiveInstanceLock()
    {
        Assert.False(CliApplication.CanRunWithoutServices([]));
        Assert.True(CliApplication.RequiresExclusiveInstance([]));
    }
}
