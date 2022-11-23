//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace DependencyUpdater.Tests;

[TestClass]
public class ProgramTests
{
    [DataTestMethod]
    [DataRow("origin	https://github.com/torbacz/nf-tools.git (fetch) origin	https://github.com/torbacz/nf-tools.git (push)", "nf-tools")]
    [DataRow("origin	https://github.com/nanoframework/nf-tools.git (fetch) origin	https://github.com/nanoframework/nf-tools.git (push)", "nf-tools")]
    public void GetLibNameFromGitString_Should_ReturnValidData(string inputData, string expectedResult)
    {
        var result = nanoFramework.Tools.DependencyUpdater.Program.GetRepoNameFromInputString(inputData);
        Assert.IsTrue(result.Success);
        
        var libName = nanoFramework.Tools.DependencyUpdater.Program.GetLibNameFromRegexMatch(result);
        Assert.AreEqual(expectedResult, libName);
    }

    [DataTestMethod]
    [DataRow("origin	https://github.com/torbacz/nf-tools.git (fetch) origin	https://github.com/torbacz/nf-tools.git (push)", "torbacz")]
    [DataRow("origin	https://github.com/nanoframework/nf-tools.git (fetch) origin	https://github.com/nanoframework/nf-tools.git (push)", "nanoframework")]
    [DataRow("origin	https://github.com/nanoframework-test/nf-tools.git (fetch) origin	https://github.com/nanoframework/nf-tools.git (push)", "nanoframework-test")]
    [DataRow("origin	https://github.com/Azure/amqpnetlite.git (fetch) origin	https://github.com/Azure/amqpnetlite.git (push)", "Azure")]
    public void GetRepoOwnerFromInputString_Should_ReturnValidData(string inputData, string expectedResult)
    {
        var result = nanoFramework.Tools.DependencyUpdater.Program.GetRepoOwnerFromUrl(inputData);
        Assert.AreEqual(expectedResult, result);
    }

    [DataTestMethod]
    [DataRow("--depth 1", "TestOwner", "TestLib", false, "", "clone --depth 1 https://github.com/TestOwner/TestLib TestLib")]
    [DataRow("--depth 1", "TestOwner", "TestLib", true, "TestPat", "clone --depth 1 https://TestPat@github.com/TestOwner/TestLib TestLib")]
    public void CreateCloneCommand_Should_ReturnValidData(string cloneDepth, string repoOwner, string library,
        bool usePatForClone, string gitHubAuth, string expectedResult)
    {
        var result =
            nanoFramework.Tools.DependencyUpdater.Program.CreateCloneCommand(cloneDepth, repoOwner, library,
                usePatForClone, gitHubAuth);
    }
}
