// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Migrate.Core.Projects;

/// <summary>
/// Converts a single legacy <c>.nfproj</c> into an SDK-style project. The result
/// is returned as data; implementations never write to the console.
/// </summary>
public interface IProjectConverter
{
    /// <summary>
    /// Converts the project at <paramref name="nfprojPath"/> per <paramref name="options"/>.
    /// In dry-run mode nothing is written; the returned <see cref="ConvertResult"/>
    /// still describes exactly what a real run would change.
    /// </summary>
    ConvertResult Convert(string nfprojPath, ConversionOptions options);
}
