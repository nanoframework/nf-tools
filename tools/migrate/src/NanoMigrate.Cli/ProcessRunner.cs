// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Text;

namespace nanoFramework.Migrate.Cli;

/// <summary>
/// Runs external processes. Also implements <see cref="IGitRunner"/> so Core's
/// fleet orchestration can issue git commands without depending on
/// <see cref="Process"/> itself.
/// </summary>
internal sealed class ProcessRunner : IGitRunner
{
    public GitResult Run(string args, string workingDirectory)
    {
        var (code, so, se) = Run("git", args, workingDirectory);
        return new GitResult(code, so, se);
    }

    // Generous per-process cap so a hung child (e.g. git waiting on a credential prompt)
    // can't block the whole run indefinitely.
    private const int TimeoutMs = 600_000;

    public static (int code, string stdout, string stderr) Run(string file, string args, string cwd)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;

        // Drain both pipes concurrently — reading them sequentially with ReadToEnd()
        // can deadlock when a child fills one buffer while we block on the other.
        // Named local functions (not lambdas) so the handlers can be detached in the
        // finally, leaving no reference from the Process to the captured builders.
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

            if (!p.WaitForExit(TimeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* already exited */ }
                return (-1, so.ToString(), $"process timed out after {TimeoutMs / 1000}s: {file} {args}");
            }
            p.WaitForExit(); // ensure the async output handlers have flushed
            return (p.ExitCode, so.ToString(), se.ToString());
        }
        finally
        {
            p.OutputDataReceived -= OnOutput;
            p.ErrorDataReceived -= OnError;
        }
    }
}
