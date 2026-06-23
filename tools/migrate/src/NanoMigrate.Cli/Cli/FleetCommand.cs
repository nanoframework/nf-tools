// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using nanoFramework.Migrate.Cli.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;
using static nanoFramework.Migrate.Cli.Rendering.ConsoleSupport;

namespace nanoFramework.Migrate.Cli.Commands;

internal sealed class FleetSettings : CommandSettings
{
    [CommandArgument(0, "<repos-dir>")]
    [Description("A directory of cloned repos; every .nfproj across them is migrated.")]
    public string ReposDir { get; init; } = "";

    [CommandOption("--sdk <version>")]
    [Description("Accepted for back-compat but ignored: the SDK reference is versionless (pinned via global.json msbuild-sdks).")]
    public string Sdk { get; init; } = "2.0.0";

    [CommandOption("--tfm <moniker>")]
    [Description("Target framework moniker (default netnano1.0).")]
    public string Tfm { get; init; } = "netnano1.0";

    [CommandOption("--ext <ext>")]
    [Description("Output extension: .nfproj or .csproj (default .csproj).")]
    public string Ext { get; init; } = ".csproj";

    [CommandOption("--no-backup")]
    [Description("Don't write a .nfproj.bak (implied by --commit).")]
    public bool NoBackup { get; init; }

    [CommandOption("--dry-run|--no-write")]
    [Description("Analyse and preview only; write nothing.")]
    public bool DryRun { get; init; }

    [CommandOption("--glob <pattern>")]
    [Description("Only convert matching .nfproj within each repo. Supports *, ** and ?.")]
    public string? Glob { get; init; }

    [CommandOption("--report <path>")]
    [Description("Markdown report path (default migration-report.md).")]
    public string Report { get; init; } = "migration-report.md";

    [CommandOption("--branch <name>")]
    [Description("Create/reset this git branch in each repo (must not start with 'develop').")]
    public string? Branch { get; init; }

    [CommandOption("--commit")]
    [Description("Commit the changes (requires --branch). Uses a contribution-compliant message and signs off (Signed-off-by) by default.")]
    public bool Commit { get; init; }

    [CommandOption("--message <msg>")]
    [Description("Commit summary line (kept <= 50 chars).")]
    public string? Message { get; init; }

    [CommandOption("--issue <n>")]
    [Description("Reference an issue: adds a \"Fix #<n>\" trailer to the commit.")]
    public string? Issue { get; init; }

    [CommandOption("--no-sign-off")]
    [Description("Don't add a Signed-off-by line.")]
    public bool NoSignOff { get; init; }

    public override ValidationResult Validate()
    {
        if (Ext is not (".nfproj" or ".csproj"))
            return ValidationResult.Error("--ext must be .nfproj or .csproj");
        return ValidationResult.Success();
    }

    public FleetOptions ToFleetOptions() => new()
    {
        Conversion = new ConversionOptions
        {
            Sdk = Sdk,
            Tfm = Tfm,
            Ext = Ext,
            NoBackup = NoBackup,
            DryRun = DryRun,
            Glob = Glob,
        },
        Report = Report,
        Branch = Branch,
        Commit = Commit,
        CommitMessage = Message,
        Issue = Issue?.TrimStart('#'),
        SignOff = !NoSignOff,
    };
}

internal sealed class FleetCommand : Command<FleetSettings>
{
    protected override int Execute(CommandContext context, FleetSettings settings, CancellationToken cancellationToken)
    {
        Header("NanoMigrate · fleet");

        var o = settings.ToFleetOptions();
        var reposDir = settings.ReposDir;
        var service = new FleetService(new ProjectConverter(), new ProcessRunner());

        // Core validates and discovers; surface its messages as clean usage errors.
        List<string> repoDirs;
        try
        {
            repoDirs = service.ResolveRepos(reposDir, o);
        }
        catch (ArgumentException ex)
        {
            throw new UserError(ex.Message);
        }

        if (o.Conversion.DryRun)
            AnsiConsole.MarkupLine($"[yellow]Dry run[/] — {repoDirs.Count} repo(s); nothing will be written.");
        else
            AnsiConsole.MarkupLine($"Processing [bold]{repoDirs.Count}[/] repo(s) under [blue]{Esc(reposDir)}[/]"
                + (o.Branch is not null ? $" on branch [blue]{Esc(o.Branch)}[/]" : "")
                + (o.Commit ? ", committing" : "") + ".");
        AnsiConsole.WriteLine();

        List<RepoReport> report;
        if (IsInteractive())
        {
            List<RepoReport>? captured = null;
            AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .Start("Migrating…", ctx => captured = service.Process(repoDirs, o,
                    name => ctx.Status($"Migrating [blue]{Esc(name)}[/]…"), cancellationToken));
            report = captured!;
        }
        else
        {
            report = service.Process(repoDirs, o, cancellationToken: cancellationToken);
        }

        FleetRenderer.RenderFleetTable(report);
        FleetRenderer.RenderFleetTree(report);
        FleetRenderer.RenderFleetReview(report);

        service.WriteReport(report, o, reposDir);
        var errored = report.Count(r => r.Error is not null);
        var needsReview = report.Count(r => r.Error is null && r.Review.Count > 0);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"[bold]{report.Count}[/] repo(s) processed  •  "
          + $"[yellow]{needsReview} need review[/]  •  [red]{errored} with errors[/]  •  "
          + $"report: [blue]{Esc(o.Report)}[/]");
        return errored > 0 ? 2 : 0;
    }
}
