using System.ComponentModel;
using MediaButler.Pipeline;
using MediaButler.Settings;

namespace MediaButler.Commands;

[Description("Stage 1 only — local rename + hoist + empty cleanup.")]
public sealed class RenameCommand : PipelineCommand
{
    protected override string Title => "Rename & Hoist (headless)";
    protected override int Run(PipelineRunner runner, MediaButlerSettings s) => runner.RunRename(s);
}
