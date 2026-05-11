using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.DependencyInjection;

using SplatDev.TranslationTool.Models;
using SplatDev.TranslationTool.Services;

using System.Collections.ObjectModel;
using System.IO;

using Application = System.Windows.Application;
using WinForms = System.Windows.Forms;

namespace SplatDev.TranslationTool.ViewModels;

/// <summary>
/// Main view model for the translation tool
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly PerplexityTranslationService _perplexityService;
    private readonly AzureTranslationService _azureService;
    private readonly ResxService _resxService;
    private CancellationTokenSource? _cancellationTokenSource;

    [ObservableProperty]
    private string _sourceFilePath = string.Empty;

    [ObservableProperty]
    private LanguageInfo? _selectedLanguage;

    [ObservableProperty]
    private string _perplexityApiKey = string.Empty;

    [ObservableProperty]
    private string _azureApiKey = string.Empty;

    [ObservableProperty]
    private string _azureRegion = string.Empty;

    [ObservableProperty]
    private string _azureEndpoint = "https://api.cognitive.microsofttranslator.com";

    [ObservableProperty]
    private bool _useAzureTranslation = true;

    [ObservableProperty]
    private bool _detectLanguage = true;

    [ObservableProperty]
    private int _rateLimitDelay = 500;

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private int _progressMax = 100;

    [ObservableProperty]
    private string _progressText = "Ready";

    [ObservableProperty]
    private string _statusMessage = "Select a source .resx file and target language to begin";

    [ObservableProperty]
    private bool _isTranslating;

    [ObservableProperty]
    private bool _canTranslate;

    [ObservableProperty]
    private bool _canOpenEditor;

    public ObservableCollection<LanguageInfo> AvailableLanguages { get; } = [];
    public ObservableCollection<string> TranslationLog { get; } = [];

    [ObservableProperty]
    private List<string> _availableLanguageCodes = [];

    public MainViewModel(
        PerplexityTranslationService perplexityService,
        AzureTranslationService azureService,
        ResxService resxService)
    {
        _perplexityService = perplexityService;
        _azureService = azureService;
        _resxService = resxService;

        _ = LoadSupportedLanguagesAsync();
    }

    private async Task LoadSupportedLanguagesAsync()
    {
        var languages = await _azureService.GetSupportedLanguagesAsync();
        var fallbackLanguages = LanguageInfo.GetFallbackLanguages();

        if (languages.Count == 0)
        {
            languages = fallbackLanguages;
            StatusMessage = "Unable to retrieve live language list. Using fallback languages.";
        }
        else
        {
            var existingCodes = new HashSet<string>(languages.Select(lang => lang.Code), StringComparer.OrdinalIgnoreCase);
            foreach (var fallback in fallbackLanguages)
            {
                if (existingCodes.Add(fallback.Code))
                {
                    languages.Add(fallback);
                }
            }
        }

        UpdateLanguageCollection(languages);
    }

    partial void OnSourceFilePathChanged(string value)
    {
        UpdateAvailableLanguages();
        UpdateCanTranslate();
    }

    partial void OnSelectedLanguageChanged(LanguageInfo? value)
    {
        UpdateCanTranslate();
    }

    partial void OnPerplexityApiKeyChanged(string value)
    {
        _perplexityService.ApiKey = value;
        UpdateCanTranslate();
    }

    partial void OnAzureApiKeyChanged(string value)
    {
        _azureService.ApiKey = value;
        UpdateCanTranslate();
    }

    partial void OnAzureRegionChanged(string value)
    {
        _azureService.Region = value;
        UpdateCanTranslate();
    }

    partial void OnAzureEndpointChanged(string value)
    {
        _azureService.Endpoint = value;
        UpdateCanTranslate();
    }

    partial void OnRateLimitDelayChanged(int value)
    {
        _perplexityService.RateLimitDelayMs = value;
        _azureService.RateLimitDelayMs = value;
    }

    private void UpdateAvailableLanguages()
    {
        if (!string.IsNullOrEmpty(SourceFilePath) && File.Exists(SourceFilePath))
        {
            AvailableLanguageCodes = ResxService.GetAvailableLanguageCodes(SourceFilePath);
        }
        else
        {
            AvailableLanguageCodes = [];
        }

        // Trigger a refresh of the UI binding
        OnPropertyChanged(nameof(AvailableLanguages));
    }

    private void UpdateLanguageCollection(IEnumerable<LanguageInfo> languages)
    {
        var previousCode = SelectedLanguage?.Code;
        AvailableLanguages.Clear();

        foreach (var language in languages)
        {
            AvailableLanguages.Add(language);
        }

        if (!string.IsNullOrEmpty(previousCode))
        {
            SelectedLanguage = AvailableLanguages.FirstOrDefault(lang => string.Equals(lang.Code, previousCode, System.StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedLanguage == null && AvailableLanguages.Count > 0)
        {
            SelectedLanguage = AvailableLanguages[0];
        }
    }

    private void UpdateCanTranslate()
    {
        var hasValidCredentials = UseAzureTranslation
            ? !string.IsNullOrEmpty(AzureApiKey) && !string.IsNullOrEmpty(AzureRegion)
            : !string.IsNullOrEmpty(PerplexityApiKey);

        CanTranslate = !string.IsNullOrEmpty(SourceFilePath)
            && File.Exists(SourceFilePath)
            && SelectedLanguage != null
            && hasValidCredentials
            && !IsTranslating;

        CanOpenEditor = !string.IsNullOrEmpty(SourceFilePath)
            && File.Exists(SourceFilePath)
            && SelectedLanguage != null
            && hasValidCredentials;
    }

    [RelayCommand]
    private void OpenEditor()
    {
        if (SelectedLanguage == null || string.IsNullOrEmpty(SourceFilePath))
            return;

        var app = (App)Application.Current;
        var editorWindow = app.ServiceProvider.GetRequiredService<TranslationEditorWindow>();
        var targetFilePath = ResxService.GetTargetFilePath(SourceFilePath, SelectedLanguage.Code);

        editorWindow.LoadTranslations(SourceFilePath, targetFilePath, SelectedLanguage, UseAzureTranslation, DetectLanguage);
        editorWindow.Show();

        AddLog($"Opened editor for {SelectedLanguage.DisplayName}");
    }


    [RelayCommand]
    private void BrowseSourceFile()
    {
        try
        {
            using var dialog = new WinForms.OpenFileDialog
            {
                Filter = "Localization Files (*.resx;language_pack-*.xml)|*.resx;language_pack-*.xml|RESX Files (*.resx)|*.resx|Language Pack XML (language_pack-*.xml)|language_pack-*.xml|All Files (*.*)|*.*",
                Title = "Select source localization file",
                CheckFileExists = true,
                CheckPathExists = true
            };

            var result = dialog.ShowDialog();
            if (result == WinForms.DialogResult.OK && !string.IsNullOrEmpty(dialog.FileName))
            {
                SourceFilePath = dialog.FileName;
                AddLog($"Selected source file: {Path.GetFileName(SourceFilePath)}");
                StatusMessage = $"Loaded: {Path.GetFileName(SourceFilePath)}";
            }
        }
        catch (Exception ex)
        {
            AddLog($"ERROR: Failed to open file dialog - {ex.Message}");
            StatusMessage = $"Error opening file dialog";
        }
    }

    [RelayCommand]
    private async Task TranslateAllAsync()
    {
        if (SelectedLanguage == null || string.IsNullOrEmpty(SourceFilePath))
            return;

        IsTranslating = true;
        CanTranslate = false;
        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;

        try
        {
            AddLog($"Starting translation to {SelectedLanguage.DisplayName}...");
            StatusMessage = $"Translating to {SelectedLanguage.DisplayName}...";

            // Read source entries
            var sourceEntries = _resxService.ReadResxFile(SourceFilePath);
            if (sourceEntries.Count == 0)
            {
                AddLog("ERROR: No entries found in source file");
                StatusMessage = "Error: No entries in source file";
                return;
            }

            AddLog($"Found {sourceEntries.Count} entries to translate");
            ProgressMax = sourceEntries.Count;
            ProgressValue = 0;

            // Get target file path
            var targetFilePath = ResxService.GetTargetFilePath(SourceFilePath, SelectedLanguage.Code);
            AddLog($"Target file: {Path.GetFileName(targetFilePath)}");

            // Load existing translations to avoid re-translating
            var existingEntries = File.Exists(targetFilePath)
                ? _resxService.ReadResxFile(targetFilePath)
                    .ToDictionary(e => e.Key, e => e.Value)
                : [];

            var translatedEntries = new List<ResxEntry>();
            var skipped = 0;
            var translated = 0;
            var failed = 0;

            foreach (var entry in sourceEntries)
            {
                if (token.IsCancellationRequested)
                {
                    AddLog("Translation cancelled by user");
                    break;
                }

                ProgressValue++;
                ProgressText = $"Processing {ProgressValue}/{ProgressMax}: {entry.Key}";

                // Skip if already translated
                if (existingEntries.TryGetValue(entry.Key, out var existingValue)
                    && !string.IsNullOrEmpty(existingValue))
                {
                    translatedEntries.Add(new ResxEntry(entry.Key, existingValue, entry.Comment));
                    skipped++;
                    AddLog($"⊘ Skipped (existing): {entry.Key}");
                    continue;
                }

                // Translate
                var result = UseAzureTranslation
                    ? await _azureService.TranslateAsync(entry.Value, SelectedLanguage.Code, DetectLanguage ? null : "en", token)
                    : await _perplexityService.TranslateAsync(entry.Value, SelectedLanguage.Code, SelectedLanguage.DisplayName, token);

                if (result.IsSuccess)
                {
                    translatedEntries.Add(new ResxEntry(entry.Key, result.TranslatedText!, entry.Comment));
                    translated++;
                    var detectedInfo = !string.IsNullOrEmpty(result.DetectedLanguage) ? $" (detected: {result.DetectedLanguage})" : "";
                    AddLog($"✓ Translated: {entry.Key}{detectedInfo}");
                }
                else if (result.IsRateLimited)
                {
                    AddLog($"⏸ Rate limited, waiting 30 seconds...");
                    StatusMessage = "Rate limit reached, waiting...";

                    // Wait for rate limit to reset
                    await Task.Delay(30000, token);

                    // Retry
                    result = UseAzureTranslation
                        ? await _azureService.TranslateAsync(entry.Value, SelectedLanguage.Code, DetectLanguage ? null : "en", token)
                        : await _perplexityService.TranslateAsync(entry.Value, SelectedLanguage.Code, SelectedLanguage.DisplayName, token);

                    if (result.IsSuccess)
                    {
                        translatedEntries.Add(new ResxEntry(entry.Key, result.TranslatedText!, entry.Comment));
                        translated++;
                        AddLog($"✓ Translated (retry): {entry.Key}");
                    }
                    else
                    {
                        // Keep original on failure
                        translatedEntries.Add(entry);
                        failed++;
                        AddLog($"✗ Failed: {entry.Key} - {result.ErrorMessage}");
                    }
                }
                else
                {
                    // Keep original on failure
                    translatedEntries.Add(entry);
                    failed++;
                    AddLog($"✗ Failed: {entry.Key} - {result.ErrorMessage}");
                }
            }

            // Save translated file
            if (translatedEntries.Count > 0)
            {
                var saved = _resxService.WriteResxFile(targetFilePath, translatedEntries);
                if (saved)
                {
                    AddLog($"✓ Saved {translatedEntries.Count} entries to {Path.GetFileName(targetFilePath)}");
                }
                else
                {
                    AddLog($"✗ Failed to save file");
                }
            }

            var summary = $"Complete! Translated: {translated}, Skipped: {skipped}, Failed: {failed}";
            AddLog(summary);
            StatusMessage = summary;
            ProgressText = "Complete";
        }
        catch (OperationCanceledException)
        {
            AddLog("Translation cancelled");
            StatusMessage = "Translation cancelled";
        }
        catch (Exception ex)
        {
            AddLog($"ERROR: {ex.Message}");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsTranslating = false;
            UpdateCanTranslate();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand]
    private void StopTranslation()
    {
        _cancellationTokenSource?.Cancel();
        AddLog("Stopping translation...");
        StatusMessage = "Stopping...";
    }

    private void AddLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        TranslationLog.Add($"[{timestamp}] {message}");
        OnPropertyChanged(nameof(TranslationLog));
    }
}
