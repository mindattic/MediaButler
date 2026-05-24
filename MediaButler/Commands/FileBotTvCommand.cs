using System.ComponentModel;
using MediaButler.Pipeline;
using MediaButler.Settings;

namespace MediaButler.Commands;

[Description("Stage 2 — FileBot TV rename + artwork.")]
public sealed class FileBotTvCommand : PipelineCommand
{
    protected override string Title => "FileBot TV (headless)";
    protected override int Run(PipelineRunner runner, MediaButlerSettings s) => runner.RunFileBotTv(s);
}
