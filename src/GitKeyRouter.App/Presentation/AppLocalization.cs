namespace GitKeyRouter.App.Presentation;

public enum AppLanguage
{
    SimplifiedChinese,
    English
}

public static class AppLocalization
{
    private static AppLanguage _currentLanguage = AppLanguage.SimplifiedChinese;

    public static AppLanguage CurrentLanguage => _currentLanguage;

    public static string CurrentCode => _currentLanguage == AppLanguage.English ? "en-US" : "zh-CN";

    public static AppLanguage Parse(string? languageCode)
        => languageCode?.Trim().StartsWith("en", StringComparison.OrdinalIgnoreCase) == true
            ? AppLanguage.English
            : AppLanguage.SimplifiedChinese;

    public static void SetLanguage(AppLanguage language)
        => _currentLanguage = language;

    public static string T(string simplifiedChinese, string english)
        => _currentLanguage == AppLanguage.English ? english : simplifiedChinese;

    public static string DisplayName(AppLanguage language)
        => language == AppLanguage.English ? "English" : "简体中文";
}
