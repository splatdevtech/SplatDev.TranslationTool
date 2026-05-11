using SplatDev.TranslationTool.Helpers;

namespace SplatDev.TranslationTool.Models;

/// <summary>
/// Represents a supported language for translation
/// </summary>
public record LanguageInfo(string Code, string DisplayName)
{
    public string ResourceCode => LanguageCodeHelper.NormalizeToResourceCode(Code);

    /// <summary>
    /// Creates a <see cref="LanguageInfo"/> entry from the Azure Languages response
    /// </summary>
    public static LanguageInfo FromAzureLanguage(string code, string name, string? nativeName)
    {
        var displayName = string.IsNullOrWhiteSpace(nativeName) || string.Equals(name, nativeName, StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{name} ({nativeName})";

        return new(code, $"{displayName} ({code})");
    }

    /// <summary>
    /// Gets the fallback languages when the live list cannot be retrieved
    /// </summary>
    public static List<LanguageInfo> GetFallbackLanguages() =>
    [
        new("pt-BR", "Português (Brasil)"),
        new("es", "Español (Generic)"),
        new("it", "Italiano"),
        new("ja", "日本語 (Japanese)"),
        new("zh-CN", "简体中文 (Chinese Simplified)"),
        new("ru", "Русский (Russian)"),
        new("tr", "Türkçe (Turkish)"),
        new("ar", "العربية (Arabic)"),
        new("de", "Deutsch (German)"),
        new("fr", "Français (French)"),
        new("hi", "हिन्दी (Hindi)"),
        new("nl", "Nederlands (Dutch)")
    ];
}

/// <summary>
/// Represents a single resource entry from a .resx file
/// </summary>
public record ResxEntry(string Key, string Value, string? Comment = null);
