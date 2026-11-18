# Overview

This extension installs a task that can be used in Azure Pipelines to install .NET nanoFramework MSBuild components, allowing building .NET nanoFramework Solutions/Projects in Azure Pipelines.

The extension installs the appropriate components for Visual Studio 2019, 2022, and 2026. Visual Studio 2026 uses the VS2022 extension.

## Usage

To use this AZDO Task simply add the following to the pipeline yaml that's building a .NET nanoFramework solution/project. Please note that this Task has to be installed before it's used by msbuild or `VSBuild` Task.

An optional `GitHubToken` parameter accepts a GitHub personal access token for authenticated GitHub API requests. This can be useful when the task needs authenticated requests or to keep API usage rate limits under control.

The optional `usePreview` boolean parameter installs the preview version of the Visual Studio extension from Open VSIX Gallery.

```yaml
- task: InstallNanoMSBuildComponents@1
  displayName: Install .NET nanoFramework MSBuild components
  inputs:
    GitHubToken: '$(SECRET.TOKEN)'
```
