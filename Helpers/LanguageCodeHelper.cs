using System;
using System.Collections.Generic;

namespace SplatDev.TranslationTool.Helpers;

public static class LanguageCodeHelper
{
    private static readonly Dictionary<string, string> CanonicalResourceCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pt"] = "pt-BR",
        ["pt-br"] = "pt-BR",
        ["zh"] = "zh-CN",
        ["zh-cn"] = "zh-CN",
        ["zh-hans"] = "zh-CN"
    };

    public static string NormalizeToResourceCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        var sanitized = code.Replace('_', '-');

        if (CanonicalResourceCodes.TryGetValue(sanitized, out var canonical))
        {
            return canonical;
        }

        var parts = sanitized.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            return $"{parts[0].ToLowerInvariant()}-{parts[1].ToUpperInvariant()}";
        }

        return sanitized.ToLowerInvariant();
    }
}
