// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using NanoFramework.Migrate.Cli.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;
using static NanoFramework.Migrate.Cli.Rendering.ConsoleSupport;

namespace NanoFramework.Migrate.Cli.Commands;

/// <summary>
/// A small shared base so the <c>migrate</c>, <c>clean</c> and <c>rollback</c>
/// commands declare the common <c>-y|--yes</c> flag once. (The commands are sibling
/// top-level commands rather than subcommands of <c>migrate</c> because Spectre.Cli
/// cannot host a command that is both a branch/default AND takes a positional
/// argument, and <c>migrate &lt;path&gt;</c> must keep working.)
/// </summary>
public abstract class AssumeYesSettings : CommandSettings
{
    [CommandOption("-y|--yes")]
    [Description("Skip interactive prompts: proceed with the default action. (Non-interactive runs never prompt regardless.)")]
    public bool AssumeYes { get; init; }
}

public sealed class MigrateSettings : AssumeYesSettings
{
    [CommandArgument(0, "<path>")]
    [Description("A .nfproj file, a solution (.sln/.slnx), or a directory. "
               + "For a solution, only its referenced .nfproj are converted and the solution is retargeted. "
               + "For a directory, discovered solutions drive the selection; with no solution found, every .nfproj under the directory is converted.")]
    public string Path { get; init; } = "";

    [CommandOption("--solution <path>")]
    [Description("Migrate only this solution (.sln or .slnx): convert just its referenced .nfproj and retarget that one solution. Overrides directory discovery.")]
    public string? Solution { get; init; }

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
    [Description("Don't write a .nfproj.bak.")]
    public bool NoBackup { get; init; }

    [CommandOption("--dry-run|--no-write")]
    [Description("Analyse and preview only; write nothing.")]
    public bool DryRun { get; init; }

    [CommandOption("--glob <pattern>")]
    [Description("Only convert .nfproj whose path (relative to <path>) matches the glob; the solutions referencing any matched project are updated. Supports *, ** and ?. Example: \"Beginner/**\".")]
    public string? Glob { get; init; }

    [CommandOption("--verify")]
    [Description("After a real migration, build the affected solution(s)/project(s) to verify the result; a failed build offers a rollback. Default: on for real runs, off for --dry-run.")]
    public bool Verify { get; init; }

    [CommandOption("--no-verify")]
    [Description("Skip the post-migration build verification.")]
    public bool NoVerify { get; init; }

    [CommandOption("--report <path>")]
    [Description("Write a migration report to this path. The format is chosen by the extension: "
               + ".md/.markdown -> Markdown, .html/.htm -> HTML (anything else -> Markdown). "
               + "Works for --dry-run too (reports what WOULD change).")]
    public string? Report { get; init; }

    public override ValidationResult Validate()
    {
        if (Ext is not (".nfproj" or ".csproj"))
            return ValidationResult.Error("--ext must be .nfproj or .csproj");
        if (Verify && NoVerify)
            return ValidationResult.Error("--verify and --no-verify are mutually exclusive");
        return ValidationResult.Success();
    }

    public ConversionOptions ToConversionOptions() => new()
    {
        Sdk = Sdk,
        Tfm = Tfm,
        Ext = Ext,
        NoBackup = NoBackup,
        DryRun = DryRun,
        Glob = Glob,
        // --verify forces on, --no-verify forces off, neither leaves the default
        // (on for real runs, off for dry-run).
        Verify = Verify ? true : NoVerify ? false : (bool?)null,
    };
}

public sealed class MigrateCommand : Command<MigrateSettings>
{
    private readonly IProjectConverter _converter = new ProjectConverter();

    protected override int Execute(CommandContext context, MigrateSettings settings, CancellationToken cancellationToken)
    {
        Header("NanoMigrate");

        var o = settings.ToConversionOptions();
        var plan = MigrationPlanner.Plan(settings.Path, settings.Solution, settings.Glob);

        // Solution-aware plans (explicit solution, directory-with-solutions, glob)
        // are handled by their own path; loose/single plans keep the historical flow.
        return plan.Kind switch
        {
            PlanKind.SingleProject or PlanKind.LooseDirectory => RunLoose(settings, o, plan),
            _ => RunSolutionScoped(settings, o, plan),
        };
    }

    // The historical flow: a single .nfproj or a directory with no solutions. The
    // converter retargets any solutions it finds by walking up the tree.
    private int RunLoose(MigrateSettings settings, ConversionOptions o, MigrationPlan plan)
    {
        var targets = plan.LooseProjects.ToList();

        // Reentrant: a fully-converted tree has no .nfproj left. Exit cleanly (0).
        if (targets.Count == 0)
        {
            var why = o.Glob is null
                ? $"no .nfproj found under '{Esc(settings.Path)}' (already SDK-style?)."
                : $"no .nfproj matched glob '{Esc(o.Glob)}' under '{Esc(settings.Path)}'.";
            AnsiConsole.MarkupLine($"[grey]nothing to convert: {why}[/]");
            return 0;
        }

        var baseDir = BaseDirFor(settings.Path);

        AnsiConsole.MarkupLine(o.DryRun
            ? $"[yellow]Dry run[/] — analysing [bold]{targets.Count}[/] project(s) under [blue]{Esc(baseDir)}[/]. Nothing will be written."
            : $"Found [bold]{targets.Count}[/] project(s) to convert under [blue]{Esc(baseDir)}[/].");
        AnsiConsole.WriteLine();

        if (!Confirm($"Proceed with {targets.Count} conversion(s)?", settings, o)) return AbortedExit();

        // Record a rollback journal (real runs only): back up every file the
        // conversions will modify or delete BEFORE the converter touches disk.
        var journal = OpenJournal(baseDir, o);
        if (journal is not null)
            RecordJournal(journal, targets, o, extraSolutions: null);

        var results = ProcessProjects(targets, baseDir, o);
        journal?.Save();

        var rewritten = Array.Empty<string>();
        var report = Report(results, baseDir, o, rewritten);

        // Verification targets in loose mode are the converted projects themselves
        // (no host-owned solution set). A failed verify can roll the run back.
        var verifyTargets = results
            .Where(r => r.Result.Status is ConvertStatus.Converted or ConvertStatus.Review)
            .Select(r => r.Result.OutputPath)
            .ToList();
        var reportCtx = new ReportContext(results, baseDir, o.DryRun, rewritten, settings.Report);
        return VerifyAndMaybeRollback(report, verifyTargets, journal, baseDir, settings, o, reportCtx);
    }

    // The solution-aware flow: pick the candidate solutions (multi-select / confirm),
    // convert their .nfproj, then retarget exactly those solutions ourselves.
    private int RunSolutionScoped(MigrateSettings settings, ConversionOptions o, MigrationPlan plan)
    {
        var baseDir = BaseDirFor(settings.Path);

        if (plan.Candidates.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]nothing to convert: the selected solution(s) reference no .nfproj (already SDK-style?).[/]");
            return 0;
        }

        // Show what is in scope before any prompt.
        MigrateRenderer.RenderCandidateSolutions(plan.Candidates, baseDir);

        var chosen = SelectSolutions(plan, settings, o);
        if (chosen.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]aborted; no solution selected, nothing written.[/]");
            return 0;
        }

        var targets = MigrationPlan.ProjectsOf(chosen);
        if (targets.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]nothing to convert: the chosen solution(s) reference no .nfproj.[/]");
            return 0;
        }

        AnsiConsole.MarkupLine(o.DryRun
            ? $"[yellow]Dry run[/] — analysing [bold]{targets.Count}[/] project(s) across [bold]{chosen.Count}[/] solution(s). Nothing will be written."
            : $"Converting [bold]{targets.Count}[/] project(s) across [bold]{chosen.Count}[/] solution(s).");
        AnsiConsole.WriteLine();

        if (!Confirm($"Proceed with {targets.Count} conversion(s) and update {chosen.Count} solution(s)?", settings, o))
            return AbortedExit();

        // The host owns solution retargeting here, so the converter must not also
        // walk up and rewrite solutions the user did not select.
        var convOpts = o with { SkipSolutionRewrite = true };

        // Record a rollback journal (real runs only): back up the files the
        // conversions touch PLUS the chosen solutions the host will rewrite itself.
        var journal = OpenJournal(baseDir, o);
        if (journal is not null)
            RecordJournal(journal, targets, convOpts, extraSolutions: chosen.Select(c => c.Solution.Path));

        var results = ProcessProjects(targets, baseDir, convOpts);

        // Retarget exactly the chosen solutions to the converted projects. Only
        // projects that actually converted (a .csproj now exists / would exist) are
        // handed to the rewriter; idempotent on re-run.
        var converted = results
            .Where(r => r.Result.Status is ConvertStatus.Converted or ConvertStatus.Review)
            .Select(r => r.Nfproj)
            .ToList();

        var rewritten = new List<string>();
        if (!o.DryRun && converted.Count > 0)
        {
            foreach (var c in chosen)
                if (SolutionRewriter.RewriteFile(c.Solution, converted))
                    rewritten.Add(c.Solution.Path);
        }

        journal?.Save();
        var report = Report(results, baseDir, o, rewritten);

        // Verify by building exactly the chosen solutions.
        var verifyTargets = chosen.Select(c => c.Solution.Path).ToList();
        var reportCtx = new ReportContext(results, baseDir, o.DryRun, rewritten, settings.Report);
        return VerifyAndMaybeRollback(report, verifyTargets, journal, baseDir, settings, o, reportCtx);
    }

    // Opens a rollback journal rooted at baseDir for real runs only; dry-runs return
    // null (nothing is written, nothing to reverse).
    private static RollbackJournal? OpenJournal(string baseDir, ConversionOptions o) =>
        o.DryRun ? null : RollbackJournal.Start(baseDir);

    // Backs up — into the journal — every file the conversions will modify or delete,
    // and records the files they will create, by analysing each target with a dry-run
    // preview pass FIRST (before any real write). extraSolutions are host-rewritten
    // solutions to back up as well.
    private void RecordJournal(RollbackJournal journal, List<string> targets, ConversionOptions o,
        IEnumerable<string>? extraSolutions)
    {
        var dryOpts = o with { DryRun = true };
        var extras = extraSolutions?.ToList();
        foreach (var nf in targets)
        {
            ConvertResult preview;
            try { preview = _converter.Convert(nf, dryOpts); }
            catch { continue; } // a project that fails to analyse is converted (and reported) normally; just not journaled
            if (preview.AlreadySdk) continue;
            MigrationJournaling.Record(journal, preview, extras);
            extras = null; // back up the host solutions once, not per project
        }
    }

    // Runs the verification build (when enabled) and, on failure, applies the rollback
    // policy: prompt-and-revert interactively, or keep-and-advise non-interactively.
    // Maps to the final exit code. When verification is off/clean, returns the
    // already-computed migrate exit code unchanged.
    private int VerifyAndMaybeRollback(int migrateExit, List<string> verifyTargets,
        RollbackJournal? journal, string baseDir, MigrateSettings settings, ConversionOptions o,
        ReportContext reportCtx)
    {
        if (!o.VerifyEffective || verifyTargets.Count == 0)
        {
            WriteReportIfRequested(reportCtx, verify: null);
            return migrateExit;
        }

        var builder = new SolutionBuilder();
        List<BuildOutcome> outcomes;
        bool toolMissing = false;
        if (IsInteractive())
        {
            List<BuildOutcome>? captured = null;
            AnsiConsole.Status().Spinner(Spinner.Known.Dots).Start("Verifying build…", ctx =>
                captured = builder.VerifyAll(verifyTargets,
                    t => ctx.Status($"Building [blue]{Esc(Path.GetFileName(t))}[/]…"),
                    () => toolMissing = true));
            outcomes = captured!;
        }
        else
        {
            outcomes = builder.VerifyAll(verifyTargets, null, () => toolMissing = true);
        }

        if (toolMissing)
            AnsiConsole.MarkupLine("[yellow]warning:[/] dotnet not found on PATH — build verification skipped.");

        MigrateRenderer.RenderVerifyTable(outcomes, baseDir);

        // Write the report (when requested) now that the verify outcomes are known, so
        // a single write covers the passed-and-failed paths alike. A subsequent
        // rollback reverts files on disk but the report still records what was attempted.
        WriteReportIfRequested(reportCtx, outcomes);

        var verifyOutcome = Verification.Evaluate(outcomes);
        if (verifyOutcome != VerifyOutcome.Failed)
            return migrateExit;

        var failed = Verification.FailedCount(outcomes);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[red]Build verification failed for {failed} target(s).[/]");

        var interactive = IsInteractive() && !settings.AssumeYes;
        bool? answer = null;
        if (interactive)
            answer = AnsiConsole.Confirm(
                $"Migration verification failed for {failed} target(s). Roll back all changes?", defaultValue: false);
        else if (settings.AssumeYes)
            // --yes is about skipping prompts to PROCEED, never to silently destroy a
            // migration; a failed verify under --yes keeps the changes (advise rollback).
            answer = false;

        var decision = Verification.Decide(verifyOutcome, interactive, answer);
        switch (decision)
        {
            case RollbackDecision.RollBack:
                ApplyRollback(journal, baseDir);
                AnsiConsole.MarkupLine("[green]All changes rolled back.[/]");
                return 1;
            case RollbackDecision.KeepInteractive:
                AnsiConsole.MarkupLine(
                    $"[yellow]Changes kept.[/] Run [bold]rollback \"{Esc(baseDir)}\"[/] later to revert.");
                return 1;
            case RollbackDecision.KeepNonInteractive:
            default:
                AnsiConsole.MarkupLine(
                    $"[yellow]Changes kept (non-interactive).[/] Run [bold]rollback \"{Esc(baseDir)}\"[/] to revert.");
                return 1;
        }
    }

    // Reverts the just-recorded run from the live journal (preferred), falling back to
    // the latest saved manifest under baseDir. Removes the backup set on success.
    private static void ApplyRollback(RollbackJournal? journal, string baseDir)
    {
        RollbackResult? result = null;
        if (journal is not null && !journal.IsEmpty && File.Exists(journal.ManifestPath))
            result = RollbackJournal.ApplyAndCleanup(journal.ManifestPath);
        if (result is null)
        {
            var latest = RollbackJournal.ManifestPaths(baseDir).FirstOrDefault();
            if (latest is not null) result = RollbackJournal.ApplyAndCleanup(latest);
        }
        if (result is not null) MigrateRenderer.RenderRollbackResult(result, baseDir);
    }

    // Decides which candidate solutions to operate on. Explicit single solution: no
    // choice. Otherwise multi-select when interactive; non-interactive / --yes
    // selects all affected (announcing it).
    private static List<SolutionCandidate> SelectSolutions(MigrationPlan plan, MigrateSettings settings, ConversionOptions o)
    {
        // An explicit single solution (positional .sln/.slnx or --solution) is the
        // only candidate; there is nothing to choose.
        if (!plan.RequiresSelection)
            return plan.Candidates.ToList();

        var all = plan.Candidates.ToList();

        // Only one affected solution: a simple confirm, not a multi-select.
        if (all.Count == 1)
        {
            if (settings.AssumeYes || o.DryRun || !IsInteractive())
            {
                if (!settings.AssumeYes && !o.DryRun)
                    AnsiConsole.MarkupLine("[grey]non-interactive: selecting the only affected solution.[/]");
                return all;
            }
            var fmt = all[0].Solution.Format == SolutionFormat.Xml ? "slnx" : "sln";
            return AnsiConsole.Confirm($"Migrate solution '{Esc(Path.GetFileName(all[0].Solution.Path))}' ({fmt})?")
                ? all : new List<SolutionCandidate>();
        }

        // Several solutions: CI / non-interactive selects all and proceeds.
        if (settings.AssumeYes || o.DryRun || !IsInteractive())
        {
            if (!settings.AssumeYes && !o.DryRun)
                AnsiConsole.MarkupLine($"[grey]non-interactive: selecting all {all.Count} affected solution(s).[/]");
            return all;
        }

        // Interactive: present the multi-select. Picking none aborts cleanly.
        var prompt = new MultiSelectionPrompt<SolutionCandidate>()
            .Title("Select the solution(s) to migrate")
            .NotRequired()                       // picking none is allowed (aborts)
            .PageSize(15)
            .InstructionsText("[grey](space to toggle, enter to confirm; pick none to abort)[/]")
            .UseConverter(c =>
            {
                var fmt = c.Solution.Format == SolutionFormat.Xml ? "slnx" : "sln";
                return $"{Path.GetFileName(c.Solution.Path)} ({fmt}, {c.NanoProjects.Count} project(s))";
            });
        prompt.AddChoices(all);
        return AnsiConsole.Prompt(prompt);
    }

    // The shared confirm gate. Returns true when we should proceed. In dry-run, when
    // --yes is set, or in a non-interactive context we never block.
    private static bool Confirm(string question, MigrateSettings settings, ConversionOptions o)
    {
        if (o.DryRun || settings.AssumeYes || !IsInteractive()) return true;
        return AnsiConsole.Confirm(question);
    }

    private static int AbortedExit()
    {
        AnsiConsole.MarkupLine("[grey]aborted; nothing written.[/]");
        return 0;
    }

    // The base directory glob/relative paths are reported against.
    private static string BaseDirFor(string path) =>
        Directory.Exists(path) ? Path.GetFullPath(path)
        : File.Exists(path)    ? Path.GetDirectoryName(Path.GetFullPath(path))!
                               : Path.GetFullPath(path);

    // Renders the summary/review/tally and (for solution-scoped runs) the rewritten
    // solutions, then maps the outcome to the exit code (0 clean / 2 review / 1 error).
    private static int Report(List<ProjectOutcome> results, string baseDir, ConversionOptions o, IReadOnlyList<string> rewritten)
    {
        MigrateRenderer.RenderSummaryTable(results, baseDir, o.DryRun);
        MigrateRenderer.RenderReviewNotes(results, baseDir);
        MigrateRenderer.RenderRewrittenSolutions(rewritten, baseDir);
        MigrateRenderer.RenderTally(results, o.DryRun);

        var errors = results.Count(r => r.Result.Status == ConvertStatus.Error);
        var flagged = results.Count(r => r.Result.Status == ConvertStatus.Review);
        if (errors > 0) return 1;
        return flagged > 0 ? 2 : 0;
    }

    // The data needed to render the optional migration report, carried from the
    // outcome computation through the verify pass so a single write site can include
    // the verify results when a verify pass ran. ReportPath is null when --report
    // was not supplied.
    private readonly record struct ReportContext(
        IReadOnlyList<ProjectOutcome> Results,
        string BaseDir,
        bool DryRun,
        IReadOnlyList<string> Rewritten,
        string? ReportPath);

    // Builds the MigrationReport from the same outcome/verify data the summary table
    // used and writes it to the requested path (format chosen by extension), printing
    // a Spectre line. A no-op when --report was not supplied. The caller (Core) owns
    // the clock: the timestamp is captured here, not inside the engine.
    private static void WriteReportIfRequested(ReportContext ctx, IReadOnlyList<BuildOutcome>? verify)
    {
        if (string.IsNullOrWhiteSpace(ctx.ReportPath)) return;

        var report = MigrationReportBuilder.Build(
            ctx.Results, ctx.BaseDir, ctx.DryRun, ctx.Rewritten, verify, DateTime.UtcNow);

        try
        {
            var format = MigrationReportBuilder.Write(report, ctx.ReportPath);
            var kind = format == ReportFormat.Html ? "HTML" : "Markdown";
            AnsiConsole.MarkupLine($"[grey]{kind} report written to[/] [blue]{Esc(ctx.ReportPath)}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]warning:[/] could not write report to [blue]{Esc(ctx.ReportPath)}[/]: {Esc(ex.Message)}");
        }
    }

    // Runs every conversion, surfacing progress with a spinner. Any per-project
    // exception is captured as an Error row rather than aborting the batch.
    private List<ProjectOutcome> ProcessProjects(List<string> targets, string baseDir, ConversionOptions o)
    {
        var results = new List<ProjectOutcome>(targets.Count);

        void RunAll(Action<string>? report)
        {
            foreach (var nf in targets)
            {
                var rel = Path.GetRelativePath(baseDir, nf);
                report?.Invoke(rel);
                ConvertResult result;
                try
                {
                    result = _converter.Convert(nf, o);
                }
                catch (Exception ex)
                {
                    result = new ConvertResult { OutputPath = nf, Error = ex.Message };
                }
                results.Add(new ProjectOutcome(nf, result));
            }
        }

        // Status() needs an interactive, non-redirected console; otherwise just
        // run straight through (Spectre would no-op the spinner anyway).
        if (IsInteractive())
        {
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start(o.DryRun ? "Analysing…" : "Converting…", ctx =>
                    RunAll(rel => ctx.Status($"{(o.DryRun ? "Analysing" : "Converting")} [blue]{Esc(rel)}[/]…")));
        }
        else
        {
            RunAll(null);
        }
        return results;
    }
}
