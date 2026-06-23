// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using nanoFramework.Tool.ExternalTools;
using Spectre.Console;
using Spectre.Console.Cli;

namespace nanoFramework.Tool.Commands;

public sealed class FlashSettings : CommandSettings
{
    [CommandOption("-t|--target <TARGET>")]
    [Description("The target board/firmware name to flash (passed to nanoff --target).")]
    public string? Target { get; init; }

    [CommandOption("-p|--port <PORT>")]
    [Description("The serial port to use (passed to nanoff --serialport).")]
    public string? Port { get; init; }

    [CommandOption("--update")]
    [Description("Update the device firmware rather than a clean flash (nanoff --update).")]
    public bool Update { get; init; }

    public override ValidationResult Validate() =>
        Target is null
            ? ValidationResult.Error("specify a --target (or pass nanoff args after --).")
            : ValidationResult.Success();
}

/// <summary>
/// <c>dotnet nano flash</c> — resolves and runs the external <c>nanoff</c> flasher
/// with mapped args. If nanoff cannot be resolved, fails with a clear Spectre error
/// (no hang, no stack dump).
/// </summary>
public sealed class FlashCommand : Command<FlashSettings>
{
    private readonly IExternalTool _nanoff;

    // Spectre's default activator needs a single, no-argument constructor. The real
    // nanoff wrapper is wired here; the resolution logic it relies on is covered by
    // ExternalToolResolverTests directly.
    public FlashCommand() => _nanoff = BuildNanoff();

    private static IExternalTool BuildNanoff()
    {
        var manifest = ToolManifest.LoadEmbedded();
        // No downloader is wired yet: when nothing resolves (bundled / PATH / cache),
        // resolution returns null and the command renders a clear "install nanoff"
        // error rather than attempting an unimplemented network fetch. Swap in a real
        // IToolDownloader here once pinned-release fetch lands (see ExternalToolResolver).
        var resolver = new ExternalToolResolver(new SystemToolEnvironment(), downloader: null);
        return new NanoffTool(manifest, resolver);
    }

    protected override int Execute(CommandContext context, FlashSettings settings, CancellationToken cancellationToken)
    {
        var resolution = _nanoff.ResolvePath();
        if (resolution is null)
        {
            AnsiConsole.MarkupLine($"[red]error:[/] could not find [bold]{Markup.Escape(_nanoff.Name)}[/]"
                + (string.IsNullOrEmpty(_nanoff.Version) ? "" : $" [grey]{Markup.Escape(_nanoff.Version)}[/]") + ".");
            AnsiConsole.MarkupLine("[grey]Looked for: a bundled binary (tools/" + Markup.Escape(_nanoff.Name)
                + "/), an installed tool / PATH, and the user cache.[/]");
            AnsiConsole.MarkupLine("[grey]Install it with[/] [blue]dotnet tool install -g nanoff[/] [grey]and retry.[/]");
            return 1;
        }

        var args = MapArgs(settings, context.Remaining.Raw);
        AnsiConsole.MarkupLine($"[grey]→ running[/] [blue]{Markup.Escape(_nanoff.Name)}[/] "
            + $"[grey]({resolution.Value.Source})[/] {Markup.Escape(string.Join(' ', args))}");

        try
        {
            return _nanoff.Run(args);
        }
        catch (ExternalToolNotFoundException ex)
        {
            AnsiConsole.MarkupLine($"[red]error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    // Maps the umbrella's friendly options onto nanoff's CLI surface. Any args after
    // a literal "--" are passed straight through to nanoff (remaining.Raw).
    private static List<string> MapArgs(FlashSettings s, IReadOnlyList<string> passthrough)
    {
        var args = new List<string>();
        if (s.Target is not null) { args.Add("--target"); args.Add(s.Target); }
        if (s.Port is not null) { args.Add("--serialport"); args.Add(s.Port); }
        if (s.Update) args.Add("--update");
        args.AddRange(passthrough);
        return args;
    }
}
