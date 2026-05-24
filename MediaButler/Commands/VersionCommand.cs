using System.ComponentModel;
using System.Reflection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MediaButler.Commands;

[Description("Print the MediaButler version and exe path.")]
public sealed class VersionCommand : Command
{
    public override int Execute(CommandContext context)
    {
        var asm = typeof(VersionCommand).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? asm.GetName().Version?.ToString()
                   ?? "unknown";
        var location = Environment.ProcessPath ?? "(unknown path)";

        AnsiConsole.MarkupLine($"[cyan1]MediaButler[/] [grey50]v[/][yellow]{Markup.Escape(info)}[/]");
        AnsiConsole.MarkupLine($"  [grey50]exe:[/] {Markup.Escape(location)}");
        return 0;
    }
}
