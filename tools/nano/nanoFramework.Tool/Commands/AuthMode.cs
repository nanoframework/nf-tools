// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Ardalis.SmartEnum;
using nanoFramework.Tools.Debugger;

namespace nanoFramework.Tool.Commands;

/// <summary>
/// The WiFi authentication mode accepted by <c>--auth</c>. A SmartEnum so each value owns
/// its behaviour — whether a pre-shared key is required and the device-side
/// authentication + encryption pair — replacing the string switch that used to live in
/// the command. Bound from the raw option string at parse time by
/// <see cref="nanoFramework.Tool.Cli.SmartEnumTypeConverter{TEnum}"/>.
/// </summary>
public sealed class AuthMode : SmartEnum<AuthMode>
{
    public static readonly AuthMode Open =
        new("OPEN", 0, AuthenticationType.Open, EncryptionType.None, requiresPassword: false);

    public static readonly AuthMode Wpa =
        new("WPA", 1, AuthenticationType.WPA, EncryptionType.WPA, requiresPassword: true);

    public static readonly AuthMode Wpa2 =
        new("WPA2", 2, AuthenticationType.WPA2, EncryptionType.WPA2, requiresPassword: true);

    private AuthMode(string name, int value, AuthenticationType authentication, EncryptionType encryption, bool requiresPassword)
        : base(name, value)
    {
        Authentication = authentication;
        Encryption = encryption;
        RequiresPassword = requiresPassword;
    }

    /// <summary>The device-side authentication type written to the WiFi config.</summary>
    public AuthenticationType Authentication { get; }

    /// <summary>The device-side encryption type written to the WiFi config.</summary>
    public EncryptionType Encryption { get; }

    /// <summary>True when this mode needs a pre-shared key (<c>--password</c>).</summary>
    public bool RequiresPassword { get; }
}
