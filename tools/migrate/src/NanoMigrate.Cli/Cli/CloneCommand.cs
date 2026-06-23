// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using static nanoFramework.Migrate.Cli.Rendering.ConsoleSupport;

namespace nanoFramework.Migrate.Cli.Commands;

internal sealed class CloneSettings : CommandSettings
{
    [CommandArgument(0, "[out-dir]")]
    [Description("Directory to clone into (default ./nano-repos).")]
    public string? OutDir { get; init; }

    [CommandOption("--org <name>")]
    [Description("GitHub org (default nanoframework).")]
    public string Org { get; init; } = "nanoframework";

    [CommandOption("--filter <prefix>")]
    [Description("Repo name prefix to match (default lib-).")]
    public string Filter { get; init; } = "lib-";

    [CommandOption("--token <pat>")]
    [Description("GitHub token (or env GITHUB_TOKEN) to raise the API rate limit.")]
    public string? Token { get; init; } = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

    [CommandOption("--include-archived")]
    [Description("Include archived repositories (skipped by default).")]
    public bool IncludeArchived { get; init; }
}

internal sealed class CloneCommand : AsyncCommand<CloneSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, CloneSettings settings, CancellationToken cancellationToken)
    {
        Header("NanoMigrate · clone");
        // Resolve to an absolute path so the destination is stable regardless of the
        // working directory git inherits for each clone.
        var outDir = Path.GetFullPath(settings.OutDir ?? "./nano-repos");
        Directory.CreateDirectory(outDir);

        AnsiConsole.MarkupLine($"Enumerating [bold]{Esc(settings.Org)}[/] repositories matching '[blue]{Esc(settings.Filter)}*[/]'…");
        var repos = (await GitHub.ListOrgReposAsync(settings.Org, settings.Token, settings.IncludeArchived, cancellationToken))
                          .Where(r => r.Name.StartsWith(settings.Filter, StringComparison.OrdinalIgnoreCase))
                          .OrderBy(r => r.Name).ToList();

        if (repos.Count == 0) throw new UserError(
            $"no repos matched '{settings.Filter}*' in org '{settings.Org}'. " +
            "Check the org name and filter, or pass --token to lift the API rate limit.");

        AnsiConsole.MarkupLine($"Found [bold]{repos.Count}[/] repositories. Cloning into [blue]{Esc(outDir)}[/]…");
        AnsiConsole.WriteLine();

        var table = new Table().Border(TableBorder.Rounded).Expand();
        table.Title = new TableTitle("Clone results");
        table.AddColumn("Repository");
        table.AddColumn("Result");

        int ok = 0, skipped = 0, failed = 0;
        void CloneAll(Action<string>? report)
        {
            foreach (var r in repos)
            {
                report?.Invoke(r.Name);
                var dest = Path.Combine(outDir, r.Name);
                if (Directory.Exists(dest))
                {
                    table.AddRow(new Markup($"[blue]{Esc(r.Name)}[/]"), new Markup("[grey]skipped (already present)[/]"));
                    skipped++; continue;
                }
                var (code, _, err) = ProcessRunner.Run("git", $"clone --depth 1 {r.CloneUrl} \"{dest}\"", outDir);
                if (code == 0)
                {
                    table.AddRow(new Markup($"[blue]{Esc(r.Name)}[/]"), new Markup("[green]cloned[/]"));
                    ok++;
                }
                else
                {
                    var msg = err.Trim().Split('\n').LastOrDefault() ?? "git clone failed";
                    table.AddRow(new Markup($"[blue]{Esc(r.Name)}[/]"), new Markup($"[red]FAIL[/] {Esc(msg)}"));
                    failed++;
                }
            }
        }

        if (IsInteractive())
            AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .Start("Cloning…", ctx => CloneAll(name => ctx.Status($"Cloning [blue]{Esc(name)}[/]…")));
        else
            CloneAll(null);

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]cloned {ok}[/]  •  [grey]skipped {skipped}[/]  •  [red]failed {failed}[/]");
        return failed > 0 ? 2 : 0;
    }
}
