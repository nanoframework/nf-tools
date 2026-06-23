// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using NanoFramework.Migrate.Cli.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;
using static NanoFramework.Migrate.Cli.Rendering.ConsoleSupport;

namespace NanoFramework.Migrate.Cli.Commands;

public sealed class RollbackSettings : AssumeYesSettings
{
    [CommandArgument(0, "[path]")]
    [Description("The directory whose last recorded migration is reverted (looks for a "
               + ".nanomigrate/ journal under it). Defaults to the current directory.")]
    public string Path { get; init; } = ".";
}

/// <summary>
/// Reverts the last recorded migration under a path by reading its rollback journal
/// (restore backed-up originals, delete created files). Idempotent and safe: with no
/// journal it reports "nothing to roll back" and exits 0. The reversal logic lives in
/// <see cref="RollbackJournal"/>; this is the thin presentation/prompt.
/// </summary>
public sealed class RollbackCommand : Command<RollbackSettings>
{
    protected override int Execute(CommandContext context, RollbackSettings settings, CancellationToken cancellationToken)
    {
        Header("NanoMigrate · rollback");

        var root = System.IO.Path.GetFullPath(settings.Path);
        var manifestPath = RollbackJournal.ManifestPaths(root).FirstOrDefault();
        if (manifestPath is null)
        {
            AnsiConsole.MarkupLine($"[grey]nothing to roll back under '{Esc(root)}' (no migration journal found).[/]");
            return 0;
        }

        var manifest = RollbackJournal.FindLatest(root);
        var count = manifest?.Entries.Count ?? 0;
        AnsiConsole.MarkupLine(
            $"Found a recorded migration ([blue]{Esc(manifest?.Id ?? "?")}[/]) with [bold]{count}[/] action(s).");

        if (!Confirm("Roll back the last recorded migration?", settings.AssumeYes))
        {
            AnsiConsole.MarkupLine("[grey]aborted; nothing reverted.[/]");
            return 0;
        }

        var result = RollbackJournal.ApplyAndCleanup(manifestPath);
        if (result is null)
        {
            AnsiConsole.MarkupLine("[red]error:[/] the rollback journal could not be read.");
            return 1;
        }

        MigrateRenderer.RenderRollbackResult(result, root);
        return result.Problems.Count > 0 ? 1 : 0;
    }

    private static bool Confirm(string question, bool assumeYes)
    {
        if (assumeYes || !IsInteractive()) return true;
        return AnsiConsole.Confirm(question, defaultValue: false);
    }
}
