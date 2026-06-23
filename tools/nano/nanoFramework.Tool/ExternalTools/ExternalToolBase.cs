// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

namespace nanoFramework.Tool.ExternalTools;

/// <summary>
/// Shared <see cref="IExternalTool"/> behaviour: pin the entry, resolve via
/// <see cref="ExternalToolResolver"/>, and run the resolved executable as a child
/// process (output streamed straight through to the inherited console). Concrete
/// tools only supply their manifest name; <see cref="NanoffTool"/> is the first.
/// </summary>
public abstract class ExternalToolBase : IExternalTool
{
    private readonly ToolManifest _manifest;
    private readonly ExternalToolResolver _resolver;

    protected ExternalToolBase(ToolManifest manifest, ExternalToolResolver resolver)
    {
        _manifest = manifest;
        _resolver = resolver;
    }

    public abstract string Name { get; }

    private ToolManifestEntry Entry =>
        _manifest.Find(Name) ?? new ToolManifestEntry { Name = Name };

    public string Version => Entry.Version;

    public ToolResolution? ResolvePath() => _resolver.Resolve(Entry);

    public int Run(IReadOnlyList<string> args)
    {
        var resolved = ResolvePath()
            ?? throw new ExternalToolNotFoundException(
                Name,
                $"could not resolve '{Name}'"
              + (string.IsNullOrEmpty(Version) ? "" : $" {Version}")
              + ". Looked for a bundled binary (tools/" + Name + "/), an installed tool / PATH, and the user cache.");

        return RunExecutable(resolved.ExecutablePath, args);
    }

    // Runs the executable inheriting stdio so the wrapped tool's own output appears
    // live. Overridable for tests. Returns the process exit code.
    protected virtual int RunExecutable(string executablePath, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)
            ?? throw new ExternalToolNotFoundException(Name, $"failed to start '{executablePath}'.");
        process.WaitForExit();
        return process.ExitCode;
    }
}
