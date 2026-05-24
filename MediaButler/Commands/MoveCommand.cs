using System.ComponentModel;
using MediaButler.Pipeline;
using MediaButler.Settings;

namespace MediaButler.Commands;

[Description("Stage 5 — move renamed media to TV/Movies destinations.")]
public sealed class MoveCommand : PipelineCommand
{
    protected override string Title => "Move to Plex (headless)";
    protected override int Run(PipelineRunner runner, MediaButlerSettings s) => runner.RunMove(s);
}
