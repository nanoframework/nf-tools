// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Spectre.Console;

namespace nanoFramework.Migrate.Cli.Rendering;

/// <summary>Shared, low-level console helpers. The only consumers of AnsiConsole.</summary>
public static class ConsoleSupport
{
    // A "title rule" header. Spectre renders this as a centred rule when the
    // terminal is wide enough, and degrades to plain text when redirected.
    public static void Header(string title)
    {
        var rule = new Rule($"[bold]{Esc(title)}[/]") { Justification = Justify.Left };
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();
    }

    // Escapes interpolated text (notably file paths) so Spectre markup like
    // "[" in a path can't be interpreted as styling — guards against injection.
    public static string Esc(string s) => Markup.Escape(s);

    // Interactive == an attached terminal whose stdin is not redirected. We use
    // this to decide whether to show a spinner and whether to prompt. In
    // non-interactive contexts (CI, piped input) we never prompt or block.
    public static bool IsInteractive() =>
        !Console.IsInputRedirected && AnsiConsole.Profile.Capabilities.Interactive;
}
