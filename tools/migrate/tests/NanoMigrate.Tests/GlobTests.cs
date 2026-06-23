// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace nanoFramework.Migrate.Tests;

public class GlobTests
{
    [Theory]
    // single star: stays within a path segment
    [InlineData("Foo.nfproj", "*.nfproj", true)]
    [InlineData("Foo.csproj", "*.nfproj", false)]
    [InlineData("sub/Foo.nfproj", "*.nfproj", false)]      // '*' does not cross a separator
    [InlineData("sub/Foo.nfproj", "*/*.nfproj", true)]
    // double star: spans directories
    [InlineData("a/b/c/Foo.nfproj", "**/*.nfproj", true)]
    [InlineData("Foo.nfproj", "**/*.nfproj", true)]        // ** can match zero directories
    [InlineData("Beginner/x/y.nfproj", "Beginner/**", true)]
    [InlineData("Other/x/y.nfproj", "Beginner/**", false)]
    // the "Beginner/** matches Beginner itself" rule
    [InlineData("Beginner", "Beginner/**", true)]
    // single char wildcard
    [InlineData("Foo1.nfproj", "Foo?.nfproj", true)]
    [InlineData("Foo12.nfproj", "Foo?.nfproj", false)]     // '?' is exactly one char
    [InlineData("Foo/.nfproj", "Foo?.nfproj", false)]      // '?' does not match a separator
    // separator-insensitive
    [InlineData("a\\b\\Foo.nfproj", "a/b/*.nfproj", true)]
    // case-insensitive
    [InlineData("FOO.NFPROJ", "*.nfproj", true)]
    public void IsMatch(string path, string pattern, bool expected)
    {
        Assert.Equal(expected, Glob.IsMatch(path, pattern));
    }
}
