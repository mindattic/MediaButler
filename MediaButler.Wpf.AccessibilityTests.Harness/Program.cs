using MediaButler.Settings;
using MediaButler.Wpf.AccessibilityTests.Harness.Components;
using MediaButler.Wpf.UI.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Minimal Blazor Web App host used only by MediaButler.Wpf.AccessibilityTests'
// AxeScanTests, which launches this as a real subprocess (like
// MediaButler.Wpf.UiTests launches the real WPF .exe) and drives it with
// headless Chromium via Playwright, so axe-core scans the actual rendered
// MediaButler.Wpf.UI.App component tree — the same production markup the WPF
// shell hosts — for computed contrast/target-size checks a fake-DOM bUnit
// render cannot perform, and for interactive checks (tab switching) that need
// a live circuit. Runs out-of-process because under `dotnet test` the process
// entry assembly is the VSTest test host, not this one, which breaks
// ASP.NET Core's entry-assembly-keyed static web asset / component discovery.
// Test-only; never shipped.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // Static web assets (_content/{Rcl}/..., _framework/blazor.web.js) are
    // only wired up in Development — decided synchronously inside
    // CreateBuilder, so it must be passed in via WebApplicationOptions.
    // This harness is never published, so force Development regardless of
    // how it's launched.
    EnvironmentName = Environments.Development,
});
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<PipelineRunner>();
// ConfirmDialog.razor injects the concrete DialogService, not just
// IDialogService — see MediaButler.Wpf/App.xaml.cs for the same fix.
builder.Services.AddSingleton<DialogService>();
builder.Services.AddSingleton<IDialogService>(sp => sp.GetRequiredService<DialogService>());

var app = builder.Build();
app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<HarnessApp>().AddInteractiveServerRenderMode();
app.Run();
