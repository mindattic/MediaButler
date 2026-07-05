using System.ComponentModel;
using MediaButler.Mcp;
using MediaButler.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MediaButler.Commands;

/// <summary>
/// <c>mediabutler mcp</c> — serve the Model Context Protocol over stdio
/// (newline-delimited JSON-RPC 2.0) so agent hosts (Claude Code, Claude
/// Desktop, ...) can drive MediaButler through the <c>scan</c> / <c>status</c>
/// / <c>run</c> tools. Register with e.g.
/// <c>claude mcp add mediabutler -- mediabutler mcp</c>.
///
/// <para>stdout carries protocol frames ONLY: on entry, Console.Out is
/// rebound to stderr (and Spectre's AnsiConsole pinned there) so pipeline
/// narration can never corrupt a frame; <see cref="McpServer"/> additionally
/// captures narration during <c>run</c> and returns it as the tool result.</para>
/// </summary>
[Description("Serve the Model Context Protocol over stdio (tools: scan, status, run).")]
public sealed class McpCommand : Command<McpCommand.Settings>
{
    public sealed class Settings : CommandSettings { }

    public override int Execute(CommandContext context, Settings settings)
    {
        var protocol = Console.Out;
        Console.SetOut(Console.Error);
        AnsiConsole.Console.Profile.Out = new AnsiConsoleOutput(Console.Error);

        var server = new McpServer(new SettingsService());
        string? line;
        while ((line = Console.In.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var response = server.HandleMessage(line);
            if (response is null) continue;
            protocol.WriteLine(response);
            protocol.Flush();
        }
        return 0;
    }
}
