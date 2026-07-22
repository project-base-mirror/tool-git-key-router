using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Tests;

public sealed class AppConfigLocalizationTests
{
    [Theory]
    [InlineData("en", "en-US")]
    [InlineData("en-GB", "en-US")]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("unsupported", "zh-CN")]
    public void NormalizeKeepsOnlySupportedUiLanguages(string input, string expected)
    {
        var config = new AppConfig { UiLanguage = input };

        config.Normalize();

        Assert.Equal(expected, config.UiLanguage);
    }
}
