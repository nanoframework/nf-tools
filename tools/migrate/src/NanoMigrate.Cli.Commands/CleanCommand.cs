// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using NanoFramework.Migrate.Cli.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;
using static NanoFramework.Migrate.Cli.Rendering.ConsoleSupport;

namespace NanoFramework.Migrate.Cli.Commands;

public sealed class CleanSettings : AssumeYesSettings
{
    [CommandArgument(0, "[path]")]
    [Description("A directory (or file) under which migration leftovers are removed: "
               + "all *.nfproj.bak files and any .nanomigrate/ rollback folders. Defaults to the current directory.")]
    public string Path { get; init; } = ".";
}

/// <summary>
/// Removes migration leftovers under a path: every <c>*.nfproj.bak</c> and every
/// <c>.nanomigrate</c> rollback folder/journal. Previews what will go and confirms
/// before deleting (skip with <c>--yes</c>; non-interactive proceeds). Pure file
/// logic lives in <see cref="BackupCleaner"/>; this is the thin presentation/prompt.
/// </summary>
public sealed class CleanCommand : Command<CleanSettings>
{
    protected override int Execute(CommandContext context, CleanSettings settings, CancellationToken cancellationToken)
    {
        Header("NanoMigrate · clean");

        var root = System.IO.Path.GetFullPath(settings.Path);
        var plan = BackupCleaner.Plan(root);

        if (plan.IsEmpty)
        {
            AnsiConsole.MarkupLine($"[grey]nothing to clean under '{Esc(root)}'.[/]");
            return 0;
        }

        // Show exactly what will be deleted before any prompt.
        var tree = new Tree($"[bold]Leftovers under[/] [blue]{Esc(root)}[/]");
        if (plan.BackupFiles.Count > 0)
        {
            var node = tree.AddNode($"[yellow]{plan.BackupFiles.Count}[/] backup file(s)");
            foreach (var f in plan.BackupFiles)
                node.AddNode($"[grey]{Esc(System.IO.Path.GetRelativePath(root, f))}[/]");
        }
        if (plan.RollbackFolders.Count > 0)
        {
            var node = tree.AddNode($"[yellow]{plan.RollbackFolders.Count}[/] rollback folder(s)");
            foreach (var d in plan.RollbackFolders)
                node.AddNode($"[grey]{Esc(System.IO.Path.GetRelativePath(root, d))}[/]");
        }
        AnsiConsole.Write(tree);
        AnsiConsole.WriteLine();

        if (!Confirm($"Delete {plan.Total} leftover item(s)?", settings.AssumeYes))
        {
            AnsiConsole.MarkupLine("[grey]aborted; nothing deleted.[/]");
            return 0;
        }

        var result = BackupCleaner.Remove(plan);
        foreach (var p in result.Problems)
            AnsiConsole.MarkupLine($"[red]clean issue:[/] {Esc(p)}");

        AnsiConsole.MarkupLine(
            $"[green]removed {result.Total} item(s)[/] "
          + $"([yellow]{result.RemovedBackups.Count}[/] backup file(s), "
          + $"[yellow]{result.RemovedFolders.Count}[/] rollback folder(s)).");
        return result.Problems.Count > 0 ? 1 : 0;
    }

    private static bool Confirm(string question, bool assumeYes)
    {
        if (assumeYes || !IsInteractive()) return true;
        return AnsiConsole.Confirm(question, defaultValue: false);
    }
}
