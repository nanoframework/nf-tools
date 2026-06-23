// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// dotnet nano deploy — full-image OTA deploy.
//
// Two phases, in order:
//   1. FLASH the CLR firmware (the native image) with esptool, gated by a SHA-256
//      sentinel so an unchanged image is skipped. Driven by a `nanoCLR.deploy.json`
//      descriptor emitted into the build output by the native-build targets.
//   2. DEPLOY the managed `.pe` set over the wire protocol with the same debugger
//      library the VS extension / Test Adapter use (nanoFramework.Tools.Debugger.Net):
//      discover the device, Connect, DeploymentExecute the assemblies, reboot the CLR.
//
// A managed-only app (no native descriptor) simply skips phase 1.

using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using nanoFramework.Tools.Debugger;
using Spectre.Console;
using Spectre.Console.Cli;

namespace nanoFramework.Tool.Commands;

public sealed class DeploySettings : CommandSettings
{
    // NOTE: `-p` is the port here (to match nanoff's `--port`), so `--project` has no
    // short alias to avoid colliding with it.
    [CommandOption("--project <PATH>")]
    [Description("Path to the app .csproj or a directory to search (default: current directory).")]
    public string? Project { get; init; }

    [CommandOption("-p|--port <PORT>")]
    [Description("Serial port of the target device, e.g. COM5. Optional when exactly one device is connected.")]
    public string? Port { get; init; }

    [CommandOption("-c|--configuration <CFG>")]
    [Description("Build configuration to deploy (default: Debug).")]
    public string Configuration { get; init; } = "Debug";

    [CommandOption("--no-flash")]
    [Description("Skip the firmware flash; deploy the managed .pe assemblies only.")]
    public bool NoFlash { get; init; }
}

/// <summary>
/// <c>dotnet nano deploy</c> — flashes the CLR firmware (unless <c>--no-flash</c>),
/// then deploys the built managed <c>.pe</c> set to the device over the wire protocol.
/// Renders clean Spectre <c>error:</c> messages and returns non-zero on failure.
/// </summary>
public sealed class DeployCommand : Command<DeploySettings>
{
    // Bounded waits so the command can never hang on a missing/unresponsive device.
    private const int DeviceEnumerationTimeoutMs = 10_000;
    private const int ConnectTimeoutMs = 5_000;
    private const int RebootWaitAfterFlashMs = 8_000;

    // After a fresh flash the ESP32-S3 USB-JTAG re-enumerates and the first wire-protocol connection can drop
    // mid-transfer ("No reply from nanoDevice"); reconnect from scratch and retry the managed deploy a few
    // times (the VS DeployProvider retries once for the same reason).
    private const int DeployAttempts = 3;
    private const int DeployRetryDelayMs = 3_000;

    protected override int Execute(CommandContext context, DeploySettings settings, CancellationToken cancellationToken)
    {
        try
        {
            // 1. Resolve the build output directory containing the app .pe set.
            var outputDir = ResolveOutputDirectory(settings);
            if (outputDir is null)
            {
                return Error("could not locate the build output directory. "
                    + "Pass --project <App.csproj> (and build it first), or run from the project directory.");
            }

            var peFiles = Directory.GetFiles(outputDir, "*.pe");
            if (peFiles.Length == 0)
            {
                return Error($"no .pe assemblies found in [bold]{Markup.Escape(outputDir)}[/]. Build the project first.");
            }

            AnsiConsole.MarkupLine($"[grey]Output:[/] {Markup.Escape(outputDir)}");
            AnsiConsole.MarkupLine($"[grey]Found[/] {peFiles.Length} [grey].pe assemblies.[/]");

            // 2. FLASH the firmware (unless --no-flash), gated by a fingerprint sentinel.
            if (!settings.NoFlash)
            {
                var flashResult = FlashFirmware(outputDir, settings.Port);
                if (flashResult != 0)
                {
                    return flashResult;
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[grey]--no-flash: skipping firmware flash.[/]");
            }

            // 3. DEPLOY the managed .pe set over the wire protocol.
            return DeployAssemblies(peFiles, settings.Port);
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    // ---- phase 1: firmware flash -------------------------------------------------

    private static int FlashFirmware(string outputDir, string? port)
    {
        // The native build emits nanoCLR.deploy.json somewhere under the output tree.
        var descriptor = Directory
            .EnumerateFiles(outputDir, "nanoCLR.deploy.json", SearchOption.AllDirectories)
            .FirstOrDefault();

        if (descriptor is null)
        {
            AnsiConsole.MarkupLine("[grey]No firmware descriptor (nanoCLR.deploy.json) found — managed-only app, skipping flash.[/]");
            return 0;
        }

        var (bin, esptool, flashAddress) = ParseDeployDescriptor(descriptor);
        if (bin is null || esptool is null || flashAddress is null)
        {
            return Error($"firmware descriptor [bold]{Markup.Escape(descriptor)}[/] is missing bin/esptool/flashAddress.");
        }
        if (!File.Exists(bin))
        {
            return Error($"firmware image not found: [bold]{Markup.Escape(bin)}[/].");
        }
        if (!File.Exists(esptool))
        {
            return Error($"esptool not found: [bold]{Markup.Escape(esptool)}[/].");
        }

        // Fingerprint-gate: skip flashing if the image hasn't changed since last flash.
        var sentinel = descriptor + ".flashed";
        var fingerprint = ComputeFingerprint(bin, flashAddress);
        if (File.Exists(sentinel) && File.ReadAllText(sentinel).Trim() == fingerprint)
        {
            AnsiConsole.MarkupLine($"[grey]Firmware unchanged ([/]{Markup.Escape(Path.GetFileName(bin))}[grey]) — skipping flash.[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[grey]→ flashing[/] [blue]{Markup.Escape(Path.GetFileName(bin))}[/] "
            + $"[grey]@ {Markup.Escape(flashAddress)}[/]");

        var args = new List<string>();
        if (!string.IsNullOrEmpty(port))
        {
            args.Add("--port");
            args.Add(port);
        }
        args.Add("write_flash");
        args.Add(flashAddress);
        args.Add(bin);

        var exitCode = RunStreaming(esptool, args);
        if (exitCode != 0)
        {
            return Error($"esptool exited with code {exitCode}. Firmware flash failed.");
        }

        // Record the fingerprint so an unchanged image is skipped next time.
        File.WriteAllText(sentinel, fingerprint);

        AnsiConsole.MarkupLine($"[green]Firmware flashed.[/] [grey]Waiting {RebootWaitAfterFlashMs / 1000}s for reboot…[/]");
        Thread.Sleep(RebootWaitAfterFlashMs);
        return 0;
    }

    // Tiny regex parse — no JSON dependency for three string fields.
    private static (string? bin, string? esptool, string? flashAddress) ParseDeployDescriptor(string path)
    {
        var json = File.ReadAllText(path);
        string? Field(string name)
        {
            var m = Regex.Match(json, "\"" + name + "\"\\s*:\\s*\"(?<v>[^\"]*)\"");
            return m.Success ? m.Groups["v"].Value : null;
        }
        return (Field("bin"), Field("esptool"), Field("flashAddress"));
    }

    private static string ComputeFingerprint(string binPath, string flashAddress)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(binPath);
        var hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash) + ":" + flashAddress;
    }

    // ---- phase 2: managed .pe deploy over the wire protocol ----------------------

    private static int DeployAssemblies(string[] peFiles, string? port)
    {
        // Build the deploy blob once: each .pe read into a 4-byte-aligned buffer (the same alignment the
        // VS DeployProvider / Test Adapter use).
        var assemblies = new List<byte[]>();
        long total = 0;
        foreach (var pe in peFiles.OrderBy(p => p))
        {
            var raw = File.ReadAllBytes(pe);
            var length = (raw.Length + 3) / 4 * 4;
            var buffer = new byte[length];
            Array.Copy(raw, buffer, raw.Length);
            assemblies.Add(buffer);
            total += length;
            AnsiConsole.MarkupLine($"  [grey]+[/] {Markup.Escape(Path.GetFileNameWithoutExtension(pe))} [grey]({length} bytes)[/]");
        }

        AnsiConsole.MarkupLine($"[grey]Deploying[/] {assemblies.Count} [grey]assemblies ({total} bytes)…[/]");

        // A fresh flash leaves the ESP32-S3 USB-JTAG re-enumerating; the first connection can drop mid-transfer.
        // Reconnect from scratch and retry a few times.
        string lastError = "deployment failed";
        for (int attempt = 1; attempt <= DeployAttempts; attempt++)
        {
            if (attempt > 1)
            {
                AnsiConsole.MarkupLine($"[yellow]Retry {attempt}/{DeployAttempts}[/] [grey](reconnecting…)[/]");
                Thread.Sleep(DeployRetryDelayMs);
            }

            var (ok, error) = TryDeployOnce(assemblies, port);
            if (ok)
            {
                AnsiConsole.MarkupLine("[green]Deploy complete.[/]");
                return 0;
            }
            lastError = error ?? lastError;
        }

        return Error($"{Markup.Escape(lastError)} after {DeployAttempts} attempts. Reboot the device and retry.");
    }

    // One discovery → connect → deploy → reboot attempt. Returns (true, null) on success, (false, message) on
    // any failure (the caller retries). On failure the debug engine is stopped so the serial port is released
    // for the next attempt.
    private static (bool ok, string? error) TryDeployOnce(List<byte[]> assemblies, string? port)
    {
        NanoDeviceBase? device;

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
                return (false, $"couldn't open a nanoDevice on {port}");
            }

            if (serialClient.NanoFrameworkDevices.Count == 0)
            {
                return (false, $"no nanoDevice found on {port}");
            }
            device = serialClient.NanoFrameworkDevices[0];
        }
        else
        {
            // Discover devices and require exactly one (bounded wait — never hangs).
            var serialClient = PortBase.CreateInstanceForSerial(true, 2000);
            AnsiConsole.MarkupLine("[grey]Discovering devices…[/]");

            var deadline = DateTime.UtcNow.AddMilliseconds(DeviceEnumerationTimeoutMs);
            while (!serialClient.IsDevicesEnumerationComplete && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }

            var count = serialClient.NanoFrameworkDevices.Count;
            if (count == 0)
            {
                return (false, "no nanoDevice found");
            }
            if (count > 1)
            {
                var ports = string.Join(", ", serialClient.NanoFrameworkDevices.Select(d => d.ConnectionId));
                return (false, $"{count} devices found ({ports}); pass --port <COMx>");
            }
            device = serialClient.NanoFrameworkDevices[0];
        }

        AnsiConsole.MarkupLine($"[grey]Device:[/] {Markup.Escape(device.Description ?? device.ConnectionId ?? "(unknown)")}");

        if (device.DebugEngine == null)
        {
            device.CreateDebugEngine();
        }

        var engine = device.DebugEngine;
        if (engine == null)
        {
            return (false, "couldn't create a debug engine");
        }

        var success = false;
        try
        {
            if (!engine.Connect(ConnectTimeoutMs, true, true))
            {
                return (false, "couldn't connect to the device");
            }

            var progress = new Progress<string>(m =>
            {
                if (!string.IsNullOrWhiteSpace(m))
                {
                    AnsiConsole.MarkupLine($"[grey]{Markup.Escape(m.TrimEnd())}[/]");
                }
            });

            // DeploymentExecute(assemblies, rebootAfterDeploy: false, performGarbageCollection: false, ...);
            // we reboot the CLR ourselves afterwards (matches the Test Adapter sequence).
            if (!engine.DeploymentExecute(assemblies, false, false, null, progress))
            {
                return (false, "deployment failed");
            }

            AnsiConsole.MarkupLine("[grey]Rebooting CLR…[/]");
            engine.RebootDevice(RebootOptions.ClrOnly, null);
            success = true;
            return (true, null);
        }
        finally
        {
            // Release the port on a failed attempt so the next reconnect can reopen it.
            if (!success)
            {
                try { engine.Stop(); } catch { /* best effort */ }
            }
        }
    }

    // ---- output-directory resolution --------------------------------------------

    private static string? ResolveOutputDirectory(DeploySettings settings)
    {
        var projectArg = settings.Project ?? Directory.GetCurrentDirectory();

        string? csproj = null;
        string searchRoot;

        if (File.Exists(projectArg) && projectArg.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            csproj = Path.GetFullPath(projectArg);
            searchRoot = Path.GetDirectoryName(csproj)!;
        }
        else if (Directory.Exists(projectArg))
        {
            searchRoot = Path.GetFullPath(projectArg);
            // If the directory holds a single .csproj, prefer it for the computed path.
            var found = Directory.GetFiles(searchRoot, "*.csproj");
            if (found.Length == 1)
            {
                csproj = found[0];
            }
        }
        else
        {
            return null;
        }

        // Preferred: the standard bin/<cfg>/<tfm>[/<rid>] layout under the project,
        // picking the folder that actually contains .pe files.
        if (csproj is not null)
        {
            var projectDir = Path.GetDirectoryName(csproj)!;
            var binCfg = Path.Combine(projectDir, "bin", settings.Configuration);
            var fromBin = FindPeDirectory(binCfg);
            if (fromBin is not null)
            {
                return fromBin;
            }
        }

        // Fallback: search the whole tree under the project/dir for a folder with .pe files.
        return FindPeDirectory(searchRoot);
    }

    // Depth-first search for the directory that holds the most .pe files (the app output).
    private static string? FindPeDirectory(string root)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        if (Directory.GetFiles(root, "*.pe").Length > 0)
        {
            return root;
        }

        string? best = null;
        var bestCount = 0;
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            var count = Directory.GetFiles(dir, "*.pe").Length;
            if (count > bestCount)
            {
                bestCount = count;
                best = dir;
            }
        }
        return best;
    }

    // ---- helpers ----------------------------------------------------------------

    // Runs a process, streaming stdout/stderr to the console; returns the exit code.
    private static int RunStreaming(string fileName, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) AnsiConsole.WriteLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) AnsiConsole.WriteLine(e.Data); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit();
        return proc.ExitCode;
    }

    private static int Error(string message)
    {
        AnsiConsole.MarkupLine($"[red]error:[/] {message}");
        return 1;
    }
}
