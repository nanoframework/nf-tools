// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace NanoFramework.Migrate.Cli;

/// <summary>A user-facing error that prints cleanly without a stack trace.</summary>
internal sealed class UserError(string message) : Exception(message);
