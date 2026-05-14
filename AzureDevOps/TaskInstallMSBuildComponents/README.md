## Azure Pipelines Tool installer

This is the project for an Azure DevOps extension. The task installs the nanoFramework MSBuild components required to build an _nfproj_ in an Azure Pipeline.

The file structure follows the basic Azure DevOps extension.

Required components to build/pack the installer:
- [VstsTaskSdk](https://github.com/Microsoft/azure-pipelines-task-lib/tree/master/powershell) inside TaskFile\ps_modules\VstsTaskSdk
- [7zip](https://www.npmjs.com/package/7zip) inside TaskFiles\7zip

Steps to package/publish the task in the "Add a build or release task" [tutorial](https://docs.microsoft.com/en-us/azure/devops/extend/develop/add-build-task?view=vsts#packageext).

## Pack and publish

`tfx extension publish --publisher nanoframework --manifest-globs vss-extension.json --no-wait-validation`

### Automated Publishing

The extension is automatically published to the Azure DevOps Marketplace when changes are pushed to the main branch. This is managed by the GitHub Actions workflow `.github/workflows/publish-msbuild-task.yml`.

#### Setting up Automated Publishing

To enable automated publishing, you need to configure a GitHub secret with an Azure DevOps Personal Access Token (PAT).

**GitHub Secret Configuration:**

1. Navigate to your repository settings: `Settings → Secrets and variables → Actions`
2. Click "New repository secret"
3. Create a secret named: `AZDO_MARKETPLACE_PAT`
4. Paste the full PAT token value (see below for how to generate)

**Creating an Azure DevOps PAT Token:**

1. Visit: https://dev.azure.com/nanoframework/_usersSettings/tokens
2. Click "New Token"
3. Configure the token:
   - **Name**: `GitHub Actions - Marketplace Publishing` (or similar)
   - **Organization**: nanoframework
   - **Scopes**:
     - ✓ Marketplace (Publish)
     - ✓ Code (Read & Write)
   - **Expiration**: Set to 1 year or longer (recommended)
4. Click "Create"
5. Copy the full token immediately (it won't be shown again)
6. Paste it into the GitHub secret as described above

**Token Permissions Required:**

- **Marketplace: Publish** - Required to publish extensions
- **Code: Read & Write** - Required for Azure DevOps authentication

**Security Best Practices:**

- Keep the PAT token secure and never commit it to the repository
- Rotate the token periodically (recommend annually)
- If the token is compromised, regenerate it immediately at the Azure DevOps settings page
- The token is only accessible during workflow execution via the GitHub secret

**Workflow Details:**

- **Trigger**: Automatically runs when changes are pushed to main branch in the `AzureDevOps/TaskInstallMSBuildComponents/` directory
- **Manual Trigger**: Can also be triggered manually via the "Actions" tab with "workflow_dispatch"
- **Status**: Check the "Actions" tab in your GitHub repository to monitor publishing status

TO-DO:

- improve marketplace stuff (description, links, etc.)
