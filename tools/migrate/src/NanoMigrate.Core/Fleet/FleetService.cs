// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using nanoFramework.Migrate.Core.Common;
using nanoFramework.Migrate.Core.Projects;

namespace nanoFramework.Migrate.Core.Fleet;

/// <summary>Result of a single git invocation. Mirrors a process exit.</summary>
public readonly record struct GitResult(int Code, string Stdout, string Stderr);

/// <summary>
/// Runs git in a working directory. The engine stays process-free by delegating
/// the actual side effect to an implementation supplied by the host (the CLI).
/// </summary>
public interface IGitRunner
{
    GitResult Run(string args, string workingDirectory);
}

/// <summary>The knobs that drive a fleet run, layered over the per-project options.</summary>
public sealed record FleetOptions
{
    /// <summary>Per-project conversion options applied to every project in the fleet.</summary>
    public ConversionOptions Conversion { get; init; } = new();

    /// <summary>Markdown report path.</summary>
    public string Report { get; init; } = "migration-report.md";

    /// <summary>Create/reset this git branch in each repo. Must not start with 'develop'.</summary>
    public string? Branch { get; init; }

    /// <summary>Commit the changes (requires <see cref="Branch"/>).</summary>
    public bool Commit { get; init; }

    /// <summary>Commit summary line (kept &lt;= 50 chars).</summary>
    public string? CommitMessage { get; init; }

    /// <summary>Referenced as "Fix #&lt;n&gt;" in the commit.</summary>
    public string? Issue { get; init; }

    /// <summary>nanoFramework recommends a Signed-off-by line; on by default.</summary>
    public bool SignOff { get; init; } = true;
}

/// <summary>
/// Pure fleet orchestration: validate options, discover repos, convert every
/// project in each, optionally branch and commit (via an injected
/// <see cref="IGitRunner"/>), and produce a markdown report. Never writes to the
/// console; progress is surfaced through an optional callback so a host can drive
/// a spinner.
/// </summary>
public sealed class FleetService
{
    private readonly IProjectConverter _converter;
    private readonly IGitRunner _git;

    public FleetService(IProjectConverter converter, IGitRunner git)
    {
        _converter = converter;
        _git = git;
    }

    /// <summary>
    /// Validates the fleet options against the repos directory, throwing
    /// <see cref="ArgumentException"/> with a user-facing message on any problem.
    /// Mirrors the original command's up-front checks.
    /// </summary>
    public List<string> ResolveRepos(string reposDir, FleetOptions o)
    {
        if (!Directory.Exists(reposDir)) throw new ArgumentException($"directory not found: {reposDir}");
        if (o.Commit && o.Branch is null) throw new ArgumentException("--commit requires --branch");
        // nanoFramework workflow: branch names must not start with "develop" (they
        // collide with upstream develop-* branches).
        if (o.Branch is not null && o.Branch.StartsWith("develop", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("branch name must not start with 'develop' (nanoFramework workflow); "
                              + "use something like 'sdk-migration' or 'issue-123'");

        // A repo qualifies if it contains at least one .nfproj that survives the
        // glob filter (default: any .nfproj).
        bool RepoMatches(string d) => ProjectScanner.NfprojUnder(d, o.Conversion.Glob).Any();

        var repoDirs = Directory.EnumerateDirectories(reposDir)
                                .Where(RepoMatches)
                                .OrderBy(d => d).ToList();
        if (repoDirs.Count == 0)
            throw new ArgumentException(o.Conversion.Glob is null
                ? $"no repos containing .nfproj found under '{reposDir}'"
                : $"no repos with .nfproj matching glob '{o.Conversion.Glob}' found under '{reposDir}'");
        return repoDirs;
    }

    /// <summary>
    /// Processes every repo: optionally branch, convert each project, optionally
    /// commit. <paramref name="progress"/> (if supplied) is invoked with each repo
    /// name before it is processed. Returns one <see cref="RepoReport"/> per repo.
    /// </summary>
    public List<RepoReport> Process(List<string> repoDirs, FleetOptions o, Action<string>? progress = null)
    {
        // In a git repo the commit history already preserves the pre-migration file,
        // so a .bak alongside it is just noise in the diff. Skip backups when committing.
        var conv = o.Commit ? o.Conversion with { NoBackup = true } : o.Conversion;

        var report = new List<RepoReport>();
        foreach (var repo in repoDirs)
        {
            progress?.Invoke(Path.GetFileName(repo));
            var rr = new RepoReport { Name = Path.GetFileName(repo) };
            try
            {
                if (o.Branch is not null && !conv.DryRun)
                {
                    var checkout = _git.Run($"checkout -B {o.Branch}", repo);
                    if (checkout.Code != 0) { rr.Error = $"git checkout failed: {checkout.Stderr.Trim()}"; report.Add(rr); continue; }
                }

                foreach (var nf in ProjectScanner.NfprojUnder(repo, conv.Glob).OrderBy(p => p))
                {
                    rr.Projects++;
                    var result = _converter.Convert(nf, conv);
                    var rel = Path.GetRelativePath(repo, nf);
                    foreach (var item in result.Review) rr.Review.Add($"{rel}: {item}");
                }

                if (o.Commit && !conv.DryRun)
                {
                    _git.Run("add -A", repo);
                    var msgFile = WriteCommitMessage(o);
                    var signOff = o.SignOff ? "-s " : "";
                    var commit = _git.Run($"commit {signOff}-F \"{msgFile}\"", repo);
                    File.Delete(msgFile);
                    rr.Committed = commit.Code == 0;
                    if (commit.Code != 0 && !commit.Stderr.Contains("nothing to commit"))
                    {
                        rr.Error = commit.Stderr.Contains("Please tell me who you are") || commit.Stderr.Contains("user.name")
                            ? "git commit failed: set git user.name/user.email (real name) so the "
                              + "Signed-off-by line is valid, or pass --no-sign-off"
                            : $"git commit: {commit.Stderr.Trim()}";
                    }
                }
            }
            catch (Exception ex)
            {
                rr.Error = ex.Message;
            }
            report.Add(rr);
        }
        return report;
    }

    /// <summary>Builds the markdown fleet report and writes it to <c>o.Report</c>.</summary>
    public void WriteReport(List<RepoReport> report, FleetOptions o, string reposDir)
    {
        File.WriteAllText(o.Report, BuildReport(report, o, reposDir));
    }

    /// <summary>Builds the markdown fleet report as a string (pure; no I/O).</summary>
    public static string BuildReport(List<RepoReport> report, FleetOptions o, string reposDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# nanoFramework SDK-style migration — fleet report\n");
        sb.AppendLine($"- Source: `{Path.GetFullPath(reposDir)}`");
        sb.AppendLine($"- Mode: {(o.Conversion.DryRun ? "dry-run (no files written)" : "applied")}"
                    + (o.Branch is not null ? $", branch `{o.Branch}`" : "")
                    + (o.Commit ? ", committed" : ""));
        sb.AppendLine($"- SDK `nanoFramework.NET.Sdk` (versionless), TFM `{o.Conversion.Tfm}`, output extension `{o.Conversion.Ext}`\n");

        int total = report.Count, clean = report.Count(r => r.Error is null && r.Review.Count == 0);
        int needsReview = report.Count(r => r.Error is null && r.Review.Count > 0);
        int errors = report.Count(r => r.Error is not null);
        sb.AppendLine("## Summary\n");
        sb.AppendLine($"| Repos | Clean | Needs review | Errored |");
        sb.AppendLine($"|------:|------:|-------------:|--------:|");
        sb.AppendLine($"| {total} | {clean} | {needsReview} | {errors} |\n");

        if (errors > 0)
        {
            sb.AppendLine("## Errored repos\n");
            foreach (var r in report.Where(r => r.Error is not null))
                sb.AppendLine($"- **{r.Name}** — {r.Error}");
            sb.AppendLine();
        }

        if (needsReview > 0)
        {
            sb.AppendLine("## Repos needing manual review\n");
            sb.AppendLine("These migrated, but the tool could not confidently resolve everything. "
                        + "Each line is something a human should confirm before merging.\n");
            foreach (var r in report.Where(r => r.Error is null && r.Review.Count > 0))
            {
                sb.AppendLine($"### {r.Name}\n");
                foreach (var item in r.Review) sb.AppendLine($"- {item}");
                sb.AppendLine();
            }
        }

        if (clean > 0)
        {
            sb.AppendLine("## Clean migrations\n");
            sb.AppendLine("Converted with no items flagged for review:\n");
            foreach (var r in report.Where(r => r.Error is null && r.Review.Count == 0))
                sb.AppendLine($"- {r.Name} ({r.Projects} project(s))"
                            + (r.Committed ? " — committed" : ""));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    // Builds a commit message that follows the nanoFramework guidance: a short
    // summary (<= 50 chars), a blank line, a body wrapped at 72 columns, and an
    // optional "Fix #<issue>" trailer. Returns the path to a temp message file.
    private static string WriteCommitMessage(FleetOptions o)
    {
        var summary = o.CommitMessage ?? "Migrate project system to SDK-style";
        if (summary.Length > 50) summary = summary[..50].TrimEnd();

        var body = Wrap(
            "Convert the legacy .nfproj project system to an SDK-style MSBuild project: "
          + "drop project-system boilerplate, fold packages.config into PackageReference, "
          + "and fold .nuspec metadata into MSBuild Pack properties. "
          + "No functional code changes.", 72);

        var sb = new StringBuilder();
        sb.Append(summary).Append("\n\n").Append(body).Append('\n');
        if (o.Issue is not null) sb.Append("\nFix #").Append(o.Issue).Append('\n');

        var path = Path.GetTempFileName();
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    private static string Wrap(string text, int width)
    {
        var sb = new StringBuilder();
        int lineLen = 0;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (lineLen > 0 && lineLen + 1 + word.Length > width) { sb.Append('\n'); lineLen = 0; }
            else if (lineLen > 0) { sb.Append(' '); lineLen++; }
            sb.Append(word); lineLen += word.Length;
        }
        return sb.ToString();
    }
}
