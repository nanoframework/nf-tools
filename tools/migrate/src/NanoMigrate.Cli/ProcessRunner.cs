// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Migrate.Cli;

/// <summary>
/// Runs external processes. Also implements <see cref="IGitRunner"/> so Core's
/// fleet orchestration can issue git commands without depending on a process API.
/// The spawn/drain/cancel/timeout details live once in <see cref="ProcessExec"/>.
/// </summary>
internal sealed class ProcessRunner : IGitRunner
{
    public GitResult Run(string args, string workingDirectory, CancellationToken cancellationToken = default)
    {
        var (code, so, se) = Run("git", args, workingDirectory, cancellationToken);
        return new GitResult(code, so, se);
    }

    public static (int code, string stdout, string stderr) Run(string file, string args, string cwd, CancellationToken cancellationToken = default) =>
        ProcessExec.Run(file, args, cwd, cancellationToken: cancellationToken);
}
