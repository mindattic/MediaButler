using System.ComponentModel;
using MediaButler.Pipeline;
using MediaButler.Settings;

namespace MediaButler.Commands;

[Description("Stage 1 only — local rename, hoist nested seasons, wrap loose movie files. Use `rename` for the full pipeline (rename + FileBot + move).")]
public sealed class HoistCommand : PipelineCommand
{
    protected override string Title => "Rename & Hoist (headless)";
    protected override int Run(PipelineRunner runner, MediaButlerSettings s) => runner.RunRename(s);
}
