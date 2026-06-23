// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using nanoFramework.Migrate.Cli.Commands;
using nanoFramework.Migrate.Cli.Rendering;
using Spectre.Console.Cli;
using Xunit;

namespace nanoFramework.Migrate.Tests;

public class ReportingTests
{
    // A representative set of project outcomes: one clean conversion (with packages),
    // one skipped (already SDK-style), one flagged for review (with a name that needs
    // HTML escaping), and one error.
    private static MigrationReport BuildRepresentativeReport(DateTime utc)
    {
        var converted = new ReportEntry
        {
            RelativePath = "src/Clean.nfproj",
            Status = ConvertStatus.Converted,
            Packages = new[]
            {
                new KeyValuePair<string, string>("nanoFramework.System.Text", "1.2.54"),
            },
        };
        var skipped = new ReportEntry
        {
            RelativePath = "src/Already.nfproj",
            Status = ConvertStatus.Skipped,
        };
        var review = new ReportEntry
        {
            // A name with characters that MUST be escaped in HTML.
            RelativePath = "src/A&B<Weird>.nfproj",
            Status = ConvertStatus.Review,
            Review = new[] { "HintPath '..\\bin\\<x>' could not be resolved & needs review" },
        };
        var error = new ReportEntry
        {
            RelativePath = "src/Boom.nfproj",
            Status = ConvertStatus.Error,
            Error = "the project file could not be parsed",
        };

        return new MigrationReport
        {
            RootPath = @"C:\repo",
            GeneratedUtc = utc,
            DryRun = false,
            Projects = new[] { converted, skipped, review, error },
            AffectedSolutions = new[] { "Sample.sln" },
            Verify = new[]
            {
                new ReportVerifyEntry { Target = "Sample.sln", Succeeded = true, Skipped = false },
            },
        };
    }

    [Fact]
    public void Markdown_contains_totals_a_row_per_project_and_review_section()
    {
        var report = BuildRepresentativeReport(new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc));
        var md = MarkdownReportWriter.Render(report);

        // Totals line.
        Assert.Contains("converted 1", md);
        Assert.Contains("skipped 1", md);
        Assert.Contains("flagged 1", md);
        Assert.Contains("errors 1", md);
        Assert.Contains("total 4", md);

        // A row per project (the table uses the relative path in the first cell).
        Assert.Contains("src/Clean.nfproj", md);
        Assert.Contains("src/Already.nfproj", md);
        Assert.Contains("src/Boom.nfproj", md);

        // The package id/version appear in the converted row.
        Assert.Contains("nanoFramework.System.Text", md);
        Assert.Contains("1.2.54", md);

        // The manual review section lists the flagged item.
        Assert.Contains("## Manual review", md);
        Assert.Contains("HintPath", md);

        // The verification section is present.
        Assert.Contains("## Verification", md);
    }

    [Fact]
    public void Html_contains_rows_status_css_classes_and_escapes_special_characters()
    {
        var report = BuildRepresentativeReport(new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc));
        var html = HtmlReportWriter.Render(report);

        // Self-contained: inline style, no external assets.
        Assert.Contains("<style>", html);
        Assert.DoesNotContain("<link", html);
        Assert.DoesNotContain("src=\"http", html);

        // Status CSS classes/colours: each status drives a row/badge class, and the
        // colour palette is defined in the inline stylesheet.
        Assert.Contains("class=\"converted\"", html);
        Assert.Contains("class=\"skipped\"", html);
        Assert.Contains("class=\"review\"", html);
        Assert.Contains("class=\"error\"", html);
        Assert.Contains("#137333", html); // green (converted)
        Assert.Contains("#b06000", html); // amber (review)
        Assert.Contains("#c5221f", html); // red (error)

        // A row per project.
        Assert.Contains("src/Clean.nfproj", html);
        Assert.Contains("src/Boom.nfproj", html);

        // The project name with < and & is HTML-escaped (never emitted raw).
        Assert.Contains("A&amp;B&lt;Weird&gt;.nfproj", html);
        Assert.DoesNotContain("A&B<Weird>.nfproj", html);

        // The review note's special characters are escaped too.
        Assert.Contains("needs review", html);
        Assert.DoesNotContain("& needs review", html); // raw ampersand never leaks
    }

    [Fact]
    public void DryRun_report_uses_would_convert_wording()
    {
        var report = new MigrationReport
        {
            RootPath = @"C:\repo",
            GeneratedUtc = new DateTime(2026, 6, 16, 0, 0, 0, DateTimeKind.Utc),
            DryRun = true,
            Projects = new[]
            {
                new ReportEntry { RelativePath = "A.nfproj", Status = ConvertStatus.Converted },
            },
        };

        var md = MarkdownReportWriter.Render(report);
        var html = HtmlReportWriter.Render(report);

        Assert.Contains("would convert 1", md);
        Assert.Contains("(dry run)", md);
        Assert.Contains("would convert 1", html);
        Assert.Contains("(dry run)", html);
    }

    [Theory]
    [InlineData("report.md", ReportFormat.Markdown)]
    [InlineData("report.markdown", ReportFormat.Markdown)]
    [InlineData("report.html", ReportFormat.Html)]
    [InlineData("report.htm", ReportFormat.Html)]
    [InlineData("report.txt", ReportFormat.Markdown)] // unknown -> Markdown
    [InlineData("report", ReportFormat.Markdown)]      // no extension -> Markdown
    public void Format_is_chosen_by_extension(string path, ReportFormat expected)
    {
        Assert.Equal(expected, MigrationReportBuilder.FormatFor(path));
    }

    [Fact]
    public void Markdown_smoke_writes_a_nonempty_markdown_file()
    {
        RunSmoke("out.md", contents =>
        {
            Assert.NotEqual(0, contents.Length);
            Assert.Contains("# nanoFramework migration report", contents);
            Assert.Contains("| Project | Result | Packages | Notes |", contents);
        });
    }

    [Fact]
    public void Html_smoke_writes_a_nonempty_html_file()
    {
        RunSmoke("out.html", contents =>
        {
            Assert.NotEqual(0, contents.Length);
            Assert.Contains("<!DOCTYPE html>", contents);
            Assert.Contains("<table class=\"results\">", contents);
        });
    }

    // Drives the shared `migrate` command against a temp dir with a single convertible
    // .nfproj, in --dry-run so nothing on disk is mutated, asserting the report file is
    // written in the right format. This is the end-to-end --report wiring smoke.
    private static void RunSmoke(string reportName, Action<string> assertContents)
    {
        using var dir = new TempDir();
        dir.File("Sample.nfproj", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup><AssemblyName>Sample</AssemblyName></PropertyGroup>
            </Project>
            """);
        var reportPath = dir.Combine(reportName);

        var app = new CommandApp();
        app.Configure(config =>
        {
            config.PropagateExceptions();
            MigrateRegistration.Add(config, "Convert .nfproj projects.");
        });

        // --dry-run keeps it hermetic (no writes/verify); the report is still produced.
        var exit = app.Run(new[] { "migrate", dir.Path, "--report", reportPath, "--dry-run", "--no-verify" });

        Assert.True(File.Exists(reportPath), $"report not written (exit {exit})");
        assertContents(File.ReadAllText(reportPath));
    }
}
