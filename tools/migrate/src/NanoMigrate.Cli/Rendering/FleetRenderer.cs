// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using Spectre.Console;
using static NanoFramework.Migrate.Cli.Rendering.ConsoleSupport;

namespace NanoFramework.Migrate.Cli.Rendering;

/// <summary>Spectre presentation for the fleet command. Consumes Core's RepoReport data.</summary>
internal static class FleetRenderer
{
    public static void RenderFleetTable(List<RepoReport> report)
    {
        var table = new Table().Border(TableBorder.Rounded).Expand();
        table.Title = new TableTitle("Fleet results");
        table.AddColumn("Repository");
        table.AddColumn("Result");
        table.AddColumn("Projects");
        table.AddColumn("Review");

        foreach (var rr in report)
        {
            string label, color;
            if (rr.Error is not null) { label = "Error"; color = "red"; }
            else if (rr.Review.Count > 0) { label = "Review"; color = "yellow"; }
            else { label = rr.Committed ? "OK (committed)" : "OK"; color = "green"; }

            var note = rr.Error is not null ? $" [red]{Esc(rr.Error.Split('\n')[0])}[/]" : "";
            table.AddRow(
                new Markup($"[blue]{Esc(rr.Name)}[/]"),
                new Markup($"[{color}]{label}[/]{note}"),
                new Markup(rr.Projects.ToString()),
                new Markup(rr.Review.Count == 0 ? "[grey]—[/]" : $"[yellow]{rr.Review.Count}[/]"));
        }
        AnsiConsole.Write(table);
    }

    // A tree view of the fleet: one branch per repo, with each repo's review
    // items as leaves. Complements the summary table for at-a-glance structure.
    public static void RenderFleetTree(List<RepoReport> report)
    {
        if (report.Count == 0) return;

        var tree = new Tree("[bold]Fleet[/]");
        foreach (var rr in report)
        {
            string label, color;
            if (rr.Error is not null) { label = "error"; color = "red"; }
            else if (rr.Review.Count > 0) { label = "review"; color = "yellow"; }
            else { label = rr.Committed ? "ok (committed)" : "ok"; color = "green"; }

            var node = tree.AddNode($"[blue]{Esc(rr.Name)}[/]  [{color}]{label}[/]  [grey]({rr.Projects} project(s))[/]");
            if (rr.Error is not null)
                node.AddNode($"[red]{Esc(rr.Error.Split('\n')[0])}[/]");
            foreach (var item in rr.Review)
                node.AddNode($"[yellow]•[/] {Esc(item)}");
        }
        AnsiConsole.WriteLine();
        AnsiConsole.Write(tree);
    }

    public static void RenderFleetReview(List<RepoReport> report)
    {
        var flagged = report.Where(r => r.Error is null && r.Review.Count > 0).ToList();
        if (flagged.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var rr in flagged)
        {
            sb.Append($"[bold]{Esc(rr.Name)}[/]\n");
            foreach (var item in rr.Review) sb.Append($"  [yellow]•[/] {Esc(item)}\n");
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
}
