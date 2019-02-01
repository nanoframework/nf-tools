## Azure Pipelines Tool installer

This is the project for an Azure DevOps extension. The task updates the native assembly declarations in nf-interpreter following a class lib tagging.

The file structure follows the basic Azure DevOps extension.

Required components to build/pack the installer:
- [VstsTaskSdk](https://github.com/Microsoft/azure-pipelines-task-lib/tree/master/powershell) inside TaskFile\ps_modules\VstsTaskSdk

Steps to package/publish the task in the "Add a build or release task" [tutorial](https://docs.microsoft.com/en-us/azure/devops/extend/develop/add-build-task?view=vsts#packageext).

TO-DO:
- automate the vsix packaging and versioning 
- improve marketplace stuff (description, links, etc.)