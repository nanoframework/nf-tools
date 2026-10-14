// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Text;

namespace nanoFramework.Migrate.Core.Common;

/// <summary>
/// Runs an external process and returns its exit code plus captured output. One place
/// for the spawn details every caller needs to get right: stdout/stderr are drained
/// concurrently (sequential <c>ReadToEnd()</c> can deadlock when a child fills one
/// buffer while we block on the other), cancellation kills the child so a long
/// git/dotnet invocation stops promptly, and a timeout bounds a wedged process.
/// </summary>
public static class ProcessExec
{
    /// <summary>Generous default cap so a hung child can't block the run indefinitely.</summary>
    public const int DefaultTimeoutMs = 600_000;

    /// <summary>
    /// Starts <paramref name="fileName"/> with <paramref name="arguments"/> in
    /// <paramref name="workingDirectory"/>, returning <c>(exitCode, stdout, stderr)</c>.
    /// On timeout the child is killed and a synthetic <c>-1</c> result with a timeout
    /// message is returned; on cancellation the child is killed and
    /// <see cref="OperationCanceledException"/> is thrown.
    /// </summary>
    public static (int exitCode, string stdout, string stderr) Run(
        string fileName, string arguments, string workingDirectory,
        int timeoutMs = DefaultTimeoutMs, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"failed to start process: {fileName}");
        // Cancellation (Ctrl+C / console close) kills the child so it stops promptly.
        using var reg = cancellationToken.Register(static state =>
        {
            var proc = (Process)state!;
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
        }, p);

        // Named local functions (not lambdas) so the handlers can be detached afterwards,
        // leaving no reference from the Process to the captured builders.
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

            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* already exited */ }
                return (-1, so.ToString(), $"process timed out after {timeoutMs / 1000}s: {fileName} {arguments}");
            }
            p.WaitForExit(); // ensure the async output handlers have flushed
            cancellationToken.ThrowIfCancellationRequested(); // a cancel-kill surfaces as cancellation, not a bad exit code
            return (p.ExitCode, so.ToString(), se.ToString());
        }
        finally
        {
            p.OutputDataReceived -= OnOutput;
            p.ErrorDataReceived -= OnError;
        }
    }
}
