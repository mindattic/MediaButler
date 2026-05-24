using System.ComponentModel;
using MediaButler.Pipeline;
using MediaButler.Settings;

namespace MediaButler.Commands;

[Description("Run the full pipeline non-interactively (rename + FileBot + move).")]
public sealed class RunCommand : PipelineCommand
{
    protected override string Title => "Run Full Pipeline (headless)";
    protected override int Run(PipelineRunner runner, MediaButlerSettings s) => runner.RunFull(s);
}
