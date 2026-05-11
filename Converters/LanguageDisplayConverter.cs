using SplatDev.TranslationTool.Models;

using System.Globalization;
using System.Windows.Data;

namespace SplatDev.TranslationTool.Converters;

/// <summary>
/// Converter to display language availability status with formatting
/// </summary>
public class LanguageDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not LanguageInfo lang || parameter is not string availableLanguageCodes)
            return value?.ToString();

        var codes = availableLanguageCodes.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var hasExistingFile = codes.Contains(lang.Code, StringComparer.OrdinalIgnoreCase);

        if (hasExistingFile && targetType == typeof(object))
        {
            // Return formatted display name with visual indicator
            return $"✓ {lang.DisplayName}";
        }

        return lang.DisplayName;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
