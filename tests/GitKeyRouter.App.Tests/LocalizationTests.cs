using GitKeyRouter.App.Presentation;

namespace GitKeyRouter.App.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("en", AppLanguage.English)]
    [InlineData("en-US", AppLanguage.English)]
    [InlineData("zh-CN", AppLanguage.SimplifiedChinese)]
    [InlineData(null, AppLanguage.SimplifiedChinese)]
    [InlineData("unsupported", AppLanguage.SimplifiedChinese)]
    public void ParseReturnsSupportedLanguage(string? code, AppLanguage expected)
        => Assert.Equal(expected, AppLocalization.Parse(code));

    [Fact]
    public void TranslationUsesCurrentLanguage()
    {
        try
        {
            AppLocalization.SetLanguage(AppLanguage.SimplifiedChinese);
            Assert.Equal("中文", AppLocalization.T("中文", "English"));
            Assert.Equal("zh-CN", AppLocalization.CurrentCode);

            AppLocalization.SetLanguage(AppLanguage.English);
            Assert.Equal("English", AppLocalization.T("中文", "English"));
            Assert.Equal("en-US", AppLocalization.CurrentCode);
        }
        finally
        {
            AppLocalization.SetLanguage(AppLanguage.SimplifiedChinese);
        }
    }
}
