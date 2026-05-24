using MediaButler.Pipeline;
using MediaButler.Settings;
using MediaButler.Ui;
using Spectre.Console.Cli;

namespace MediaButler.Commands;

/// <summary>
/// Shared base for every headless subcommand. Each leaf command declares only
/// its breadcrumb title and the runner method to dispatch to — the framework
/// boilerplate (verbosity wiring, settings load + CLI overlay, header) lives
/// here once instead of being duplicated across ten near-identical classes.
/// </summary>
public abstract class PipelineCommand : Command<BaseSettings>
{
    public override int Execute(CommandContext context, BaseSettings settings)
    {
        Status.Verbosity = settings.Verbosity;
        var runner = new PipelineRunner(new SettingsService());
        var s = runner.LoadEffective(settings.ApplyTo);
        Screen.Header(Title);
        return Run(runner, s);
    }

    protected abstract string Title { get; }
    protected abstract int Run(PipelineRunner runner, MediaButlerSettings settings);
}
