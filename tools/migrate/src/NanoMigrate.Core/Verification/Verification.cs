// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace NanoFramework.Migrate.Core.Verification;

/// <summary>What the command should do after the verification build(s) ran.</summary>
public enum VerifyOutcome
{
    /// <summary>Verification was not run (dry-run, --no-verify, or no targets).</summary>
    NotRun,

    /// <summary>Every attempted target built (some may have been skipped).</summary>
    Passed,

    /// <summary>At least one target failed to build — a rollback should be considered.</summary>
    Failed,
}

/// <summary>
/// The pure decision logic that turns a set of <see cref="BuildOutcome"/> into a
/// verify outcome and, when it failed, into the rollback action the command should
/// take. Kept free of console/prompts so the failed-verify → rollback decision path
/// is unit-testable: the command renders/prompts around these answers.
/// </summary>
public static class Verification
{
    /// <summary>
    /// The outcome of a verification pass. A failed target (built and returned
    /// non-zero) means <see cref="VerifyOutcome.Failed"/>; targets that were only
    /// skipped (no <c>dotnet</c>) never count as failures.
    /// </summary>
    public static VerifyOutcome Evaluate(IReadOnlyList<BuildOutcome> outcomes)
    {
        if (outcomes.Count == 0) return VerifyOutcome.NotRun;
        if (outcomes.All(o => o.Skipped)) return VerifyOutcome.NotRun;
        return outcomes.Any(o => !o.Succeeded && !o.Skipped)
            ? VerifyOutcome.Failed
            : VerifyOutcome.Passed;
    }

    /// <summary>The number of targets that actually failed to build.</summary>
    public static int FailedCount(IReadOnlyList<BuildOutcome> outcomes) =>
        outcomes.Count(o => !o.Succeeded && !o.Skipped);

    /// <summary>
    /// The rollback decision after a verification pass, given whether the session is
    /// interactive and (for the interactive case) the user's yes/no answer supplied
    /// as <paramref name="userSaidYes"/>. This is the pure policy:
    /// <list type="bullet">
    /// <item>verify did not fail → <see cref="RollbackDecision.None"/>;</item>
    /// <item>failed + interactive + user said yes → <see cref="RollbackDecision.RollBack"/>;</item>
    /// <item>failed + interactive + user said no → <see cref="RollbackDecision.KeepInteractive"/>;</item>
    /// <item>failed + non-interactive → <see cref="RollbackDecision.KeepNonInteractive"/>
    ///       (never auto-roll-back silently; tell the user to run <c>rollback</c>).</item>
    /// </list>
    /// </summary>
    public static RollbackDecision Decide(VerifyOutcome outcome, bool interactive, bool? userSaidYes)
    {
        if (outcome != VerifyOutcome.Failed) return RollbackDecision.None;
        if (!interactive) return RollbackDecision.KeepNonInteractive;
        return userSaidYes == true ? RollbackDecision.RollBack : RollbackDecision.KeepInteractive;
    }
}

/// <summary>What the command does about rollback after a verification.</summary>
public enum RollbackDecision
{
    /// <summary>No rollback needed (verification passed or did not run).</summary>
    None,

    /// <summary>Roll back: restore originals and delete created files.</summary>
    RollBack,

    /// <summary>Verification failed but the user (interactively) chose to keep the changes.</summary>
    KeepInteractive,

    /// <summary>Verification failed in a non-interactive run: keep changes, exit non-zero, advise rollback.</summary>
    KeepNonInteractive,
}
