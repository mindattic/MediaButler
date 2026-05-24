using MediaButler.Maui.Pages;
using MediaButler.Maui.Services;
using MediaButler.Settings;
using Microsoft.Extensions.Logging;

namespace MediaButler.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton<SettingsService>();
		builder.Services.AddSingleton<PipelineRunner>();
		builder.Services.AddTransient<MainPage>();
		builder.Services.AddTransient<SettingsPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
