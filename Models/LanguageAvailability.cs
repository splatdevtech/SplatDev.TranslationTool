namespace SplatDev.TranslationTool.Models;

/// <summary>
/// Represents language availability information for target languages
/// </summary>
public record LanguageAvailability(string Code, string DisplayName, bool HasExistingFile = false)
{
    /// <summary>
    /// Gets the display name with bold formatting applied for existing files
    /// </summary>
    public string GetFormattedDisplayName() => HasExistingFile ? $"**{DisplayName}**" : DisplayName;
}
