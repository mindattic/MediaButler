using System.Windows;
using MediaButler.Settings;
using MediaButler.Wpf.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MediaButler.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
