using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.DependencyInjection;

using SplatDev.TranslationTool.Models;
using SplatDev.TranslationTool.Services;

using System.Collections.ObjectModel;
using System.IO;
using System.Text;

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
    private readonly List<ResxEntry> _failedEntries = [];
    private string _failedTranslationsLogPath = string.Empty;
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

    [ObservableProperty]
    private bool _canRetryFailed;

    [ObservableProperty]
    private int _failedTranslationsCount;

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
        ClearFailedEntries();
        UpdateAvailableLanguages();
        UpdateCanTranslate();
    }

    partial void OnSelectedLanguageChanged(LanguageInfo? value)
    {
        ClearFailedEntries();
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

        var hasValidTranslationContext = !string.IsNullOrEmpty(SourceFilePath)
            && File.Exists(SourceFilePath)
            && SelectedLanguage != null
            && hasValidCredentials;

        CanTranslate = hasValidTranslationContext && !IsTranslating;

        CanOpenEditor = hasValidTranslationContext;

        CanRetryFailed = hasValidTranslationContext
            && !IsTranslating
            && _failedEntries.Count > 0;
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

        ClearFailedEntries();

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
            _failedTranslationsLogPath = Path.ChangeExtension(targetFilePath, ".failed.log");
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
                        TrackFailedEntry(entry, result.ErrorMessage);
                        AddLog($"✗ Failed: {entry.Key} - {result.ErrorMessage}");
                    }
                }
                else
                {
                    // Keep original on failure
                    translatedEntries.Add(entry);
                    failed++;
                    TrackFailedEntry(entry, result.ErrorMessage);
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
    private async Task RetryFailedAsync()
    {
        if (SelectedLanguage == null || string.IsNullOrEmpty(SourceFilePath) || _failedEntries.Count == 0)
        {
            return;
        }

        IsTranslating = true;
        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;

        try
        {
            var targetFilePath = ResxService.GetTargetFilePath(SourceFilePath, SelectedLanguage.Code);
            _failedTranslationsLogPath = Path.ChangeExtension(targetFilePath, ".failed.log");

            var pendingRetries = _failedEntries.ToList();
            var retrySucceeded = 0;
            var retryFailed = 0;
            var retryUpdates = new List<ResxEntry>();

            ProgressMax = pendingRetries.Count;
            ProgressValue = 0;
            AddLog($"Retrying {pendingRetries.Count} failed entries...");

            foreach (var entry in pendingRetries)
            {
                token.ThrowIfCancellationRequested();

                ProgressValue++;
                ProgressText = $"Retrying {ProgressValue}/{ProgressMax}: {entry.Key}";

                var result = UseAzureTranslation
                    ? await _azureService.TranslateAsync(entry.Value, SelectedLanguage.Code, DetectLanguage ? null : "en", token)
                    : await _perplexityService.TranslateAsync(entry.Value, SelectedLanguage.Code, SelectedLanguage.DisplayName, token);

                if (result.IsSuccess)
                {
                    retrySucceeded++;
                    retryUpdates.Add(new ResxEntry(entry.Key, result.TranslatedText!, entry.Comment));
                    _failedEntries.RemoveAll(failedEntry => string.Equals(failedEntry.Key, entry.Key, StringComparison.Ordinal));
                    AddLog($"✓ Retry succeeded: {entry.Key}");
                    continue;
                }

                retryFailed++;
                TrackFailedEntry(entry, result.ErrorMessage);
                AddLog($"✗ Retry failed: {entry.Key} - {result.ErrorMessage}");
            }

            if (retryUpdates.Count > 0)
            {
                var saved = _resxService.WriteResxFile(targetFilePath, retryUpdates);
                if (saved)
                {
                    AddLog($"✓ Saved {retryUpdates.Count} retried translations to {Path.GetFileName(targetFilePath)}");
                }
                else
                {
                    AddLog("✗ Failed to save retried translations");
                }
            }

            FailedTranslationsCount = _failedEntries.Count;
            UpdateCanTranslate();

            var summary = $"Retry complete. Succeeded: {retrySucceeded}, Still failed: {retryFailed}";
            AddLog(summary);
            StatusMessage = summary;
            ProgressText = "Retry complete";
        }
        catch (OperationCanceledException)
        {
            AddLog("Retry cancelled");
            StatusMessage = "Retry cancelled";
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

    private void ClearFailedEntries()
    {
        _failedEntries.Clear();
        FailedTranslationsCount = 0;
    }

    private void TrackFailedEntry(ResxEntry entry, string? errorMessage)
    {
        _failedEntries.RemoveAll(existing => string.Equals(existing.Key, entry.Key, StringComparison.Ordinal));
        _failedEntries.Add(entry);
        FailedTranslationsCount = _failedEntries.Count;

        WriteFailedTranslationLogEntry(entry, errorMessage);
        UpdateCanTranslate();
    }

    private void WriteFailedTranslationLogEntry(ResxEntry entry, string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(_failedTranslationsLogPath))
        {
            return;
        }

        try
        {
            var builder = new StringBuilder();
            builder.Append(DateTime.UtcNow.ToString("O"));
            builder.Append(" | key=").Append(entry.Key);
            builder.Append(" | language=").Append(SelectedLanguage?.Code);
            builder.Append(" | service=").Append(UseAzureTranslation ? "Azure" : "Perplexity");
            builder.Append(" | error=").Append(errorMessage ?? "Unknown error");
            builder.Append(" | source=").Append(entry.Value.Replace(Environment.NewLine, " "));

            File.AppendAllText(_failedTranslationsLogPath, builder.AppendLine().ToString());
        }
        catch (IOException)
        {
            AddLog("WARN: Unable to write failed translation log file.");
        }
        catch (UnauthorizedAccessException)
        {
            AddLog("WARN: No permission to write failed translation log file.");
        }
    }

    private void AddLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        TranslationLog.Add($"[{timestamp}] {message}");
        OnPropertyChanged(nameof(TranslationLog));
    }
}
