using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SplatDev.TranslationTool.Services;
using SplatDev.TranslationTool.ViewModels;
using System.IO;
using System.Windows;

using Application = System.Windows.Application;

namespace SplatDev.TranslationTool;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public IServiceProvider ServiceProvider { get; private set; } = null!;
    public IConfiguration Configuration { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Build configuration
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

        Configuration = builder.Build();

        // Configure services
        var services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();

        // Show main window
        var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Configuration
        services.AddSingleton(Configuration);

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // HTTP client for Perplexity AI
        services.AddHttpClient();

        // Services
        services.AddSingleton<PerplexityTranslationService>();
        services.AddSingleton<AzureTranslationService>();
        services.AddSingleton<ResxService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddTransient<TranslationEditorViewModel>();

        // Windows
        services.AddTransient<MainWindow>();
        services.AddTransient<TranslationEditorWindow>();
    }
}
