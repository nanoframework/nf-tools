## Azure Pipelines Tool installer

This is the project for an Azure DevOps extension. The task installs the nanoFramework MSBuild components required to build an _nfproj_ in an Azure Pipeline.

The file structure follows the basic Azure DevOps extension.

Required components to build/pack the installer:
- [VstsTaskSdk](https://github.com/Microsoft/azure-pipelines-task-lib/tree/master/powershell) inside TaskFile\ps_modules\VstsTaskSdk
- [7zip](https://www.npmjs.com/package/7zip) inside TaskFiles\7zip


TO-DO:
- automate the vsix packaging and versioning 
- improve marketplace stuff (description, links, etc.)