// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tool.ExternalTools;

/// <summary>
/// Wraps the prebuilt <c>nanoff</c> firmware flasher. Not rebuilt here — resolved
/// (bundled / installed / cached) and run with mapped args by the <c>flash</c> command.
/// </summary>
public sealed class NanoffTool(ToolManifest manifest, ExternalToolResolver resolver)
    : ExternalToolBase(manifest, resolver)
{
    public const string ToolName = "nanoff";

    public override string Name => ToolName;
}
