//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace DependencyUpdater.Tests;

[TestClass]
public class ProgramTests
{
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
