using GitKeyRouter.Core.Services;

namespace GitKeyRouter.Tests;

public sealed class SshPathConverterTests
{
    [Fact]
    public void ConvertWindowsPathToOpenSsh_UsesForwardSlashes()
    {
        var result = SshConfigService.ConvertWindowsPathToOpenSsh(@"C:\Users\fgc01\.ssh\id_ed25519_camus");

        Assert.Equal("C:/Users/fgc01/.ssh/id_ed25519_camus", result);
        Assert.DoesNotContain('\\', result);
    }
}
