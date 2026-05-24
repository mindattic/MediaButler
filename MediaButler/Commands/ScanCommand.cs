using System.ComponentModel;
using MediaButler.Pipeline;
using MediaButler.Settings;

namespace MediaButler.Commands;

[Description("Classify everything under the source root and exit (read-only).")]
public sealed class ScanCommand : PipelineCommand
{
    protected override string Title => "Scan (read-only)";
    protected override int Run(PipelineRunner runner, MediaButlerSettings s) => runner.RunScan(s);
}
