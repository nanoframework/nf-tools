// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// nano-migrate — convert legacy nanoFramework .nfproj projects to the SDK-style
// MSBuild project system, one project at a time or across an entire cloned fleet.
//
// SCOPE: project-system migration ONLY. This tool does NOT touch OTA, modular
// firmware packaging, runtimes/{rid}/native layouts, or ABI manifests. It moves
// a repo from the legacy flavored .nfproj format onto an SDK-style project that
// composes over the nanoFramework SDK, folds packages.config into PackageReference,
// and folds .nuspec metadata into MSBuild Pack properties. Nothing more.
//
// The conversion engine lives in NanoMigrate.Core (a console-free library). This
// project is a thin Spectre.Console.Cli front end over it.

using NanoFramework.Migrate.Cli;
using NanoFramework.Migrate.Cli.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

// No arguments: show help and signal misuse (exit 1), matching the original.
if (args.Length == 0)
{
    BuildApp(out _).Run(new[] { "--help" });
    return 1;
}

var app = BuildApp(out _);
try
{
    return app.Run(args);
}
catch (UserError ue)
{
    AnsiConsole.MarkupLine($"[red]error:[/] {Markup.Escape(ue.Message)}");
    return 1;
}
catch (Exception ex)
{
    // Spectre's own parse/validation errors surface here (PropagateExceptions).
    AnsiConsole.MarkupLine($"[red]error:[/] {Markup.Escape(ex.Message)}");
    return 1;
}

static CommandApp BuildApp(out CommandApp app)
{
    var a = new CommandApp();
    a.Configure(config =>
    {
        config.SetApplicationName("nano-migrate");
        // Surface exceptions to Program so UserError maps to exit 1 and parse
        // errors render cleanly without a stack trace.
        config.PropagateExceptions();

        // migrate + clean + rollback, registered once and shared with the umbrella tool.
        MigrateRegistration.Add(config,
            "Convert a .nfproj, or every .nfproj under a directory.");

        config.AddCommand<CloneCommand>("clone")
            .WithDescription("Clone all matching repos from a GitHub org.")
            .WithExample("clone", "./nano-repos", "--token", "$GITHUB_TOKEN");

        config.AddCommand<FleetCommand>("fleet")
            .WithDescription("Migrate every .nfproj across cloned repos; write a report.")
            .WithExample("fleet", "./nano-repos", "--branch", "sdk-migration", "--commit", "--dry-run");
    });
    app = a;
    return a;
}
