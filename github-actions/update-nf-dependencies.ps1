# Copyright (c) .NET Foundation and Contributors
# See LICENSE file in the project root for full license information.

# This PS update the .NET nanoFramework dependencies on the repo where it's running

# optional parameters:
# nugetReleaseType:  to request for stable or preview releases to be used when updating NuGets
# targetSolutions: solution (or solutions) to update
param (
    [string]$nugetReleaseType,
    [string]$targetPath,
    [string]$targetSolutions
    )

# install nanodu tool
dotnet tool install -g nanodu --add-source https://pkgs.dev.azure.com/nanoframework/feed/_packaging/sandbox/nuget/v3/index.json

# check if this is running in Azure Pipelines or GitHub actions
if($null -ne $env:NF_Library)
{
    ######################################
    # this is building from Azure Pipelines

    Set-Location "$env:Build_SourcesDirectory\$env:NF_Library" | Out-Null

    $library = $env:NF_Library
}
elseif($env:GITHUB_ACTIONS)
{
    ######################################
    # this is building from github actions

    # get repository name from the repo path
    Set-Location ".." | Out-Null
    $library = Split-Path $(Get-Location) -Leaf
   
    # need this to move to the 
    "Moving to 'main' folder" | Write-Host

    Set-Location "main" | Out-Null
}

# set working path
if ([string]::IsNullOrEmpty($targetPath))
{
    $workingPath = $(Get-Location)
}
else
{
    $workingPath = "$targetPath"
}

if ([string]::IsNullOrEmpty($nugetReleaseType))
{
    if($env:GITHUB_REF -like '*release*' -or $env:GITHUB_REF -like '*master*' -or $env:GITHUB_REF -like '*main*' -or $env:GITHUB_REF -like '*stable*')
    {
        nanodu --stable-packages --working-directory $workingPath --solutions-to-check $library
    }
    else
    {
        nanodu --preview-packages --working-directory $workingPath --solutions-to-check $library
    }
}
else
{
    if($nugetReleaseType -ne 'stable' -and $nugetReleaseType -ne 'prerelease' )
    {
        nanodu --stable-packages --working-directory $workingPath --solutions-to-check $library
    }
}

