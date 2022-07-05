//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace DependencyUpdater.Tests;

public class ProgramTests
{
    [Theory]
    [InlineData("origin	https://github.com/torbacz/nf-tools.git (fetch) origin	https://github.com/torbacz/nf-tools.git (push)", "nf-tools")]
    [InlineData("origin	https://github.com/nanoframework/nf-tools.git (fetch) origin	https://github.com/nanoframework/nf-tools.git (push)", "nf-tools")]
    public void GetLibNameFromGitString_Should_ReturnValidData(string inputData, string expectedResult)
    {
        var result = nanoFramework.Tools.DependencyUpdater.Program.GetRepoNameFromInputString(inputData);
        Assert.True(result.Success);
        
        var libName = nanoFramework.Tools.DependencyUpdater.Program.GetLibNameFromRegexMatch(result);
        Assert.Equal(expectedResult, libName);
    }
}