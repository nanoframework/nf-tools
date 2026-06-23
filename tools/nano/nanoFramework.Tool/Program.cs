// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// dotnet nano — the single umbrella CLI for nanoFramework.
//
// One entry point, two kinds of command:
//   • built-in managed commands hosted in-proc over an engine library
//     (migrate → NanoMigrate.Core, reusing the shared MigrateCommand verbatim;
//      deploy / monitor / devices are registered placeholders for now), and
//   • external prebuilt tools wrapped under the same namespace
//     (flash → nanoff, resolved and run via the ExternalTools layer).
//
// This file is a thin Spectre.Console.Cli CommandApp host: it wires the command
// surface, descriptions and examples, surfaces exceptions cleanly, and maps the
// command result to the process exit code.

using NanoFramework.Migrate.Cli.Commands;
using NanoFramework.Tool.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

var app = BuildApp();

// No arguments: show help and signal misuse (exit 1).
if (args.Length == 0)
{
    app.Run(new[] { "--help" });
    return 1;
}

try
{
    return app.Run(args);
}
catch (Exception ex)
{
    // Spectre's parse/validation errors and any uncaught command error surface here
    // (PropagateExceptions) and render cleanly without a stack trace.
    AnsiConsole.MarkupLine($"[red]error:[/] {Markup.Escape(ex.Message)}");
    return 1;
}

static CommandApp BuildApp()
{
    var app = new CommandApp();
    app.Configure(config =>
    {
        config.SetApplicationName("nano");
        // Surface exceptions to the catch above so parse errors render cleanly.
        config.PropagateExceptions();

        // Built-in: migrate (in-proc, over NanoMigrate.Core via the shared command).
        // The shared registration adds `migrate` plus the sibling `clean` and
        // `rollback` commands, so the umbrella `nano` matches `nano-migrate`.
        MigrateRegistration.Add(config,
            "Convert legacy .nfproj projects to the SDK-style project system.");

        // External: flash (wraps the prebuilt nanoff flasher).
        config.AddCommand<FlashCommand>("flash")
            .WithDescription("Flash device firmware (wraps the external nanoff tool).")
            .WithExample("flash", "--target", "ESP32_REV0", "--port", "COM5");

        // Built-in: deploy (full-image OTA — flash the CLR firmware, then push the
        // managed .pe set over the wire protocol via nanoFramework.Tools.Debugger.Net).
        config.AddCommand<DeployCommand>("deploy")
            .WithDescription("Deploy a built nanoFramework app to a device (flashes firmware, then deploys the managed assemblies).")
            .WithExample("deploy", "--project", "App.csproj", "--port", "COM5");

        // Built-in: wifi (set the device's 802.11 configuration over the wire protocol,
        // then reboot the CLR so it reconnects with the new credentials).
        config.AddCommand<WifiCommand>("wifi")
            .WithDescription("Set the device's WiFi (802.11) configuration and reboot.")
            .WithExample("wifi", "--ssid", "MySsid", "--password", "secret", "--port", "COM5");

        // Built-in placeholders (command surface per the design; not yet in the CLI).
        config.AddCommand<MonitorCommand>("monitor")
            .WithDescription("Monitor device output. (not yet implemented)");

        config.AddCommand<DevicesCommand>("devices")
            .WithDescription("List connected nanoFramework devices. (not yet implemented)");
    });
    return app;
}
