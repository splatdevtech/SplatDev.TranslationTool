using SplatDev.TranslationTool.Models;

using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SplatDev.TranslationTool.Services;

/// <summary>
/// Service for translating text using Azure AI Translation
/// </summary>
public class AzureTranslationService
{
    private readonly HttpClient _httpClient;

    public string ApiKey { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Endpoint { get; set; } = "https://api.cognitive.microsofttranslator.com";
    public int RateLimitDelayMs { get; set; } = 100;

    public AzureTranslationService()
    {
        _httpClient = new HttpClient();
    }

    /// <summary>
    /// Retrieves the list of supported translation languages from the Azure service
    /// </summary>
    public async Task<List<LanguageInfo>> GetSupportedLanguagesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var route = "/languages?api-version=3.0&scope=translation";
            using var response = await _httpClient.GetAsync(Endpoint + route, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("translation", out var translationProperty))
            {
                return [];
            }

            var languages = new List<LanguageInfo>();

            foreach (var languageProperty in translationProperty.EnumerateObject())
            {
                var code = languageProperty.Name;
                var languageValue = languageProperty.Value;
                var name = languageValue.GetProperty("name").GetString();
                var nativeName = languageValue.GetProperty("nativeName").GetString();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                languages.Add(LanguageInfo.FromAzureLanguage(code, name, nativeName));
            }

            return [.. languages.OrderBy(lang => lang.DisplayName)];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Detect the language of the given text
    /// </summary>
    public async Task<LanguageDetectionResult?> DetectLanguageAsync(string text, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(Region))
            {
                return null;
            }

            var route = "/detect?api-version=3.0";
            var body = new[] { new { Text = text } };
            var requestBody = JsonSerializer.Serialize(body);

            using var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(Endpoint + route),
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
            };

            request.Headers.Add("Ocp-Apim-Subscription-Key", ApiKey);
            request.Headers.Add("Ocp-Apim-Subscription-Region", Region);

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<List<DetectionResponse>>(jsonResponse);

            if (result?.Count > 0)
            {
                return new LanguageDetectionResult
                {
                    Language = result[0].Language,
                    LanguageName = GetLanguageName(result[0].Language),
                    Score = result[0].Score
                };
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Translate text from source language to target language
    /// If fromLanguage is null or empty, language will be auto-detected
    /// </summary>
    public async Task<TranslationResult> TranslateAsync(
        string text,
        string toLanguage,
        string? fromLanguage = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(Region))
            {
                return TranslationResult.Failure("Azure Translation credentials not configured");
            }

            var route = $"/translate?api-version=3.0&to={toLanguage}";
            if (!string.IsNullOrEmpty(fromLanguage))
            {
                route += $"&from={fromLanguage}";
            }

            var body = new[] { new { Text = text } };
            var requestBody = JsonSerializer.Serialize(body);

            using var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(Endpoint + route),
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
            };

            request.Headers.Add("Ocp-Apim-Subscription-Key", ApiKey);
            request.Headers.Add("Ocp-Apim-Subscription-Region", Region);

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    return TranslationResult.RateLimited("Rate limit reached. Please wait...");
                }
                return TranslationResult.Failure($"Azure Translation API returned {response.StatusCode}");
            }

            var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<List<TranslationResponse>>(jsonResponse);

            if (result?.Count > 0 && result[0].Translations?.Count > 0)
            {
                // Apply rate limit delay
                await Task.Delay(RateLimitDelayMs, cancellationToken);

                var detectedLang = result[0].DetectedLanguage?.Language;
                return TranslationResult.Success(result[0].Translations[0].Text, detectedLang);
            }

            return TranslationResult.Failure("No translation returned from Azure");
        }
        catch (OperationCanceledException)
        {
            return TranslationResult.Cancelled();
        }
        catch (Exception ex)
        {
            return TranslationResult.Failure($"Translation failed: {ex.Message}");
        }
    }

    private static string GetLanguageName(string languageCode)
    {
        return languageCode.ToLower() switch
        {
            "en" => "English",
            "es" => "Spanish",
            "fr" => "French",
            "de" => "German",
            "it" => "Italian",
            "pt" => "Portuguese",
            "pt-br" => "Portuguese (Brazil)",
            "pt-pt" => "Portuguese (Portugal)",
            "ja" => "Japanese",
            "ko" => "Korean",
            "zh-hans" or "zh-cn" => "Chinese (Simplified)",
            "zh-hant" or "zh-tw" => "Chinese (Traditional)",
            "ru" => "Russian",
            "ar" => "Arabic",
            "hi" => "Hindi",
            "nl" => "Dutch",
            "pl" => "Polish",
            "sv" => "Swedish",
            "tr" => "Turkish",
            "th" => "Thai",
            "vi" => "Vietnamese",
            _ => languageCode.ToUpper()
        };
    }

    // Response models
    private class DetectionResponse
    {
        [JsonPropertyName("language")]
        public string Language { get; set; } = string.Empty;

        [JsonPropertyName("score")]
        public double Score { get; set; }

        [JsonPropertyName("isTranslationSupported")]
        public bool IsTranslationSupported { get; set; }

        [JsonPropertyName("isTransliterationSupported")]
        public bool IsTransliterationSupported { get; set; }
    }

    private class TranslationResponse
    {
        [JsonPropertyName("detectedLanguage")]
        public DetectedLanguageInfo? DetectedLanguage { get; set; }

        [JsonPropertyName("translations")]
        public List<TranslationInfo> Translations { get; set; } = [];
    }

    private class DetectedLanguageInfo
    {
        [JsonPropertyName("language")]
        public string Language { get; set; } = string.Empty;

        [JsonPropertyName("score")]
        public double Score { get; set; }
    }

    private class TranslationInfo
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("to")]
        public string To { get; set; } = string.Empty;
    }

    public class LanguageDetectionResult
    {
        public string Language { get; set; } = string.Empty;
        public string LanguageName { get; set; } = string.Empty;
        public double Score { get; set; }
    }
}

/// <summary>
/// Result of a translation operation - Shared with PerplexityTranslationService
/// </summary>
public record TranslationResult
{
    public bool IsSuccess { get; init; }
    public bool IsRateLimited { get; init; }
    public bool IsCancelled { get; init; }
    public string? TranslatedText { get; init; }
    public string? ErrorMessage { get; init; }
    public string? DetectedLanguage { get; init; }

    public static TranslationResult Success(string translatedText, string? detectedLanguage = null) =>
        new() { IsSuccess = true, TranslatedText = translatedText, DetectedLanguage = detectedLanguage };

    public static TranslationResult Failure(string errorMessage) =>
        new() { IsSuccess = false, ErrorMessage = errorMessage };

    public static TranslationResult RateLimited(string message) =>
        new() { IsSuccess = false, IsRateLimited = true, ErrorMessage = message };

    public static TranslationResult Cancelled() =>
        new() { IsSuccess = false, IsCancelled = true, ErrorMessage = "Operation cancelled" };
}

