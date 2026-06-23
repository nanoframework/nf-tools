// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Globalization;
using Ardalis.SmartEnum;

namespace nanoFramework.Tool.Cli;

/// <summary>
/// Binds a fixed-value command option to a <see cref="SmartEnum{TEnum}"/> member at parse
/// time (case-insensitive, by name), so a command receives a typed value that carries its
/// own behaviour instead of a raw string it must re-parse inside Execute. Spectre.Console.Cli
/// honours the standard <see cref="TypeConverter"/> on a settings property; apply with
/// <c>[TypeConverter(typeof(SmartEnumTypeConverter&lt;MyEnum&gt;))]</c>.
/// </summary>
public sealed class SmartEnumTypeConverter<TEnum> : TypeConverter
    where TEnum : SmartEnum<TEnum>
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string s)
        {
            if (SmartEnum<TEnum>.TryFromName(s.Trim(), ignoreCase: true, out var result))
            {
                return result;
            }

            var allowed = string.Join(", ", SmartEnum<TEnum>.List.Select(e => e.Name));
            throw new InvalidOperationException($"'{s}' is not a valid value. Expected one of: {allowed}.");
        }

        return base.ConvertFrom(context, culture, value)!;
    }
}
