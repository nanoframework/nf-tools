// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Text;

namespace nanoFramework.Migrate.Core.Verification;

/// <summary>
/// The outcome of verifying one target (a solution or a project) by building it.
/// </summary>
public sealed class BuildOutcome
{
    /// <summary>The solution/project that was built (absolute path).</summary>
    public required string Target { get; init; }

    /// <summary>True when the build succeeded (exit code 0).</summary>
    public bool Succeeded { get; init; }

    /// <summary>
    /// True when the build could not be attempted at all — e.g. the <c>dotnet</c>
    /// CLI is not on PATH. Such a target is reported as <em>skipped</em>, not failed,
    /// and never triggers rollback.
    /// </summary>
    public bool Skipped { get; init; }

    /// <summary>The process exit code (or a synthetic non-zero when skipped/errored).</summary>
    public int ExitCode { get; init; }

    /// <summary>A short tail of the build output, surfaced on failure for diagnosis.</summary>
    public string ErrorTail { get; init; } = "";

    /// <summary>A skipped/setup message (e.g. "dotnet not found on PATH").</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Verifies a migration by building the affected solution(s)/project(s) with the
/// <c>dotnet</c> CLI on PATH. Captures the exit code and a short error tail so the
/// command can render pass/fail and decide whether to offer a rollback.
///
/// The build invocation is isolated behind <see cref="IBuildRunner"/> so the
/// result interpretation (<see cref="Interpret"/>) is unit-testable without a real
/// build. The absence of <c>dotnet</c> is tolerated: the target is marked skipped
/// (a warning), never failed.
/// </summary>
public sealed class SolutionBuilder
{
    /// <summary>How many trailing non-empty output lines to keep as the error tail.</summary>
    public const int ErrorTailLines = 12;

    private readonly IBuildRunner _runner;

    /// <summary>Builds via the real <c>dotnet</c> CLI on PATH.</summary>
    public SolutionBuilder() : this(new DotnetBuildRunner()) { }

    /// <summary>Builds via the supplied runner (used by tests to fake the process).</summary>
    public SolutionBuilder(IBuildRunner runner) => _runner = runner;

    /// <summary>
    /// Builds every target and returns one <see cref="BuildOutcome"/> each. When
    /// <c>dotnet</c> is unavailable, every target is reported skipped and
    /// <paramref name="onSkippedToolMissing"/> (if given) is invoked once.
    /// </summary>
    public List<BuildOutcome> VerifyAll(IEnumerable<string> targets, Action<string>? onProgress = null,
        Action? onSkippedToolMissing = null)
    {
        var results = new List<BuildOutcome>();
        if (!_runner.IsAvailable)
        {
            onSkippedToolMissing?.Invoke();
            foreach (var t in targets)
                results.Add(new BuildOutcome
                {
                    Target = Path.GetFullPath(t),
                    Skipped = true,
                    ExitCode = -1,
                    Message = "dotnet not found on PATH; verification skipped",
                });
            return results;
        }

        foreach (var t in targets)
        {
            var full = Path.GetFullPath(t);
            onProgress?.Invoke(full);
            var (code, stdout, stderr) = _runner.Build(full);
            results.Add(Interpret(full, code, stdout, stderr));
        }
        return results;
    }

    /// <summary>
    /// Turns a raw build result (exit code + output streams) into a
    /// <see cref="BuildOutcome"/>. Pure and side-effect-free, so the pass/fail and
    /// error-tail logic can be unit-tested without spawning a process.
    /// </summary>
    public static BuildOutcome Interpret(string target, int exitCode, string stdout, string stderr)
    {
        var succeeded = exitCode == 0;
        return new BuildOutcome
        {
            Target = Path.GetFullPath(target),
            Succeeded = succeeded,
            ExitCode = exitCode,
            ErrorTail = succeeded ? "" : Tail(stdout, stderr, ErrorTailLines),
        };
    }

    /// <summary>
    /// The last <paramref name="lines"/> non-empty lines of the build output,
    /// preferring lines that look like errors/warnings when there are many. Joined
    /// with newlines; empty when both streams are blank.
    /// </summary>
    public static string Tail(string stdout, string stderr, int lines)
    {
        var all = (stdout ?? "")
            .Replace("\r\n", "\n").Split('\n')
            .Concat((stderr ?? "").Replace("\r\n", "\n").Split('\n'))
            .Select(l => l.TrimEnd())
            .Where(l => l.Length > 0)
            .ToList();
        if (all.Count == 0) return "";

        // Prefer error/warning lines if present; otherwise just the tail.
        var errs = all.Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)
                               || l.Contains(": error", StringComparison.OrdinalIgnoreCase)).ToList();
        var pick = errs.Count > 0 ? errs : all;
        return string.Join("\n", pick.Skip(Math.Max(0, pick.Count - lines)));
    }
}

/// <summary>
/// Abstracts the actual <c>dotnet build</c> invocation so the result interpretation
/// is testable without a real build.
/// </summary>
public interface IBuildRunner
{
    /// <summary>True when a build can be attempted (the <c>dotnet</c> CLI is on PATH).</summary>
    bool IsAvailable { get; }

    /// <summary>Builds <paramref name="target"/>, returning the exit code and output streams.</summary>
    (int exitCode, string stdout, string stderr) Build(string target);
}

/// <summary>The production runner: shells the <c>dotnet</c> already on PATH.</summary>
public sealed class DotnetBuildRunner : IBuildRunner
{
    // Generous cap so a wedged build can't hang the verification step forever.
    private const int BuildTimeoutMs = 600_000;

    private readonly Lazy<bool> _available = new(ProbeDotnet);

    public bool IsAvailable => _available.Value;

    public (int exitCode, string stdout, string stderr) Build(string target)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(target)) ?? ".";
        var psi = new ProcessStartInfo("dotnet", $"build \"{target}\" --nologo")
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try
        {
            using var p = Process.Start(psi)!;

            // Drain both pipes concurrently; sequential ReadToEnd() can deadlock when the
            // build fills one buffer while we block on the other. Named local functions
            // (not lambdas) so the handlers can be detached in the finally.
            var so = new StringBuilder();
            var se = new StringBuilder();
            void OnOutput(object? _, DataReceivedEventArgs e) { if (e.Data is not null) so.AppendLine(e.Data); }
            void OnError(object? _, DataReceivedEventArgs e) { if (e.Data is not null) se.AppendLine(e.Data); }
            p.OutputDataReceived += OnOutput;
            p.ErrorDataReceived += OnError;
            try
            {
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                if (!p.WaitForExit(BuildTimeoutMs))
                {
                    try { p.Kill(entireProcessTree: true); } catch { /* already exited */ }
                    return (-1, so.ToString(), $"build timed out after {BuildTimeoutMs / 1000}s");
                }
                p.WaitForExit(); // let the async output handlers flush
                return (p.ExitCode, so.ToString(), se.ToString());
            }
            finally
            {
                p.OutputDataReceived -= OnOutput;
                p.ErrorDataReceived -= OnError;
            }
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }

    // True when `dotnet --version` runs and exits 0. Cached for the runner's lifetime.
    private static bool ProbeDotnet()
    {
        try
        {
            var psi = new ProcessStartInfo("dotnet", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
