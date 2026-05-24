using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MediaButler.Pipeline;
using MediaButler.Settings;
using MediaButler.Ui;
using Spectre.Console.Cli;

namespace MediaButler.Commands;

[SuppressMessage("Performance", "CA1812", Justification = "Instantiated by Spectre.Console.Cli")]
[Description("Classify everything under the source root and exit (read-only).")]
public sealed class ScanCommand : Command<ScanCommand.Settings>
{
    public sealed class Settings : BaseSettings { }

    public override int Execute(CommandContext context, Settings settings)
    {
        Status.Verbosity = settings.Verbosity;
        var runner = new PipelineRunner(new SettingsService());
        var s = runner.LoadEffective(settings.ApplyTo);
        Screen.Header("Scan (read-only)");
        return runner.RunScan(s);
    }
}
