// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

namespace NanoFramework.Migrate.Cli;

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
        var so = p.StandardOutput.ReadToEnd();
        var se = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, so, se);
    }
}
