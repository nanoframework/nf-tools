// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// dotnet nano wifi — set a device's WiFi (802.11) configuration over the wire protocol.
//
// Discovers/connects to the device (the same pattern DeployCommand uses), writes a
// Wireless80211ConfigurationProperties block to a config slot via the debugger library
// (nanoFramework.Tools.Debugger.Net), then reboots the CLR so the device reconnects with
// the new credentials. The password is never echoed.

using System.ComponentModel;
using nanoFramework.Tools.Debugger;
using Spectre.Console;
using Spectre.Console.Cli;

namespace nanoFramework.Tool.Commands;

public sealed class WifiSettings : CommandSettings
{
    [CommandOption("--ssid <SSID>")]
    [Description("The WiFi network name (SSID) to connect to.")]
    public string? Ssid { get; init; }

    [CommandOption("--password <PWD>")]
    [Description("The WiFi password. Leave empty for an open network.")]
    public string Password { get; init; } = string.Empty;

    [CommandOption("-p|--port <PORT>")]
    [Description("Serial port of the target device, e.g. COM5. Optional when exactly one device is connected.")]
    public string? Port { get; init; }

    [CommandOption("--id <N>")]
    [Description("Wireless configuration slot to write (default: 0).")]
    public uint Id { get; init; }

    [CommandOption("--no-autoconnect")]
    [Description("Do not auto-connect to the network on boot (default is to auto-connect).")]
    public bool NoAutoConnect { get; init; }

    [CommandOption("--auth <AUTH>")]
    [Description("Authentication: WPA2 (default), WPA, or OPEN.")]
    public string Auth { get; init; } = "WPA2";

    public override Spectre.Console.ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Ssid))
        {
            return Spectre.Console.ValidationResult.Error("--ssid is required.");
        }

        var auth = Auth.Trim().ToUpperInvariant();
        if (auth != "WPA2" && auth != "WPA" && auth != "OPEN")
        {
            return Spectre.Console.ValidationResult.Error("--auth must be one of: WPA2, WPA, OPEN.");
        }

        return Spectre.Console.ValidationResult.Success();
    }
}

/// <summary>
/// <c>dotnet nano wifi</c> — sets the device's WiFi (802.11) configuration over the wire
/// protocol and reboots the CLR so the device reconnects with the new credentials.
/// </summary>
public sealed class WifiCommand : Command<WifiSettings>
{
    // Bounded waits so the command can never hang on a missing/unresponsive device.
    private const int DeviceEnumerationTimeoutMs = 10_000;
    private const int ConnectTimeoutMs = 5_000;

    protected override int Execute(CommandContext context, WifiSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            // Map --auth to the matching authentication + encryption pair.
            var (authentication, encryption) = settings.Auth.Trim().ToUpperInvariant() switch
            {
                "OPEN" => (AuthenticationType.Open, EncryptionType.None),
                "WPA" => (AuthenticationType.WPA, EncryptionType.WPA),
                _ => (AuthenticationType.WPA2, EncryptionType.WPA2),
            };

            // AutoConnect (which also forces Enable) unless the user opted out.
            var options = settings.NoAutoConnect
                ? Wireless80211_ConfigurationOptions.None
                : Wireless80211_ConfigurationOptions.AutoConnect;

            return ConfigureWifi(settings, authentication, encryption, options);
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private static int ConfigureWifi(
        WifiSettings settings,
        AuthenticationType authentication,
        EncryptionType encryption,
        Wireless80211_ConfigurationOptions options)
    {
        var device = ResolveDevice(settings.Port, out var resolveError);
        if (device is null)
        {
            return Error(resolveError ?? "no nanoDevice found");
        }

        AnsiConsole.MarkupLine($"[grey]Device:[/] {Markup.Escape(device.Description ?? device.ConnectionId ?? "(unknown)")}");

        if (device.DebugEngine == null)
        {
            device.CreateDebugEngine();
        }

        var engine = device.DebugEngine;
        if (engine == null)
        {
            return Error("couldn't create a debug engine");
        }

        try
        {
            if (!engine.Connect(ConnectTimeoutMs, true, true))
            {
                return Error("couldn't connect to the device");
            }

            var cfg = new DeviceConfiguration.Wireless80211ConfigurationProperties
            {
                Id = settings.Id,
                Ssid = settings.Ssid!,
                Password = settings.Password,
                Authentication = authentication,
                Encryption = encryption,
                Wireless80211Options = options,
            };

            AnsiConsole.MarkupLine(
                $"[grey]Setting WiFi config slot[/] {settings.Id} [grey]→ SSID[/] [blue]{Markup.Escape(settings.Ssid!)}[/] "
                + $"[grey](password[/] {MaskedPassword(settings.Password)}[grey], auth {Markup.Escape(authentication.ToString())}, "
                + $"{(options == Wireless80211_ConfigurationOptions.None ? "no auto-connect" : "auto-connect")})[/]");

            var result = engine.UpdateDeviceConfiguration(cfg, settings.Id);
            if (result != Engine.UpdateDeviceResult.Sucess)
            {
                return Error($"the device rejected the WiFi configuration ({result}).");
            }

            AnsiConsole.MarkupLine("[green]WiFi configuration written.[/]");

            AnsiConsole.MarkupLine("[grey]Rebooting CLR so the device connects…[/]");
            engine.RebootDevice(RebootOptions.ClrOnly, null);

            AnsiConsole.MarkupLine($"[green]Done.[/] [grey]Device will reconnect to[/] [blue]{Markup.Escape(settings.Ssid!)}[/][grey].[/]");
            return 0;
        }
        finally
        {
            try { engine.Stop(); } catch { /* best effort */ }
        }
    }

    // Discover (or directly open) a single device, mirroring DeployCommand.TryDeployOnce.
    private static NanoDeviceBase? ResolveDevice(string? port, out string? error)
    {
        error = null;

        if (!string.IsNullOrEmpty(port))
        {
            // Target the exact port directly (mirrors the Test Adapter's AddDevice path).
            var serialClient = PortBase.CreateInstanceForSerial(false);
            AnsiConsole.MarkupLine($"[grey]Checking device on[/] [blue]{Markup.Escape(port)}[/]…");
            try
            {
                serialClient.AddDevice(port);
            }
            catch
            {
                error = $"couldn't open a nanoDevice on {port}";
                return null;
            }

            if (serialClient.NanoFrameworkDevices.Count == 0)
            {
                error = $"no nanoDevice found on {port}";
                return null;
            }
            return serialClient.NanoFrameworkDevices[0];
        }

        // Discover devices and require exactly one (bounded wait — never hangs).
        var discoveryClient = PortBase.CreateInstanceForSerial(true, 2000);
        AnsiConsole.MarkupLine("[grey]Discovering devices…[/]");

        var deadline = DateTime.UtcNow.AddMilliseconds(DeviceEnumerationTimeoutMs);
        while (!discoveryClient.IsDevicesEnumerationComplete && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(50);
        }

        var count = discoveryClient.NanoFrameworkDevices.Count;
        if (count == 0)
        {
            error = "no nanoDevice found";
            return null;
        }
        if (count > 1)
        {
            var ports = string.Join(", ", discoveryClient.NanoFrameworkDevices.Select(d => d.ConnectionId));
            error = $"{count} devices found ({ports}); pass --port <COMx>";
            return null;
        }
        return discoveryClient.NanoFrameworkDevices[0];
    }

    // Never echo the password; show its length as a row of dots instead.
    private static string MaskedPassword(string password)
        => string.IsNullOrEmpty(password) ? "[grey](none)[/]" : new string('•', password.Length);

    private static int Error(string message)
    {
        AnsiConsole.MarkupLine($"[red]error:[/] {message}");
        return 1;
    }
}
