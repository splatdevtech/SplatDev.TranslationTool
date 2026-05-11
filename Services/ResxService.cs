using Microsoft.Extensions.Logging;

using SplatDev.TranslationTool.Extensions;
using SplatDev.TranslationTool.Helpers;
using SplatDev.TranslationTool.Models;

using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace SplatDev.TranslationTool.Services;

/// <summary>
/// Service for reading and writing localization resource files.
/// Supports both .resx and language_pack-*.xml formats.
/// </summary>
public class ResxService(ILogger<ResxService> logger)
{
    private static readonly Lazy<HashSet<string>> KnownLanguageCodes = new(
        () => CultureInfo
            .GetCultures(CultureTypes.AllCultures)
            .Where(culture => !string.IsNullOrWhiteSpace(culture.Name))
            .Select(culture => culture.Name.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase));

    private readonly ILogger<ResxService> _logger = logger;

    /// <summary>
    /// Reads all entries from a localization file.
    /// </summary>
    public List<ResxEntry> ReadResxFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.LogError("File not found: {FilePath}", filePath);
                return [];
            }

            var extension = Path.GetExtension(filePath);
            var entries = string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase)
                ? ReadLanguagePackXmlFile(filePath)
                : ReadResxEntries(filePath);

            _logger.LogInformationLazy("Read {Count} entries from {FilePath}", () => [entries.Count, filePath]);
            return entries;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading localization file: {FilePath}", filePath);
            return [];
        }
    }

    /// <summary>
    /// Writes entries to a localization file, creating or updating it.
    /// </summary>
    public bool WriteResxFile(string filePath, List<ResxEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        try
        {
            var extension = Path.GetExtension(filePath);
            var succeeded = string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase)
                ? WriteLanguagePackXmlFile(filePath, entries)
                : WriteResxEntries(filePath, entries);

            if (succeeded)
            {
                _logger.LogInformationLazy("Saved {Count} entries to {FilePath}", () => [entries.Count, filePath]);
            }

            return succeeded;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing localization file: {FilePath}", filePath);
            return false;
        }
    }

    /// <summary>
    /// Gets the target file path for a specific language.
    /// </summary>
    public static string GetTargetFilePath(string sourceFilePath, string languageCode)
    {
        var directory = Path.GetDirectoryName(sourceFilePath) ?? string.Empty;
        var extension = Path.GetExtension(sourceFilePath);
        var fileName = Path.GetFileNameWithoutExtension(sourceFilePath);

        if (string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase))
        {
            TryGetLanguageAwareBaseName(fileName, '-', out var baseName);
            var xmlLanguageCode = NormalizeToXmlLanguageCode(languageCode);

            return Path.Combine(directory, $"{baseName}-{xmlLanguageCode}{extension}");
        }

        TryGetLanguageAwareBaseName(fileName, '.', out var resourceBaseName);
        var normalizedLanguageCode = LanguageCodeHelper.NormalizeToResourceCode(languageCode);
        if (string.IsNullOrEmpty(normalizedLanguageCode))
        {
            normalizedLanguageCode = languageCode;
        }

        return Path.Combine(directory, $"{resourceBaseName}.{normalizedLanguageCode}.resx");
    }

    /// <summary>
    /// Gets available language codes in the same folder as the source file.
    /// </summary>
    public static List<string> GetAvailableLanguageCodes(string sourceFilePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(sourceFilePath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return [];
            }

            var extension = Path.GetExtension(sourceFilePath);

            return string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase)
                ? GetAvailableLanguageCodesForLanguagePackXml(sourceFilePath)
                : GetAvailableLanguageCodesForResx(sourceFilePath);
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static List<ResxEntry> ReadResxEntries(string filePath)
    {
        var entries = new List<ResxEntry>();
        var doc = XDocument.Load(filePath);

        var dataElements = doc.Descendants("data")
            .Where(d => d.Attribute("type") == null);

        foreach (var data in dataElements)
        {
            var name = data.Attribute("name")?.Value;
            var value = data.Element("value")?.Value;
            var comment = data.Element("comment")?.Value;

            if (!string.IsNullOrEmpty(name) && value != null)
            {
                entries.Add(new ResxEntry(name, value, comment));
            }
        }

        return entries;
    }

    private static bool WriteResxEntries(string filePath, List<ResxEntry> entries)
    {
        XDocument doc;

        if (File.Exists(filePath))
        {
            doc = XDocument.Load(filePath);
        }
        else
        {
            doc = CreateEmptyResxDocument();
        }

        var root = doc.Root;
        if (root == null)
        {
            return false;
        }

        foreach (var entry in entries)
        {
            var existingData = root.Elements("data")
                .FirstOrDefault(d => string.Equals(d.Attribute("name")?.Value, entry.Key, StringComparison.Ordinal));

            if (existingData != null)
            {
                var valueElement = existingData.Element("value");
                if (valueElement != null)
                {
                    valueElement.Value = entry.Value;
                }
                else
                {
                    existingData.Add(new XElement("value", entry.Value));
                }

                var commentElement = existingData.Element("comment");
                if (!string.IsNullOrEmpty(entry.Comment))
                {
                    if (commentElement != null)
                    {
                        commentElement.Value = entry.Comment;
                    }
                    else
                    {
                        existingData.Add(new XElement("comment", entry.Comment));
                    }
                }
                else
                {
                    commentElement?.Remove();
                }
            }
            else
            {
                var dataElement = new XElement("data",
                    new XAttribute("name", entry.Key),
                    new XAttribute(XNamespace.Xml + "space", "preserve"),
                    new XElement("value", entry.Value));

                if (!string.IsNullOrEmpty(entry.Comment))
                {
                    dataElement.Add(new XElement("comment", entry.Comment));
                }

                root.Add(dataElement);
            }
        }

        doc.Save(filePath);
        return true;
    }

    private static List<ResxEntry> ReadLanguagePackXmlFile(string filePath)
    {
        var entries = new List<ResxEntry>();
        var doc = XDocument.Load(filePath);

        foreach (var localeResource in doc.Descendants("LocaleResource"))
        {
            var key = localeResource.Attribute("Name")?.Value;
            var value = localeResource.Element("Value")?.Value;

            if (!string.IsNullOrWhiteSpace(key) && value != null)
            {
                entries.Add(new ResxEntry(key, value));
            }
        }

        return entries;
    }

    private static bool WriteLanguagePackXmlFile(string filePath, List<ResxEntry> entries)
    {
        XDocument doc;

        if (File.Exists(filePath))
        {
            doc = XDocument.Load(filePath);
        }
        else
        {
            doc = CreateEmptyLanguagePackDocument();
        }

        var root = doc.Root;
        if (root == null)
        {
            return false;
        }

        foreach (var entry in entries)
        {
            var existingNode = root.Elements("LocaleResource")
                .FirstOrDefault(node => string.Equals(node.Attribute("Name")?.Value, entry.Key, StringComparison.Ordinal));

            if (existingNode != null)
            {
                var valueElement = existingNode.Element("Value");
                if (valueElement != null)
                {
                    valueElement.Value = entry.Value;
                }
                else
                {
                    existingNode.Add(new XElement("Value", entry.Value));
                }
            }
            else
            {
                root.Add(new XElement("LocaleResource",
                    new XAttribute("Name", entry.Key),
                    new XElement("Value", entry.Value)));
            }
        }

        doc.Save(filePath);
        return true;
    }

    private static List<string> GetAvailableLanguageCodesForResx(string sourceFilePath)
    {
        var directory = Path.GetDirectoryName(sourceFilePath);
        if (string.IsNullOrEmpty(directory))
        {
            return [];
        }

        var fileName = Path.GetFileNameWithoutExtension(sourceFilePath);
        TryGetLanguageAwareBaseName(fileName, '.', out var baseName);

        var files = Directory.GetFiles(directory, $"{baseName}.*.resx");
        var availableCodes = new List<string>();

        foreach (var file in files)
        {
            var fileNameOnly = Path.GetFileNameWithoutExtension(file);
            var prefix = $"{baseName}.";
            if (!fileNameOnly.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var langCode = fileNameOnly[prefix.Length..];
            if (!IsLanguageCode(langCode))
            {
                continue;
            }

            var normalizedLangCode = LanguageCodeHelper.NormalizeToResourceCode(langCode);
            if (!string.IsNullOrEmpty(normalizedLangCode) && !availableCodes.Contains(normalizedLangCode, StringComparer.OrdinalIgnoreCase))
            {
                availableCodes.Add(normalizedLangCode);
            }
        }

        return availableCodes;
    }

    private static List<string> GetAvailableLanguageCodesForLanguagePackXml(string sourceFilePath)
    {
        var directory = Path.GetDirectoryName(sourceFilePath);
        if (string.IsNullOrEmpty(directory))
        {
            return [];
        }

        var fileName = Path.GetFileNameWithoutExtension(sourceFilePath);
        TryGetLanguageAwareBaseName(fileName, '-', out var baseName);

        var files = Directory.GetFiles(directory, $"{baseName}-*.xml");
        var availableCodes = new List<string>();

        foreach (var file in files)
        {
            var fileNameOnly = Path.GetFileNameWithoutExtension(file);
            var prefix = $"{baseName}-";
            if (!fileNameOnly.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var langCode = fileNameOnly[prefix.Length..];
            if (!IsLanguageCode(langCode))
            {
                continue;
            }

            var normalizedLangCode = LanguageCodeHelper.NormalizeToResourceCode(langCode);
            if (!string.IsNullOrEmpty(normalizedLangCode) && !availableCodes.Contains(normalizedLangCode, StringComparer.OrdinalIgnoreCase))
            {
                availableCodes.Add(normalizedLangCode);
            }
        }

        return availableCodes;
    }

    private static bool TryGetLanguageAwareBaseName(string fileNameWithoutExtension, char separator, out string baseName)
    {
        baseName = fileNameWithoutExtension;

        for (var index = 0; index < fileNameWithoutExtension.Length; index++)
        {
            if (fileNameWithoutExtension[index] != separator)
            {
                continue;
            }

            var candidate = fileNameWithoutExtension[(index + 1)..];
            if (IsLanguageCode(candidate))
            {
                baseName = fileNameWithoutExtension[..index];
                return true;
            }
        }

        return false;
    }

    private static bool IsLanguageCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var normalized = code.Replace('_', '-').Trim().ToLowerInvariant();
        return KnownLanguageCodes.Value.Contains(normalized);
    }

    private static string NormalizeToXmlLanguageCode(string code)
    {
        var normalized = LanguageCodeHelper.NormalizeToResourceCode(code);
        var value = string.IsNullOrEmpty(normalized) ? code : normalized;

        return value.Replace('_', '-').ToLowerInvariant();
    }

    private static XDocument CreateEmptyResxDocument()
    {
        return XDocument.Parse(@"<?xml version=""1.0"" encoding=""utf-8""?>
<root>
  <resheader name=""resmimetype"">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name=""version"">
    <value>2.0</value>
  </resheader>
  <resheader name=""reader"">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <resheader name=""writer"">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
</root>");
    }

    private static XDocument CreateEmptyLanguagePackDocument()
    {
        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("Language",
                new XAttribute("Name", "Translated"),
                new XAttribute("SupportedVersion", "1.0")));
    }
}
