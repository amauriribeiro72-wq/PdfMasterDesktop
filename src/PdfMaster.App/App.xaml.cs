using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PdfMaster.Core.Interfaces;
using PdfMaster.Infrastructure.Services;
using PdfMaster.App.ViewModels;
using PdfMaster.App.Views;

namespace PdfMaster.App;

public partial class App : Application
{
    private static IServiceProvider? _serviceProvider;
    public static IServiceProvider Services => _serviceProvider ?? throw new InvalidOperationException("ServiceProvider not initialized");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);

        _serviceProvider = serviceCollection.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Serviços de Infraestrutura
        services.AddSingleton<IPdfService, PdfService>();
        services.AddSingleton<IOcrService, WindowsOcrService>();
        services.AddSingleton<IDigitalSignatureService, WindowsSignatureService>();

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<BatchProcessingViewModel>();

        // Views
        services.AddTransient<MainWindow>();
        services.AddTransient<BatchProcessingView>();
    }
}
