// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using Spectre.Console;
using static nanoFramework.Migrate.Cli.Rendering.ConsoleSupport;

namespace nanoFramework.Migrate.Cli.Rendering;

/// <summary>Pairs a source .nfproj path with its conversion outcome.</summary>
public readonly record struct ProjectOutcome(string Nfproj, ConvertResult Result);

/// <summary>Spectre presentation for the migrate command. Consumes Core's data.</summary>
public static class MigrateRenderer
{
    public static void RenderSummaryTable(List<ProjectOutcome> results, string baseDir, bool dryRun)
    {
        var table = new Table().Border(TableBorder.Rounded).Expand();
        table.Title = new TableTitle(dryRun ? "Migration preview (dry run)" : "Migration summary");
        table.AddColumn("Project");
        table.AddColumn("Result");
        table.AddColumn("Packages");
        table.AddColumn("Notes");

        foreach (var (nf, result) in results)
        {
            var rel = Path.GetRelativePath(baseDir, nf);
            var (label, color) = StatusLabel(result.Status);

            var pkgs = result.Packages.Count == 0
                ? "[grey]—[/]"
                : string.Join("\n", result.Packages.Select(p => $"{Esc(p.Key)} [grey]{Esc(p.Value)}[/]"));

            var notes = BuildNotesCell(result, dryRun);

            table.AddRow(
                new Markup($"[blue]{Esc(rel)}[/]"),
                new Markup($"[{color}]{label}[/]"),
                new Markup(pkgs),
                new Markup(notes));
        }

        AnsiConsole.Write(table);
    }

    // The Notes cell: in dry-run it previews what WOULD change (target path,
    // deletions, .sln edits); otherwise it shows the count of review flags.
    private static string BuildNotesCell(ConvertResult result, bool dryRun)
    {
        if (result.Status == ConvertStatus.Error)
            return $"[red]{Esc(result.Error ?? "error")}[/]";
        if (result.Status == ConvertStatus.Skipped)
            return "[grey]already SDK-style[/]";

        var lines = new List<string>();
        if (dryRun)
        {
            lines.Add($"[grey]→[/] {Esc(Path.GetFileName(result.OutputPath))}");
            foreach (var d in result.DeletedFiles)
                lines.Add($"[red]delete[/] {Esc(Path.GetFileName(d))}");
            foreach (var s in result.UpdatedSolutions)
                lines.Add($"[yellow]edit[/] {Esc(Path.GetFileName(s))}");
        }
        if (result.Review.Count > 0)
            lines.Add($"[yellow]{result.Review.Count} item(s) need review[/]");
        return lines.Count == 0 ? "[green]clean[/]" : string.Join("\n", lines);
    }

    // Review notes get a clearly-visible yellow panel, grouped per project, so
    // they are never buried in the table.
    public static void RenderReviewNotes(List<ProjectOutcome> results, string baseDir)
    {
        var flagged = results.Where(r => r.Result.Review.Count > 0
                                      && r.Result.Status == ConvertStatus.Review).ToList();
        if (flagged.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var (nf, result) in flagged)
        {
            var rel = Path.GetRelativePath(baseDir, nf);
            sb.Append($"[bold]{Esc(rel)}[/]\n");
            foreach (var item in result.Review)
                sb.Append($"  [yellow]•[/] {Esc(item)}\n");
        }

        var panel = new Panel(sb.ToString().TrimEnd('\n'))
        {
            Header = new PanelHeader("[yellow]MANUAL REVIEW NEEDED[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Yellow),
            Expand = true,
        };
        AnsiConsole.WriteLine();
        AnsiConsole.Write(panel);
    }

    public static void RenderTally(List<ProjectOutcome> results, bool dryRun)
    {
        int converted = results.Count(r => r.Result.Status == ConvertStatus.Converted);
        int skipped   = results.Count(r => r.Result.Status == ConvertStatus.Skipped);
        int flagged   = results.Count(r => r.Result.Status == ConvertStatus.Review);
        int errors    = results.Count(r => r.Result.Status == ConvertStatus.Error);
        var verb = dryRun ? "would convert" : "converted";

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"[green]{verb} {converted}[/]  •  [grey]skipped {skipped}[/]  •  "
          + $"[yellow]flagged {flagged}[/]  •  [red]errors {errors}[/]  •  total {results.Count}");
    }

    // A tree of the candidate solutions and the .nfproj each would convert, so the
    // user sees what is in scope before any prompt. Used for solution-input,
    // directory-with-solutions, and glob-scoped plans.
    public static void RenderCandidateSolutions(IReadOnlyList<SolutionCandidate> candidates, string baseDir)
    {
        if (candidates.Count == 0) return;

        var tree = new Tree("[bold]Affected solution(s)[/]");
        foreach (var c in candidates)
        {
            var fmt = c.Solution.Format == SolutionFormat.Xml ? "slnx" : "sln";
            var slnRel = RelOrName(baseDir, c.Solution.Path);
            var node = tree.AddNode($"[yellow]{Esc(slnRel)}[/] [grey]({fmt}, {c.NanoProjects.Count} project(s))[/]");
            foreach (var nf in c.NanoProjects)
                node.AddNode($"[blue]{Esc(RelOrName(baseDir, nf))}[/]");
        }
        AnsiConsole.Write(tree);
        AnsiConsole.WriteLine();
    }

    // The solutions actually retargeted by a real run (grouped notice). Pure
    // presentation over the rewrite results.
    public static void RenderRewrittenSolutions(IReadOnlyList<string> rewritten, string baseDir)
    {
        if (rewritten.Count == 0) return;
        AnsiConsole.WriteLine();
        foreach (var sln in rewritten)
            AnsiConsole.MarkupLine($"[yellow]updated[/] {Esc(RelOrName(baseDir, sln))}");
    }

    // A path relative to baseDir when it sits underneath it; otherwise the bare
    // file name. Keeps output readable whether or not the file is inside the tree.
    private static string RelOrName(string baseDir, string path)
    {
        var rel = Path.GetRelativePath(baseDir, path);
        return rel.StartsWith("..", StringComparison.Ordinal) ? Path.GetFileName(path) : rel;
    }

    // The verification build results: one row per target with a Pass/Fail/Skipped
    // badge and (on failure) a short error tail. Pure presentation over Core data.
    public static void RenderVerifyTable(IReadOnlyList<BuildOutcome> outcomes, string baseDir)
    {
        if (outcomes.Count == 0) return;

        var table = new Table().Border(TableBorder.Rounded).Expand();
        table.Title = new TableTitle("Build verification");
        table.AddColumn("Target");
        table.AddColumn("Result");
        table.AddColumn("Details");

        foreach (var o in outcomes)
        {
            var (label, color) = o.Skipped ? ("Skipped", "grey")
                : o.Succeeded ? ("Pass", "green")
                : ("Fail", "red");
            var details = o.Skipped ? Esc(o.Message ?? "skipped")
                : o.Succeeded ? "[grey]—[/]"
                : $"[red]exit {o.ExitCode}[/]\n{Esc(Truncate(o.ErrorTail, 600))}";
            table.AddRow(
                new Markup($"[blue]{Esc(RelOrName(baseDir, o.Target))}[/]"),
                new Markup($"[{color}]{label}[/]"),
                new Markup(details));
        }
        AnsiConsole.WriteLine();
        AnsiConsole.Write(table);
    }

    // The result of a rollback: what was restored/deleted (grouped notice).
    public static void RenderRollbackResult(RollbackResult result, string baseDir)
    {
        AnsiConsole.WriteLine();
        foreach (var r in result.Restored)
            AnsiConsole.MarkupLine($"[green]restored[/] {Esc(RelOrName(baseDir, r))}");
        foreach (var d in result.Deleted)
            AnsiConsole.MarkupLine($"[yellow]removed[/] {Esc(RelOrName(baseDir, d))}");
        foreach (var p in result.Problems)
            AnsiConsole.MarkupLine($"[red]rollback issue:[/] {Esc(p)}");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]rolled back {result.Total} file(s)[/] "
            + $"([green]{result.Restored.Count} restored[/], [yellow]{result.Deleted.Count} removed[/]).");
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    private static (string label, string color) StatusLabel(ConvertStatus s) => s switch
    {
        ConvertStatus.Converted => ("Converted", "green"),
        ConvertStatus.Skipped   => ("Skipped", "grey"),
        ConvertStatus.Review    => ("Review", "yellow"),
        ConvertStatus.Error     => ("Error", "red"),
        _                       => ("?", "white"),
    };
}
