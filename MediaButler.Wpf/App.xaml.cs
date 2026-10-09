using System.Windows;
using MediaButler.Diagnostics;
using MediaButler.Settings;
using MediaButler.Wpf.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MediaButler.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Previously unhandled exceptions here had no record at all — not even a console (this
        // is a GUI app). CrashLog is shared with the CLI front door; both land in the same
        // rolled MindAttic.Log files under %LOCALAPPDATA%\MindAttic\MediaButler.
        DispatcherUnhandledException += (_, args) =>
        {
            CrashLog.Fatal("WPF dispatcher unhandled exception", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) CrashLog.Fatal("AppDomain unhandled exception", ex);
        };

        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
        services.AddSingleton<SettingsService>();
        services.AddSingleton<PipelineRunner>();
        // ConfirmDialog.razor injects the concrete DialogService (it needs
        // .Current/.OnChange/.Complete, not just the IDialogService surface),
        // so both must resolve to the same instance.
        services.AddSingleton<DialogService>();
        services.AddSingleton<IDialogService>(sp => sp.GetRequiredService<DialogService>());

        var provider = services.BuildServiceProvider();

        var window = new MainWindow(provider);
        window.Show();
    }
}
