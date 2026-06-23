// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Spectre.Console;
using Spectre.Console.Cli;

namespace NanoFramework.Tool.Commands;

/// <summary>
/// A minimal placeholder for a built-in command that is on the design's command
/// surface (so it shows in <c>--help</c>) but is not implemented in the CLI yet.
/// Prints a clear "use VS / VS Code for now" notice and returns non-zero.
/// </summary>
public abstract class PlaceholderCommand : Command
{
    protected abstract string Verb { get; }

    protected override int Execute(CommandContext context, CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine($"[yellow]'{Markup.Escape(Verb)}' is not yet implemented in the CLI.[/]");
        AnsiConsole.MarkupLine("[grey]Use Visual Studio or VS Code for now.[/]");
        return 1;
    }
}

/// <summary><c>dotnet nano monitor</c> — placeholder (device output monitor).</summary>
public sealed class MonitorCommand : PlaceholderCommand
{
    protected override string Verb => "monitor";
}

/// <summary><c>dotnet nano devices</c> — placeholder (list connected nanoFramework devices).</summary>
public sealed class DevicesCommand : PlaceholderCommand
{
    protected override string Verb => "devices";
}
