// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Spectre.Console.Cli;

namespace nanoFramework.Migrate.Cli.Commands;

/// <summary>
/// Registers the migrate command surface once, so BOTH front ends — the standalone
/// <c>nano-migrate</c> exe and the umbrella <c>nano</c> tool — expose the same verbs:
/// <c>migrate</c> (the conversion, with a positional <c>&lt;path&gt;</c>), plus the
/// sibling <c>clean</c> and <c>rollback</c> commands.
///
/// These are top-level commands rather than subcommands of <c>migrate</c> on purpose:
/// Spectre.Console.Cli (0.55) cannot host a default/branch command that ALSO takes a
/// positional argument (the first token is parsed as a subcommand selector, breaking
/// <c>migrate &lt;path&gt;</c>), and preserving <c>migrate &lt;path&gt;</c> is required.
/// </summary>
public static class MigrateRegistration
{
    /// <summary>
    /// Adds <c>migrate</c>, <c>clean</c> and <c>rollback</c> to <paramref name="config"/>.
    /// <paramref name="migrateDescription"/> is the lead description for the <c>migrate</c>
    /// verb, phrased by the host (<c>nano</c> or <c>nano-migrate</c>).
    /// </summary>
    public static void Add(IConfigurator config, string migrateDescription)
    {
        config.AddCommand<MigrateCommand>("migrate")
            .WithDescription(migrateDescription + " Related: 'clean' and 'rollback'.")
            .WithExample("migrate", "./samples", "--glob", "Beginner/**", "--dry-run");

        config.AddCommand<CleanCommand>("clean")
            .WithDescription("Remove migration leftovers (*.nfproj.bak files and .nanomigrate/ rollback folders) under a path.")
            .WithExample("clean", "./samples", "--yes");

        config.AddCommand<RollbackCommand>("rollback")
            .WithDescription("Revert the last recorded migration under a path (restore originals, delete created projects).")
            .WithExample("rollback", "./samples");
    }
}
