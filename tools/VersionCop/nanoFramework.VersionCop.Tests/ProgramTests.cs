namespace nanoFramework.VersionCop.Tests;

[TestClass]
public class ProgramTests
{
    [TestMethod]
    [DataRow("System.Net.Http", "System.Net.Http.Client", true)]
    [DataRow("nanoFramework.System.Net.Http", "System.Net.Http.Server", true)]
    [DataRow("System.Net.Http", "System.Net.Http", true)]
    [DataRow("System.Net.Http", "System.Net.Http.WebSockets", false)]
    public void NuspecIdMatchesAssemblyName_ReturnsExpectedResult(
        string nuspecId,
        string assemblyName,
        bool expectedResult)
    {
        var result = global::Program.NuspecIdMatchesAssemblyName(nuspecId, assemblyName);

        Assert.AreEqual(expectedResult, result);
    }
}