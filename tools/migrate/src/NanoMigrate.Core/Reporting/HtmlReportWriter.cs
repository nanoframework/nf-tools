// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Text;
using nanoFramework.Migrate.Core.Projects;

namespace nanoFramework.Migrate.Core.Reporting;

/// <summary>
/// Renders a <see cref="MigrationReport"/> as a self-contained HTML page: inline
/// <c>&lt;style&gt;</c>, no external assets. The results table is colour-coded by
/// status (green converted / grey skipped / amber review / red error). Pure and
/// console-free; ALL interpolated text (project names, notes, package ids) is
/// HTML-escaped to guard against injection.
/// </summary>
public static class HtmlReportWriter
{
    /// <summary>Renders the report to a self-contained HTML string.</summary>
    public static string Render(MigrationReport report)
    {
        var sb = new StringBuilder();
        var title = report.DryRun
            ? "nanoFramework migration report (dry run)"
            : "nanoFramework migration report";

        sb.Append("<!DOCTYPE html>\n");
        sb.Append("<html lang=\"en\">\n<head>\n");
        sb.Append("<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<title>").Append(Esc(title)).Append("</title>\n");
        sb.Append("<style>\n").Append(Css).Append("</style>\n");
        sb.Append("</head>\n<body>\n");

        sb.Append("<h1>").Append(Esc(title)).Append("</h1>\n");
        sb.Append("<p class=\"meta\">Root: <code>").Append(Esc(report.RootPath)).Append("</code><br>\n");
        sb.Append("Generated (UTC): ")
          .Append(Esc(report.GeneratedUtc.ToString("yyyy-MM-dd HH:mm:ss'Z'")))
          .Append("</p>\n");

        var verb = report.DryRun ? "would convert" : "converted";
        sb.Append("<ul class=\"totals\">\n");
        sb.Append("<li class=\"t-converted\">").Append(Esc(verb)).Append(' ').Append(report.Converted).Append("</li>\n");
        sb.Append("<li class=\"t-skipped\">skipped ").Append(report.Skipped).Append("</li>\n");
        sb.Append("<li class=\"t-review\">flagged ").Append(report.Flagged).Append("</li>\n");
        sb.Append("<li class=\"t-error\">errors ").Append(report.Errors).Append("</li>\n");
        sb.Append("<li class=\"t-total\">total ").Append(report.Total).Append("</li>\n");
        sb.Append("</ul>\n");

        // Results table.
        sb.Append("<table class=\"results\">\n<thead>\n<tr>");
        sb.Append("<th>Project</th><th>Result</th><th>Packages</th><th>Notes</th>");
        sb.Append("</tr>\n</thead>\n<tbody>\n");
        foreach (var p in report.Projects)
        {
            var (label, cls) = StatusLabel(p.Status);
            sb.Append("<tr class=\"").Append(cls).Append("\">");
            sb.Append("<td><code>").Append(Esc(p.RelativePath)).Append("</code></td>");
            sb.Append("<td><span class=\"badge ").Append(cls).Append("\">").Append(Esc(label)).Append("</span></td>");
            sb.Append("<td>").Append(Packages(p)).Append("</td>");
            sb.Append("<td>").Append(Esc(Notes(p))).Append("</td>");
            sb.Append("</tr>\n");
        }
        sb.Append("</tbody>\n</table>\n");

        if (report.AffectedSolutions.Count > 0)
        {
            sb.Append("<h2>Solutions</h2>\n<ul class=\"solutions\">\n");
            foreach (var s in report.AffectedSolutions)
                sb.Append("<li><code>").Append(Esc(s)).Append("</code></li>\n");
            sb.Append("</ul>\n");
        }

        var flagged = report.Projects.Where(p => p.Review.Count > 0).ToList();
        if (flagged.Count > 0)
        {
            sb.Append("<h2>Manual review</h2>\n");
            foreach (var p in flagged)
            {
                sb.Append("<h3><code>").Append(Esc(p.RelativePath)).Append("</code></h3>\n<ul>\n");
                foreach (var item in p.Review)
                    sb.Append("<li>").Append(Esc(item)).Append("</li>\n");
                sb.Append("</ul>\n");
            }
        }

        if (report.Verify.Count > 0)
        {
            sb.Append("<h2>Verification</h2>\n");
            sb.Append("<table class=\"results\">\n<thead>\n<tr>");
            sb.Append("<th>Target</th><th>Result</th><th>Details</th>");
            sb.Append("</tr>\n</thead>\n<tbody>\n");
            foreach (var v in report.Verify)
            {
                var (label, cls) = v.Skipped ? ("Skipped", "skipped")
                    : v.Succeeded ? ("Pass", "converted")
                    : ("Fail", "error");
                sb.Append("<tr class=\"").Append(cls).Append("\">");
                sb.Append("<td><code>").Append(Esc(v.Target)).Append("</code></td>");
                sb.Append("<td><span class=\"badge ").Append(cls).Append("\">").Append(Esc(label)).Append("</span></td>");
                sb.Append("<td>");
                sb.Append(string.IsNullOrEmpty(v.Details) ? "—" : $"<pre>{Esc(v.Details!)}</pre>");
                sb.Append("</td>");
                sb.Append("</tr>\n");
            }
            sb.Append("</tbody>\n</table>\n");
        }

        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>Renders the report and writes it to <paramref name="path"/> as UTF-8 (no BOM).</summary>
    public static void Write(MigrationReport report, string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, Render(report), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string Packages(ReportEntry p)
    {
        if (p.Packages.Count == 0) return "<span class=\"muted\">—</span>";
        return string.Join("<br>", p.Packages.Select(kv =>
            $"{Esc(kv.Key)} <span class=\"muted\">{Esc(kv.Value)}</span>"));
    }

    private static (string label, string cls) StatusLabel(ConvertStatus s) => s switch
    {
        ConvertStatus.Converted => ("Converted", "converted"),
        ConvertStatus.Skipped   => ("Skipped", "skipped"),
        ConvertStatus.Review    => ("Review", "review"),
        ConvertStatus.Error     => ("Error", "error"),
        _                       => ("?", "skipped"),
    };

    private static string Notes(ReportEntry p) => p.Status switch
    {
        ConvertStatus.Error   => p.Error ?? "error",
        ConvertStatus.Skipped => "already SDK-style",
        ConvertStatus.Review  => $"{p.Review.Count} item(s) need review",
        _                     => "clean",
    };

    // HTML-escapes interpolated text. WebUtility.HtmlEncode handles &, <, >, ".
    private static string Esc(string s) => WebUtility.HtmlEncode(s);

    // A small, self-contained stylesheet. Status colours: green (converted),
    // grey (skipped), amber (review), red (error).
    private const string Css = """
        :root { color-scheme: light dark; }
        body { font-family: -apple-system, Segoe UI, Roboto, Helvetica, Arial, sans-serif;
               margin: 2rem; line-height: 1.45; color: #1b1b1b; background: #fff; }
        h1 { font-size: 1.6rem; margin-bottom: .25rem; }
        h2 { margin-top: 2rem; border-bottom: 1px solid #ddd; padding-bottom: .25rem; }
        .meta { color: #555; }
        code { font-family: Consolas, Menlo, monospace; font-size: .9em; }
        .muted { color: #888; }
        ul.totals { list-style: none; padding: 0; display: flex; flex-wrap: wrap; gap: .5rem; }
        ul.totals li { padding: .35rem .7rem; border-radius: .4rem; font-weight: 600; }
        .t-converted { background: #e6f4ea; color: #137333; }
        .t-skipped   { background: #f1f3f4; color: #5f6368; }
        .t-review    { background: #fef7e0; color: #b06000; }
        .t-error     { background: #fce8e6; color: #c5221f; }
        .t-total     { background: #e8f0fe; color: #1a73e8; }
        table.results { border-collapse: collapse; width: 100%; margin-top: 1rem; }
        table.results th, table.results td { border: 1px solid #ddd; padding: .5rem .65rem; text-align: left; vertical-align: top; }
        table.results th { background: #f5f5f5; }
        .badge { display: inline-block; padding: .15rem .55rem; border-radius: .35rem; font-size: .85em; font-weight: 600; }
        tr.converted .badge, .badge.converted { background: #e6f4ea; color: #137333; }
        tr.skipped .badge,   .badge.skipped   { background: #f1f3f4; color: #5f6368; }
        tr.review .badge,    .badge.review    { background: #fef7e0; color: #b06000; }
        tr.error .badge,     .badge.error     { background: #fce8e6; color: #c5221f; }
        tr.converted td:first-child { border-left: 4px solid #34a853; }
        tr.skipped   td:first-child { border-left: 4px solid #9aa0a6; }
        tr.review    td:first-child { border-left: 4px solid #f9ab00; }
        tr.error     td:first-child { border-left: 4px solid #ea4335; }
        pre { margin: 0; white-space: pre-wrap; font-size: .85em; }

        """;
}
