using System.ComponentModel;
using MediaButler.Pipeline;
using MediaButler.Settings;

namespace MediaButler.Commands;

[Description("Stage 3 — FileBot movie rename + artwork.")]
public sealed class FileBotMoviesCommand : PipelineCommand
{
    protected override string Title => "FileBot Movies (headless)";
    protected override int Run(PipelineRunner runner, MediaButlerSettings s) => runner.RunFileBotMovies(s);
}
