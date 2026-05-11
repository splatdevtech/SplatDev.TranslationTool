using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SplatDev.TranslationTool.Converters;

/// <summary>
/// Multi-value converter to show language availability status
/// </summary>
public class LanguageAvailabilityConverter : IMultiValueConverter
{
    public static readonly LanguageAvailabilityConverter Instance = new();

    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return targetType == typeof(Visibility) ? Visibility.Collapsed : FontWeights.Normal;

        var languageCode = values[0] as string;
        var availableLanguageCodesObj = values[1];

        if (string.IsNullOrEmpty(languageCode) || availableLanguageCodesObj == null)
            return targetType == typeof(Visibility) ? Visibility.Collapsed : FontWeights.Normal;

        // Convert available codes to list
        var availableCodes = new List<string>();
        if (availableLanguageCodesObj is List<string> list)
        {
            availableCodes = list;
        }

        var hasExistingFile = availableCodes.Contains(languageCode, StringComparer.OrdinalIgnoreCase);

        if (targetType == typeof(Visibility))
        {
            return hasExistingFile ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (targetType == typeof(FontWeight))
        {
            return hasExistingFile ? FontWeights.Bold : FontWeights.Normal;
        }

        return targetType == typeof(Visibility) ? Visibility.Collapsed : FontWeights.Normal;
    }

    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
