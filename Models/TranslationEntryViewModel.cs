using CommunityToolkit.Mvvm.ComponentModel;

namespace SplatDev.TranslationTool.Models;

/// <summary>
/// View model for a single translation entry in the editor
/// </summary>
public partial class TranslationEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private string _key = string.Empty;

    [ObservableProperty]
    private string _originalValue = string.Empty;

    [ObservableProperty]
    private string _translatedValue = string.Empty;

    [ObservableProperty]
    private bool _isModified;

    [ObservableProperty]
    private bool _isTranslating;

    [ObservableProperty]
    private string? _comment;

    partial void OnTranslatedValueChanged(string value)
    {
        IsModified = true;
    }

    public TranslationEntryViewModel() { }

    public TranslationEntryViewModel(string key, string originalValue, string translatedValue, string? comment = null)
    {
        _key = key;
        _originalValue = originalValue;
        _translatedValue = translatedValue;
        _comment = comment;
        _isModified = false;
    }
}
