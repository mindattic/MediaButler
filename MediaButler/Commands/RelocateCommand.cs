using System.ComponentModel;
using MediaButler.Pipeline;
using MediaButler.Settings;

namespace MediaButler.Commands;

[Description("Library cleanup — move misplaced items out of the scanned folder.")]
public sealed class RelocateCommand : PipelineCommand
{
    protected override string Title => "Relocate (headless)";
    protected override int Run(PipelineRunner runner, MediaButlerSettings s) => runner.RunRelocate(s);
}
