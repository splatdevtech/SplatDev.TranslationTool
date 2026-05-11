using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SplatDev.TranslationTool.Services;

/// <summary>
/// Service for translating text using Perplexity AI API
/// </summary>
public class PerplexityTranslationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PerplexityTranslationService> _logger;
    private const string ApiBaseUrl = "https://api.perplexity.ai";

    public string? ApiKey { get; set; }
    public int RateLimitDelayMs { get; set; } = 500;

    public PerplexityTranslationService(
        IHttpClientFactory httpClientFactory,
        ILogger<PerplexityTranslationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Translates text to target language using Perplexity AI
    /// </summary>
    public async Task<TranslationResult> TranslateAsync(
        string text,
        string targetLanguageCode,
        string targetLanguageName,
        CancellationToken cancellationToken = default)
    {
        const int maxRetries = 2;
        
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            var result = await TranslateInternalAsync(text, targetLanguageCode, targetLanguageName, attempt > 0, cancellationToken);
            
            if (!result.IsSuccess)
                return result;

            // Validate that the translation is not still in English (for non-trivial text)
            if (text.Length > 3 && IsLikelyStillEnglish(text, result.TranslatedText!, targetLanguageCode))
            {
                _logger.LogWarning("Translation appears to still be in English, attempt {Attempt}/{MaxRetries}", 
                    attempt + 1, maxRetries + 1);
                
                if (attempt < maxRetries)
                {
                    await Task.Delay(RateLimitDelayMs, cancellationToken);
                    continue;
                }
                
                return TranslationResult.Failure("Translation returned English text instead of target language");
            }

            return result;
        }

        return TranslationResult.Failure("Translation validation failed after retries");
    }

    /// <summary>
    /// Checks if the translation appears to still be in English
    /// </summary>
    private bool IsLikelyStillEnglish(string originalText, string translatedText, string targetLanguageCode)
    {
        // If the text is identical (case-insensitive), it's likely not translated
        if (string.Equals(originalText.Trim(), translatedText.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        // For languages with non-Latin scripts, check if result contains expected characters
        var nonLatinLanguages = new Dictionary<string, Func<string, bool>>
        {
            ["zh"] = text => text.Any(c => c >= 0x4E00 && c <= 0x9FFF), // Chinese
            ["ja"] = text => text.Any(c => (c >= 0x3040 && c <= 0x309F) || (c >= 0x30A0 && c <= 0x30FF) || (c >= 0x4E00 && c <= 0x9FFF)), // Japanese
            ["ko"] = text => text.Any(c => c >= 0xAC00 && c <= 0xD7AF), // Korean
            ["ar"] = text => text.Any(c => c >= 0x0600 && c <= 0x06FF), // Arabic
            ["he"] = text => text.Any(c => c >= 0x0590 && c <= 0x05FF), // Hebrew
            ["th"] = text => text.Any(c => c >= 0x0E00 && c <= 0x0E7F), // Thai
            ["ru"] = text => text.Any(c => c >= 0x0400 && c <= 0x04FF), // Russian/Cyrillic
            ["uk"] = text => text.Any(c => c >= 0x0400 && c <= 0x04FF), // Ukrainian/Cyrillic
            ["bg"] = text => text.Any(c => c >= 0x0400 && c <= 0x04FF), // Bulgarian/Cyrillic
            ["el"] = text => text.Any(c => c >= 0x0370 && c <= 0x03FF), // Greek
            ["hi"] = text => text.Any(c => c >= 0x0900 && c <= 0x097F), // Hindi/Devanagari
        };

        // Get base language code (e.g., "zh-CN" -> "zh")
        var baseCode = targetLanguageCode.Split('-')[0].ToLowerInvariant();

        if (nonLatinLanguages.TryGetValue(baseCode, out var validator))
        {
            // For non-Latin languages, the translation should contain script-specific characters
            return !validator(translatedText);
        }

        // For Latin-script languages, we can't easily detect if it's still English
        // But we can check if it's exactly the same
        return false;
    }

    private async Task<TranslationResult> TranslateInternalAsync(
        string text,
        string targetLanguageCode,
        string targetLanguageName,
        bool isRetry,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(ApiKey))
            {
                return TranslationResult.Failure("API key not configured");
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return TranslationResult.Success(text);
            }

            var systemPrompt = "You are a translation engine. You translate text from English to other languages. " +
                "You output ONLY the translated text with no explanations, no quotes, no markdown, and no additional commentary. " +
                "Preserve any placeholders like {0}, {1}, {{0}}, {{1}} exactly as they appear." +
                (isRetry ? " IMPORTANT: You MUST translate to the target language, do NOT return English text." : "");

            var userPrompt = isRetry 
                ? $"Translate the following English text to {targetLanguageName}. Return ONLY the {targetLanguageName} translation:\n\n{text}"
                : $"Translate to {targetLanguageName}:\n\n{text}";

            var request = new
            {
                model = "sonar",
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                }
            };

            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");
            httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

            var response = await httpClient.PostAsJsonAsync(
                $"{ApiBaseUrl}/chat/completions",
                request,
                cancellationToken);

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Check for rate limiting
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    return TranslationResult.RateLimited("Rate limit reached. Please wait...");
                }

                _logger.LogError("Perplexity API error: {StatusCode} - {Content}",
                    response.StatusCode, responseContent);
                return TranslationResult.Failure($"API error: {response.StatusCode}");
            }

            var responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);

            if (responseJson.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentElement))
                {
                    var translation = contentElement.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(translation))
                    {
                        return TranslationResult.Success(translation);
                    }
                }
            }

            return TranslationResult.Failure("No translation content in response");
        }
        catch (OperationCanceledException)
        {
            return TranslationResult.Cancelled();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error translating text");
            return TranslationResult.Failure($"Error: {ex.Message}");
        }
    }
}


