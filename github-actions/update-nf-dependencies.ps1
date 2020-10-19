# Copyright (c) .NET Foundation and Contributors
# See LICENSE file in the project root for full license information.

# This PS update the .NET nanoFramework dependencies on the repo where it's running

if($env:NF_Library -ne $null)
{
    ######################################
    # this is building from Azure Pipelines

    Set-Location "$env:Build_SourcesDirectory\$env:NF_Library" | Out-Null

    $library = $env:NF_Library
}
else
{
    ######################################
    # this is building from github actions

    # get repository name from the repo path
    Set-Location ".." | Out-Null
    $library = Split-Path $(Get-Location) -Leaf

    "Repository: '$library'" | Write-Host

    # need this to move to the 
    "Moving to 'main' folder" | Write-Host

    Set-Location "main" | Out-Null
}

# init/reset these
$updateCount = 0
$commitMessage = ""
$prTitle = ""
$newBranchName = "develop-nfbot/update-dependencies/" + [guid]::NewGuid().ToString()
$workingPath = '.\'

# need this to remove definition of redirect stdErr (only on Azure Pipelines image fo VS2019)
$env:GIT_REDIRECT_STDERR = '2>&1'

# setup github stuff
git config --global gc.auto 0
git config --global user.name nfbot
git config --global user.email nanoframework@outlook.com
git config --global core.autocrlf true

# check for special repos that have sources on different location

######################################
# paho.mqtt.m2mqtt 
if ($library -like "paho.mqtt.m2mqtt")
{
    # solution is at root

    # find solution file in repository
    $solutionFile = (Get-ChildItem -Path ".\" -Include "M2Mqtt.nanoFramework.sln" -Recurse)

    # find packages.config
    $packagesConfig = (Get-ChildItem -Path ".\M2Mqtt" -Include "packages.config" -Recurse)

}
######################################
# AMQPLite
elseif ($library -like "amqpnetlite")
{
    # solution is at root

    # need to set working path
    $workingPath = '.\nanoFramework', '.\Examples\Device\Device.SmallMemory.nanoFramework', '.\Examples\Device\Device.Thermometer.nanoFramework', '.\Examples\ServiceBus\ServiceBus.EventHub.nanoFramework'

    # find solution file in repository
    $solutionFile = (Get-ChildItem -Path ".\" -Include "amqp-nanoFramework.sln" -Recurse)

    # find packages.config
    $packagesConfig = (Get-ChildItem -Path ".\nanoFramework" -Include "packages.config" -Recurse)

    # CD-CI branch is not 'develop'
    $baseBranch = "cd-nanoframework"

}
########################################
# now all the rest
else 
{
    # find solution file in repository
    $solutionFile = (Get-ChildItem -Path ".\" -Include "*.sln" -Recurse)

    # find packages.config
    $packagesConfig = (Get-ChildItem -Path ".\" -Include "packages.config" -Recurse)
    
    $baseBranch = "develop"
}

foreach ($packageFile in $packagesConfig)
{
    # load packages.config as XML doc
    [xml]$packagesDoc = Get-Content $packageFile

    $nodes = $packagesDoc.SelectNodes("*").SelectNodes("*")

    $packageList = @(,@())

    "Building package list to update" | Write-Host

    foreach ($node in $nodes)
    {
        # filter out Nerdbank.GitVersioning package
        if($node.id -notlike "Nerdbank.GitVersioning*")
        {
            "Adding $node.id $node.version" | Write-Host

            if($packageList)
            {
                $packageList += , ($node.id,  $node.version)
            }
            else
            {
                $packageList = , ($node.id,  $node.version)
            }
        }
    }

    if ($packageList.length -gt 0)
    {
        "NuGet packages to update:" | Write-Host
        $packageList | Write-Host

        # restore NuGet packages, need to do this before anything else
        nuget restore $solutionFile[0] -ConfigFile NuGet.Config

        # rename nfproj files to csproj
        Get-ChildItem -Path $workingPath -Include "*.nfproj" -Recurse |
            Foreach-object {
                $OldName = $_.name; 
                $NewName = $_.name -replace 'nfproj','csproj'; 
                Rename-Item  -Path $_.fullname -Newname $NewName; 
            }

        # update all packages
        foreach ($package in $packageList)
        {
            # get package name and target version
            $packageName = $package[0]
            $packageOriginVersion = $package[1]

            "Updating package $packageName" | Write-Host

            if ('${{ github.ref }}' -like '*release*' -or '${{ github.ref }}' -like '*master*')
            {
                # don't allow prerelease for release and master branches
                nuget update $solutionFile[0].FullName -Id "$packageName" -ConfigFile NuGet.Config
            }
            else
            {
                # allow prerelease for all others
                nuget update $solutionFile[0].FullName -Id "$packageName" -ConfigFile NuGet.Config -PreRelease
            }

            # need to get target version
            # load packages.config as XML doc
            [xml]$packagesDoc = Get-Content $packageFile

            $nodes = $packagesDoc.SelectNodes("*").SelectNodes("*")

            foreach ($node in $nodes)
            {
                # find this package
                if($node.id -match $packageName)
                {
                    $packageTargetVersion = $node.version
                }
            }

            # sanity check
            if($packageTargetVersion -eq $packageOriginVersion)
            {
                "Skip update of $packageName because it has the same version as before: $packageOriginVersion."
            }
            else
            {
                "Bumping $packageName from $packageOriginVersion to $packageTargetVersion." | Write-Host -ForegroundColor Cyan                

                $updateCount = $updateCount + 1;

                #  find csproj(s)
                $projectFiles = (Get-ChildItem -Path ".\" -Include "*.csproj" -Recurse)

                Write-Debug "Updating NFMDP_PE LoadHints"

                # replace NFMDP_PE_LoadHints
                foreach ($project in $projectFiles)
                {
                    $filecontent = Get-Content($project)
                    attrib $project -r
                    $filecontent -replace "($packageName.$packageOriginVersion)", "$packageName.$packageTargetVersion" | Out-File $project -Encoding utf8
                }

                # update nuspec files, if any
                $nuspecFiles = (Get-ChildItem -Path ".\" -Include "*.nuspec" -Recurse)
                
                Write-Debug "Updating nuspec files"

                foreach ($nuspec in $nuspecFiles)
                {
                    Write-Debug "Nuspec file is " 

                    [xml]$nuspecDoc = Get-Content $nuspec -Encoding UTF8

                    $nodes = $nuspecDoc.SelectNodes("*").SelectNodes("*")

                    foreach ($node in $nodes)
                    {
                        if($node.Name -eq "metadata")
                        {
                            foreach ($metadataItem in $node.ChildNodes)
                            {                          
                                if($metadataItem.Name -eq "dependencies")
                                {
                                    foreach ($dependency in $metadataItem.ChildNodes)
                                    {
                                        if($dependency.Attributes["id"].value -eq $packageName)
                                        {
                                            $dependency.Attributes["version"].value = "$packageTargetVersion"
                                        }
                                    }
                                }
                            }
                        }
                    }

                    $nuspecDoc.Save($nuspec[0].FullName)
                }

                # build commit message
                $commitMessage += "Bumps $packageName from $packageOriginVersion to $packageTargetVersion.`n"
                # build PR title
                $prTitle = "Bumps $packageName from $packageOriginVersion to $packageTargetVersion"

            }

        }

        # rename csproj files back to nfproj
        Get-ChildItem -Path $workingPath -Include "*.csproj" -Recurse |
        Foreach-object {
            $OldName = $_.name; 
            $NewName = $_.name -replace 'csproj','nfproj'; 
            Rename-Item  -Path $_.fullname -Newname $NewName; 
            }

    }
}

if($updateCount -eq 0)
{
    # something went wrong as no package was updated and it should be at least one
    'No packages were updated...' | Write-Host -ForegroundColor Yellow
}
else
{
   
    # fix PR title
    $prTitle = "Update dependencies"

    Write-Host "::set-env name=CREATE_PR::true"
    Write-Host "::set-env name=BRANCH_NAME::$newBranchName"
    Write-Host "::set-env name=PR_MESSAGE::$commitMessage"
    Write-Host "::set-env name=PR_TITLE::$prTitle"
}
