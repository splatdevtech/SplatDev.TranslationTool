using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SplatDev.TranslationTool.Models;
using SplatDev.TranslationTool.Services;
using System.Collections.ObjectModel;
using System.IO;

namespace SplatDev.TranslationTool.ViewModels;

/// <summary>
/// View model for the translation editor window
/// </summary>
public partial class TranslationEditorViewModel : ObservableObject
{
    private readonly PerplexityTranslationService _perplexityService;
    private readonly AzureTranslationService _azureService;
    private readonly ResxService _resxService;

    [ObservableProperty]
    private string _sourceFilePath = string.Empty;

    [ObservableProperty]
    private string _targetFilePath = string.Empty;

    [ObservableProperty]
    private LanguageInfo? _targetLanguage;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _showOnlyModified;

    [ObservableProperty]
    private TranslationEntryViewModel? _selectedEntry;

    [ObservableProperty]
    private bool _useAzureTranslation = true;

    [ObservableProperty]
    private bool _detectLanguage = true;

    public ObservableCollection<TranslationEntryViewModel> Entries { get; } = [];
    public ObservableCollection<TranslationEntryViewModel> FilteredEntries { get; } = [];

    public TranslationEditorViewModel(
        PerplexityTranslationService perplexityService,
        AzureTranslationService azureService,
        ResxService resxService)
    {
        _perplexityService = perplexityService;
        _azureService = azureService;
        _resxService = resxService;
    }

    public void LoadTranslations(string sourceFilePath, string targetFilePath, LanguageInfo targetLanguage, bool useAzureTranslation = true, bool detectLanguage = true)
    {
        SourceFilePath = sourceFilePath;
        TargetFilePath = targetFilePath;
        TargetLanguage = targetLanguage;
        UseAzureTranslation = useAzureTranslation;
        DetectLanguage = detectLanguage;

        Entries.Clear();
        FilteredEntries.Clear();

        // Read source entries (handle duplicates by taking the first occurrence)
        var sourceEntries = _resxService.ReadResxFile(sourceFilePath)
            .GroupBy(e => e.Key)
            .ToDictionary(g => g.Key, g => g.First());

        // Read target entries (handle duplicates by taking the first occurrence)
        var targetEntries = File.Exists(targetFilePath)
            ? _resxService.ReadResxFile(targetFilePath)
                .GroupBy(e => e.Key)
                .ToDictionary(g => g.Key, g => g.First().Value)
            : new Dictionary<string, string>();

        // Create view models
        foreach (var source in sourceEntries.Values.OrderBy(e => e.Key))
        {
            var translatedValue = targetEntries.TryGetValue(source.Key, out var tv) ? tv : string.Empty;
            var entry = new TranslationEntryViewModel(source.Key, source.Value, translatedValue, source.Comment);
            Entries.Add(entry);
            FilteredEntries.Add(entry);
        }

        StatusMessage = $"Loaded {Entries.Count} entries";
    }

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnShowOnlyModifiedChanged(bool value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredEntries.Clear();

        foreach (var entry in Entries)
        {
            var matchesFilter = string.IsNullOrEmpty(FilterText) ||
                entry.Key.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                entry.OriginalValue.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                entry.TranslatedValue.Contains(FilterText, StringComparison.OrdinalIgnoreCase);

            var matchesModified = !ShowOnlyModified || entry.IsModified;

            if (matchesFilter && matchesModified)
            {
                FilteredEntries.Add(entry);
            }
        }

        StatusMessage = $"Showing {FilteredEntries.Count} of {Entries.Count} entries";
    }

    [RelayCommand]
    private async Task RetranslateEntryAsync(TranslationEntryViewModel? entry)
    {
        if (entry == null || TargetLanguage == null)
            return;

        entry.IsTranslating = true;
        StatusMessage = $"Retranslating: {entry.Key}...";

        try
        {
            var result = UseAzureTranslation
                ? await _azureService.TranslateAsync(entry.OriginalValue, TargetLanguage.Code, DetectLanguage ? null : "en")
                : await _perplexityService.TranslateAsync(entry.OriginalValue, TargetLanguage.Code, TargetLanguage.DisplayName);

            if (result.IsSuccess && !string.IsNullOrEmpty(result.TranslatedText))
            {
                entry.TranslatedValue = result.TranslatedText;
                StatusMessage = $"Retranslated: {entry.Key}";
            }
            else
            {
                StatusMessage = $"Failed to retranslate: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            entry.IsTranslating = false;
        }
    }

    [RelayCommand]
    private async Task RetranslateAllModifiedAsync()
    {
        if (TargetLanguage == null)
            return;

        var modifiedEntries = Entries.Where(e => e.IsModified || string.IsNullOrEmpty(e.TranslatedValue)).ToList();

        if (modifiedEntries.Count == 0)
        {
            StatusMessage = "No entries to retranslate";
            return;
        }

        var count = 0;
        var rateLimitDelay = UseAzureTranslation ? _azureService.RateLimitDelayMs : _perplexityService.RateLimitDelayMs;

        foreach (var entry in modifiedEntries)
        {
            count++;
            StatusMessage = $"Retranslating {count}/{modifiedEntries.Count}: {entry.Key}...";

            entry.IsTranslating = true;

            try
            {
                var result = UseAzureTranslation
                    ? await _azureService.TranslateAsync(entry.OriginalValue, TargetLanguage.Code, DetectLanguage ? null : "en")
                    : await _perplexityService.TranslateAsync(entry.OriginalValue, TargetLanguage.Code, TargetLanguage.DisplayName);

                if (result.IsSuccess && !string.IsNullOrEmpty(result.TranslatedText))
                {
                    entry.TranslatedValue = result.TranslatedText;
                }

                await Task.Delay(rateLimitDelay);
            }
            finally
            {
                entry.IsTranslating = false;
            }
        }

        StatusMessage = $"Retranslated {modifiedEntries.Count} entries";
    }

    [RelayCommand]
    private void SaveTranslations()
    {
        if (string.IsNullOrEmpty(TargetFilePath))
            return;

        var entries = Entries
            .Where(e => !string.IsNullOrEmpty(e.TranslatedValue))
            .Select(e => new ResxEntry(e.Key, e.TranslatedValue, e.Comment))
            .ToList();

        if (_resxService.WriteResxFile(TargetFilePath, entries))
        {
            // Reset modified flags
            foreach (var entry in Entries)
            {
                entry.IsModified = false;
            }

            StatusMessage = $"Saved {entries.Count} entries to {Path.GetFileName(TargetFilePath)}";
        }
        else
        {
            StatusMessage = "Error saving translations";
        }
    }

    [RelayCommand]
    private void CopyOriginalToTranslation(TranslationEntryViewModel? entry)
    {
        if (entry != null)
        {
            entry.TranslatedValue = entry.OriginalValue;
        }
    }

    [RelayCommand]
    private void ClearTranslation(TranslationEntryViewModel? entry)
    {
        if (entry != null)
        {
            entry.TranslatedValue = string.Empty;
        }
    }
}
