// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using nanoFramework.Tool.Cli;
using nanoFramework.Tool.Commands;
using Xunit;

namespace nanoFramework.Tool.Tests;

public class AuthModeTests
{
    private static readonly TypeConverter Converter = new SmartEnumTypeConverter<AuthMode>();

    [Theory]
    [InlineData("WPA2")]
    [InlineData("wpa2")]
    [InlineData(" WPA2 ")]
    public void Converter_binds_known_value_case_insensitively(string input)
    {
        // SmartEnum members are singletons, so the same instance comes back.
        Assert.Same(AuthMode.Wpa2, Converter.ConvertFrom(input));
    }

    [Theory]
    [InlineData("OPEN", false)]
    [InlineData("WPA", true)]
    [InlineData("WPA2", true)]
    public void RequiresPassword_reflects_the_mode(string input, bool requiresPassword)
    {
        var mode = (AuthMode)Converter.ConvertFrom(input)!;
        Assert.Equal(requiresPassword, mode.RequiresPassword);
    }

    [Fact]
    public void Converter_rejects_an_unknown_value_and_lists_the_allowed_ones()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Converter.ConvertFrom("WEP"));
        Assert.Contains("OPEN", ex.Message);
        Assert.Contains("WPA2", ex.Message);
    }
}
