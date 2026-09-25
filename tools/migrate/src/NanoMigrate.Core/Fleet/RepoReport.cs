// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Migrate.Core.Fleet;

/// <summary>Per-repo outcome accumulated by the fleet command. Pure data.</summary>
public sealed class RepoReport
{
    public required string Name { get; init; }
    public int Projects { get; set; }
    public List<string> Review { get; } = new();
    public bool Committed { get; set; }
    public string? Error { get; set; }
}
