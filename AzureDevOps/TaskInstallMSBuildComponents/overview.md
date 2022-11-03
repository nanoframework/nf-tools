# Overview

This extension installs a task that can be used in Azure Pipelines to install .NET nanoFramework MSBuild components, allowing building .NET nanoFramework Solutions/Projects in Azure Pipelines.

The extension takes care of installing the appropriate components according to the Visual Studio version. Supported Visual Studio versions: 2019 and 2022.

# Usage

To use this AZDO Task simply add the following to the pipeline yaml that's building a .NET nanoFramework solution/project. Please note that this Task has to be installed before it's used by msbuild or `VSBuild` Task.

```yaml
- task: InstallNanoMSBuildComponents@1
  displayName: Install .NET nanoFramework MSBuild components
```
