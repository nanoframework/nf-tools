// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using NanoFramework.Migrate.Core.Projects;

namespace NanoFramework.Migrate.Core.Reporting;

/// <summary>
/// Renders a <see cref="MigrationReport"/> as Markdown. Pure: it returns a string
/// (or writes that string to a path) and never touches the console. The document is
/// a title, a summary line, a results table, and — when present — a "Manual review"
/// section and a "Verification" section.
/// </summary>
public static class MarkdownReportWriter
{
    /// <summary>Renders the report to a Markdown string.</summary>
    public static string Render(MigrationReport report)
    {
        var sb = new StringBuilder();

        sb.Append("# nanoFramework migration report");
        if (report.DryRun) sb.Append(" (dry run)");
        sb.Append('\n').Append('\n');

        sb.Append("- **Root:** `").Append(report.RootPath).Append("`\n");
        sb.Append("- **Generated (UTC):** ")
          .Append(report.GeneratedUtc.ToString("yyyy-MM-dd HH:mm:ss'Z'"))
          .Append('\n').Append('\n');

        var verb = report.DryRun ? "would convert" : "converted";
        sb.Append("**Summary:** ")
          .Append(verb).Append(' ').Append(report.Converted)
          .Append(" · skipped ").Append(report.Skipped)
          .Append(" · flagged ").Append(report.Flagged)
          .Append(" · errors ").Append(report.Errors)
          .Append(" · total ").Append(report.Total)
          .Append('\n').Append('\n');

        // Results table.
        sb.Append("| Project | Result | Packages | Notes |\n");
        sb.Append("| --- | --- | --- | --- |\n");
        foreach (var p in report.Projects)
        {
            var packages = p.Packages.Count == 0
                ? "—"
                : string.Join("<br>", p.Packages.Select(kv => $"{Cell(kv.Key)} {Cell(kv.Value)}"));
            sb.Append("| ")
              .Append(Cell(p.RelativePath)).Append(" | ")
              .Append(StatusLabel(p.Status)).Append(" | ")
              .Append(packages).Append(" | ")
              .Append(Notes(p)).Append(" |\n");
        }
        sb.Append('\n');

        if (report.AffectedSolutions.Count > 0)
        {
            sb.Append("## Solutions\n\n");
            foreach (var s in report.AffectedSolutions)
                sb.Append("- `").Append(s).Append("`\n");
            sb.Append('\n');
        }

        var flagged = report.Projects.Where(p => p.Review.Count > 0).ToList();
        if (flagged.Count > 0)
        {
            sb.Append("## Manual review\n\n");
            foreach (var p in flagged)
            {
                sb.Append("### ").Append(p.RelativePath).Append('\n').Append('\n');
                foreach (var item in p.Review)
                    sb.Append("- ").Append(item).Append('\n');
                sb.Append('\n');
            }
        }

        if (report.Verify.Count > 0)
        {
            sb.Append("## Verification\n\n");
            sb.Append("| Target | Result | Details |\n");
            sb.Append("| --- | --- | --- |\n");
            foreach (var v in report.Verify)
            {
                var label = v.Skipped ? "Skipped" : v.Succeeded ? "Pass" : "Fail";
                var details = string.IsNullOrEmpty(v.Details)
                    ? "—"
                    : Cell(v.Details);
                sb.Append("| ")
                  .Append(Cell(v.Target)).Append(" | ")
                  .Append(label).Append(" | ")
                  .Append(details).Append(" |\n");
            }
            sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>Renders the report and writes it to <paramref name="path"/> as UTF-8 (no BOM).</summary>
    public static void Write(MigrationReport report, string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, Render(report), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string StatusLabel(ConvertStatus s) => s switch
    {
        ConvertStatus.Converted => "Converted",
        ConvertStatus.Skipped   => "Skipped",
        ConvertStatus.Review    => "Review",
        ConvertStatus.Error     => "Error",
        _                       => "?",
    };

    private static string Notes(ReportEntry p) => p.Status switch
    {
        ConvertStatus.Error   => Cell(p.Error ?? "error"),
        ConvertStatus.Skipped => "already SDK-style",
        ConvertStatus.Review  => $"{p.Review.Count} item(s) need review",
        _                     => "clean",
    };

    // Escapes the few characters that would break a Markdown table cell: pipes
    // (column separators) and newlines (row separators -> <br>).
    private static string Cell(string s) =>
        s.Replace("\\", "\\\\")
         .Replace("|", "\\|")
         .Replace("\r\n", "<br>")
         .Replace("\n", "<br>")
         .Replace("\r", "<br>");
}
