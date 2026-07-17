using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;

namespace GitKeyRouter.Tests;

public sealed class SshKeyFormatTests
{
    [Fact]
    public void DetectsOpenSshPublicKey()
    {
        var result = SshKeyFormatDetector.Detect("ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIAABAgMEBQYHCAkKCwwNDg8QERITFBUWFxgZGhscHR4f test@example.com", "id.pub");

        Assert.Equal(SshKeyFormat.OpenSshPublic, result.Format);
        Assert.True(result.IsOpenSsh);
        Assert.False(result.IsPrivateMaterial);
        Assert.Equal("ssh-ed25519", result.Algorithm);
        Assert.Equal("ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIAABAgMEBQYHCAkKCwwNDg8QERITFBUWFxgZGhscHR4f test@example.com", result.PublicKeyText);
    }

    [Fact]
    public void DetectsRfc4716AndPemPublicKeys()
    {
        var rfc = SshKeyFormatDetector.Detect("---- BEGIN SSH2 PUBLIC KEY ----\nAQID\n---- END SSH2 PUBLIC KEY ----");
        var pem = SshKeyFormatDetector.Detect("-----BEGIN PUBLIC KEY-----\nAQID\n-----END PUBLIC KEY-----");

        Assert.Equal(SshKeyFormat.Rfc4716Public, rfc.Format);
        Assert.True(rfc.CanConvert);
        Assert.Equal(SshKeyFormat.PemPublic, pem.Format);
        Assert.True(pem.CanConvert);
    }

    [Theory]
    [InlineData("-----BEGIN OPENSSH PRIVATE KEY-----\nsecret", SshKeyFormat.OpenSshPrivate, true)]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\nsecret", SshKeyFormat.PemPrivate, true)]
    [InlineData("PuTTY-User-Key-File-3: ssh-rsa", SshKeyFormat.PuttyPrivate, false)]
    public void DetectsPrivateFormatsWithoutExposingContent(string text, SshKeyFormat format, bool canConvert)
    {
        var result = SshKeyFormatDetector.Detect(text);

        Assert.Equal(format, result.Format);
        Assert.True(result.IsPrivateMaterial);
        Assert.Equal(canConvert, result.CanConvert);
        Assert.Empty(result.PublicKeyText);
    }

    [Fact]
    public void RejectsOpenSshBlobWhoseEmbeddedAlgorithmDoesNotMatch()
    {
        var result = SshKeyFormatDetector.Detect("ssh-rsa AAAAC3NzaC1lZDI1NTE5AAAAIAABAgMEBQYHCAkKCwwNDg8QERITFBUWFxgZGhscHR4f comment");

        Assert.Equal(SshKeyFormat.Unknown, result.Format);
        Assert.False(result.IsOpenSsh);
    }

    [Fact]
    public void RejectsInvalidOpenSshBase64()
    {
        var result = SshKeyFormatDetector.Detect("ssh-ed25519 not-base64 comment");

        Assert.Equal(SshKeyFormat.Unknown, result.Format);
        Assert.False(result.IsOpenSsh);
    }
}
