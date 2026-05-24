using System.ComponentModel;
using MediaButler.Pipeline;
using MediaButler.Settings;

namespace MediaButler.Commands;

[Description("Stage 4 — fetch subtitles via OpenSubtitles.")]
public sealed class FileBotSubtitlesCommand : PipelineCommand
{
    protected override string Title => "FileBot Subtitles (headless)";
    protected override int Run(PipelineRunner runner, MediaButlerSettings s) => runner.RunFileBotSubtitles(s);
}
