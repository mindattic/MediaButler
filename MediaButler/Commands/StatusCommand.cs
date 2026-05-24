using System.ComponentModel;
using MediaButler.Pipeline;
using MediaButler.Settings;

namespace MediaButler.Commands;

[Description("Print settings, paths, and a scan summary.")]
public sealed class StatusCommand : PipelineCommand
{
    protected override string Title => "Status";
    protected override int Run(PipelineRunner runner, MediaButlerSettings s) => runner.ShowStatus(s);
}
