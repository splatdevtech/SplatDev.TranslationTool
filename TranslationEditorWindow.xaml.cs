using SplatDev.TranslationTool.Models;
using SplatDev.TranslationTool.ViewModels;
using System.Windows;

namespace SplatDev.TranslationTool;

/// <summary>
/// Interaction logic for TranslationEditorWindow.xaml
/// </summary>
public partial class TranslationEditorWindow : Window
{
    private readonly TranslationEditorViewModel _viewModel;

    public TranslationEditorWindow(TranslationEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    public void LoadTranslations(string sourceFilePath, string targetFilePath, LanguageInfo targetLanguage, bool useAzureTranslation = true, bool detectLanguage = true)
    {
        _viewModel.LoadTranslations(sourceFilePath, targetFilePath, targetLanguage, useAzureTranslation, detectLanguage);
        Title = $"Translation Editor - {targetLanguage.DisplayName}";
    }
}
