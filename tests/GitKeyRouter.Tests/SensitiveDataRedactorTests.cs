using GitKeyRouter.Infrastructure.Logging;

namespace GitKeyRouter.Tests;

public sealed class SensitiveDataRedactorTests
{
    [Fact]
    public void Redact_RemovesPrivateKeyBody()
    {
        var input = "before\n-----BEGIN OPENSSH PRIVATE KEY-----\nsecret-data\n-----END OPENSSH PRIVATE KEY-----\nafter";

        var output = SensitiveDataRedactor.Redact(input);

        Assert.DoesNotContain("secret-data", output);
        Assert.DoesNotContain("BEGIN OPENSSH PRIVATE KEY", output);
        Assert.Contains("[REDACTED OPENSSH PRIVATE KEY]", output);
    }
}
