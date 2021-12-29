[CmdletBinding()]
param()

Trace-VstsEnteringInvocation $MyInvocation

Import-Module $PSScriptRoot\ps_modules\VstsTaskSdk

Import-VstsLocStrings "$PSScriptRoot\Task.json"

# Get the inputs
[string]$repositoriesToUpdate = Get-VstsInput -Name RepositoriesToUpdate -Require
[string]$gitHubToken = Get-VstsInput -Name GitHubToken -Require

Write-Debug "repositories to update:`n$repositoriesToUpdate"

# compute authorization header in format "AUTHORIZATION: basic 'encoded token'"
# 'encoded token' is the Base64 of the string "nfbot:personal-token"
$auth = "basic $([System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("nfbot:$gitHubToken"))))"

# because it can take sometime for the package to become available on the NuGet providers
# need to hang here for 2 minutes (2 * 60)
"Waiting 2 minutes to let package process flow in Azure Artifacts feed..." | Write-Host
Start-Sleep -Seconds 120 

$librariesToUpdate = $repositoriesToUpdate.Split([environment]::NewLine)

$nugetsToSkip = @()

ForEach($library in $librariesToUpdate)
{
    # remove quotes, if any
    $library = $library -replace "'", ""

    "" | Write-Host
    "*******************************" | Write-Host
    "Updating $library" | Write-Host

    # init/reset these
    $updateCount = 0
    $commitMessage = ""
    $prTitle = ""
    $pathOfProject = ""
    $baseBranch = "develop"
    $newBranchName = "nfbot/update-dependencies/" + [guid]::NewGuid().ToString()

    # working directory is agent temp directory
    Write-Debug "Changing working directory to $env:Agent_TempDirectory"
    Set-Location "$env:Agent_TempDirectory" | Out-Null

    # need this to remove definition of redirect stdErr (only on Azure Pipelines image fo VS2019)
    $env:GIT_REDIRECT_STDERR = '2>&1'

    # clone library repo and checkout develop branch
    Write-Debug "Init and fetch $library repo"
    
    git clone --depth 1 https://github.com/nanoframework/$library $library
    
    Set-Location "$library" | Out-Null
    git config --global gc.auto 0
    git config --global user.name nfbot
    git config --global user.email nanoframework@outlook.com
    git config --global core.autocrlf true
   
    # check for special repos that have sources on different location
    
    ######################################
    # AMQPLite
    if ($library -like "amqpnetlite")
    {
        # solution is at root

        # need to set working path
        $workingPath = '.\nanoFramework', '.\Examples\Device\Device.SmallMemory.nanoFramework', '.\Examples\Device\Device.Thermometer.nanoFramework', '.\Examples\ServiceBus\ServiceBus.EventHub.nanoFramework'

        # find solution file in repository
        $solutionFiles = (Get-ChildItem -Path ".\" -Include "amqp-nanoFramework.sln" -Recurse)

        # CD-CI branch is not 'develop'
        $baseBranch = "nanoframework-dev"
    
    }
    ########################################
    # now all the rest
    else 
    {
        # find solution file in repository
        $solutionFiles = (Get-ChildItem -Path ".\" -Include "*.sln" -Recurse)
        
        $baseBranch = "develop"
    }

    Write-Host "Checkout base branch: $baseBranch..."
    git checkout --quiet "$baseBranch" | Out-Null

    foreach ($solutionFile in $solutionFiles)
    {
        Write-Host ""
        Write-Host "************"
        Write-Host "Processing: '$solutionFile'"
    
        # check if there are any nfproj here
        $slnFileContent = Get-Content $solutionFile -Encoding utf8
        $hasnfproj = $slnFileContent | Where-Object {$_ -like '*.nfproj*'}
        if($null -eq $hasnfproj)
        {
            continue
        }

        $solutionPath = Split-Path -Path $solutionFile

        if (![string]::IsNullOrEmpty($nugetConfig))
        {
            nuget restore $solutionFile -ConfigFile $nugetConfig
        }
        else
        {
            nuget restore $solutionFile
        }

        # find ALL packages.config files in the solution projects
        $packagesConfigs = (Get-ChildItem -Path "$solutionPath" -Include "packages.config" -Recurse)

        nuget config -set repositoryPath="$solutionPath\packages"

        foreach ($packagesConfig in $packagesConfigs)
        {
            # check if this project is in our solution file
            $pathOfProject = Split-Path -Path $packagesConfig -Parent
            $projectPathInSln = $pathOfProject.Replace("$solutionPath",'')

            if($projectPathInSln[0] -eq "\")
            {
                # need to remove the leading \
                $projectPathInSln = $projectPathInSln.Substring(1)

                # need to add a trailing \
                $projectPathInSln = $projectPathInSln + "\"
                # need to replace \ for regex 
                $projectPathInSln = $projectPathInSln.Replace('\','\\')
                # need to replace . for regex 
                $projectPathInSln = $projectPathInSln.Replace('.','\.')
            }
    
            $isProjecInSolution = $slnFileContent | Where-Object {$_.ToString() -match "(?>"", ""$projectPathInSln[a-zA-Z0-9_.]+\.nfproj"")"}
            if($null -eq $isProjecInSolution)
            {
                Write-Host "Project '$projectPathInSln' is not in solution. Skipping."
                continue
            }

            # get project(s) at path
            $projectsAtPath = Get-ChildItem -Path $pathOfProject -Include '*.nfproj' -Recurse

            foreach ($projectToUpdate in $projectsAtPath)
            {
                "Updating project $projectToUpdate" | Write-Host

                # load packages.config as XML doc
                [xml]$packagesDoc = Get-Content $packagesConfig -Encoding utf8

                $nodes = $packagesDoc.SelectNodes("*").SelectNodes("*")

                $packageList = @(,@())

                "Building package list to update" | Write-Host

                foreach ($node in $nodes)
                {
                    # filter out Nerdbank.GitVersioning package
                    if($node.id -notlike "Nerdbank.GitVersioning*")
                    {
                        "Adding {0} {1}" -f [string]$node.id,[string]$node.version | Write-Host
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
                    
                    # update all packages
                    foreach ($package in $packageList)
                    {
                        # get package name and target version
                        [string]$packageName = $package[0]
                        [string]$packageOriginVersion = $package[1]

                        # check if this one is to skip
                        if($nugetsToSkip.Contains($packageName))
                        {
                            continue
                        }

                        "Updating package $packageName from $packageOriginVersion" | Write-Host

                        if ($nugetReleaseType -like '*stable*' -and -not $packageName.StartsWith('UnitsNet.'))
                        {
                            # don't allow prerelease for release, main branches and UnitsNet packages
    
                            if (![string]::IsNullOrEmpty($nugetConfig))
                            {
                                nuget restore $packagesConfig -ConfigFile $nugetConfig -SolutionDirectory $solutionFile.DirectoryName
                                nuget update $projectToUpdate.FullName -Id "$packageName" -ConfigFile $nugetConfig -FileConflictAction Overwrite
                            }
                            else
                            {
                                nuget restore $packagesConfig -SolutionDirectory $solutionFile.DirectoryName
                                nuget update $projectToUpdate.FullName -Id "$packageName" -FileConflictAction Overwrite
                            }
    
                        }
                        elseif ($packageName.StartsWith('UnitsNet.'))
                        {
                            # grab latest version from NuGet
                            $unitsNetPackageInfo = nuget search UnitsNet.nanoFramework.ElectricCurrent -Verbosity quiet -Source "https://api.nuget.org/v3/index.json"
                            $unitsNetVersion = $unitsNetPackageInfo[2].Split('|')[1]
    
                            if (![string]::IsNullOrEmpty($nugetConfig))
                            {
                                nuget restore $packagesConfig -ConfigFile $nugetConfig -SolutionDirectory $solutionFile.DirectoryName
                                nuget update $projectToUpdate.FullName -Id "$packageName" -Version $unitsNetVersion -ConfigFile $nugetConfig -FileConflictAction Overwrite
                            }
                            else
                            {
                                nuget restore $packagesConfig -SolutionDirectory $solutionFile.DirectoryName
                                nuget update $projectToUpdate.FullName -Id "$packageName" -Version $unitsNetVersion -FileConflictAction Overwrite
                            }
                        }
                        else
                        {
                            if (![string]::IsNullOrEmpty($nugetConfig))
                            {
                                nuget restore $packagesConfig -ConfigFile $nugetConfig -SolutionDirectory $solutionFile.DirectoryName
                                nuget update $projectToUpdate.FullName -Id "$packageName" -ConfigFile $nugetConfig -PreRelease -FileConflictAction Overwrite
                            }
                            else
                            {
                                nuget restore $packagesConfig -SolutionDirectory $solutionFile.DirectoryName
                                nuget update $projectToUpdate.FullName -Id "$packageName" -PreRelease -FileConflictAction Overwrite
                            }
                        }

                        # need to get target version
                        # load packages.config as XML doc
                        [xml]$packagesDoc = Get-Content $packagesConfig -Encoding utf8

                        $nodes = $packagesDoc.SelectNodes("*").SelectNodes("*")

                        foreach ($node in $nodes)
                        {
                            # find this package
                            if($node.id -eq $packageName)
                            {
                                $packageTargetVersion = $node.version

                                # done here
                                break
                            }
                        }

                        # sanity check
                        if($packageTargetVersion -eq $packageOriginVersion)
                        {
                            "Skip update of $packageName because it has the same version as before: $packageOriginVersion." | Write-Host -ForegroundColor Cyan
                        }
                        elseif ($packageTargetVersion.Contains('alpha'))
                        {
                            "Skip update of $packageName because it's trying to use an alpha version!" | Write-Host -ForegroundColor Red
    
                            # done here
                            throw "Skip update of $packageName because it's trying to use an alpha version!"
                        }
                        else
                        {
                            # if we are updating samples repo, OK to move to next one
                            if($Env:GITHUB_REPOSITORY -eq "nanoframework/Samples")
                            {
                                $updateCount = $updateCount + 1;
                                
                                # build commit message
                                $commitMessage += "Bumps $packageName from $packageOriginVersion to $packageTargetVersion</br>"

                                # done here
                                continue
                            }

                            "Bumping $packageName from $packageOriginVersion to $packageTargetVersion." | Write-Host -ForegroundColor Cyan                

                            # update nuspec file
                            $nuspecFileName = $projectToUpdate.BaseName+".nuspec"
                            $nuspecFile = (Get-ChildItem -Path "$solutionPath" -Include $nuspecFileName -Recurse)
                            
                            if ($null -eq $nuspecFile)
                            {
                                "**********************************************" | Write-Host -ForegroundColor Yellow
                                "INFO: Can't find nuspec file '$nuspecFileName'" | Write-Host -ForegroundColor Yellow
                                "**********************************************" | Write-Host -ForegroundColor Yellow
                            }
                            else
                            {
                                "Trying update on nuspec file: '$nuspecFileName' " | Write-Host
    
                                # reset this
                                $dependencyToUpdate = $true
    
                                [xml]$nuspecDoc = Get-Content $nuspecFile.FullName -Encoding UTF8
    
                                $nodes = $nuspecDoc.SelectNodes("*").SelectNodes("*")
    
                                foreach ($node in $nodes)
                                {
                                    if($node.Name -eq "metadata" -and $dependencyToUpdate)
                                    {
                                        foreach ($metadataItem in $node.ChildNodes)
                                        {                          
                                            if($metadataItem.Name -eq "dependencies" -and $dependencyToUpdate)
                                            {
                                                foreach ($dependency in $metadataItem.ChildNodes)
                                                {
                                                    if($dependency.Attributes["id"].value -eq $packageName)
                                                    {
                                                        "Updating dependency: $packageName to $packageTargetVersion" | Write-Host
                                                        $dependency.Attributes["version"].value = "$packageTargetVersion"
                                                        # reset flag
                                                        $dependencyToUpdate = $false
                                                        #done here
                                                        break
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
    
                                $nuspecDoc.Save($nuspecFile.FullName)
    
                                "Finished updating nuspec file." | Write-Host
    
                                # build commit message
                                $updateMessage = "Bumps $packageName from $packageOriginVersion to $packageTargetVersion</br>";
    
                                if($commitMessage.Contains($updateMessage))
                                {
                                    # already reported
                                }
                                else
                                {
                                    # update message
                                    $commitMessage += $updateMessage
    
                                    # update count
                                    $updateCount = $updateCount + 1;
                                }
                            }
                        }
                    }
                }
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
        "Updated $updateCount package(s)" | Write-Host -ForegroundColor Yellow

        # need this line so nfbot flags the PR appropriately
        $commitMessage += "`n[version update]`n`n"

        # better add this warning line               
        $commitMessage += "### :warning: This is an automated update. :warning:`n"
        
        Write-Debug "Git branch" 

        # create branch to perform updates
        git branch $newBranchName

        Write-Debug "Checkout branch" 
        
        # checkout branch
        git checkout $newBranchName

        Write-Debug "Add changes" 
        
        # commit changes
        git add -A > $null

        # commit message with a different title if one or more dependencies are updated
        if ($updateCount -gt 1)
        {
            Write-Debug "Commit changed file" 

            git commit -m "Update $updateCount NuGet dependencies" -m"$commitMessage" > $null

            # fix PR title
            $prTitle = "Update $updateCount NuGet dependencies"
        }
        else 
        {
            Write-Debug "Commit changed files"

            git commit -m "$prTitle" -m "$commitMessage" > $null
        }

        Write-Debug "Push changes"

        git -c http.extraheader="AUTHORIZATION: $auth" push --set-upstream origin $newBranchName > $null

        # start PR
        # we are hardcoding to the repo base branch (usually 'develop') to have a fixed one
        # this is very important for tags (which don't have branch information)
        # considering that the base branch can be changed at the PR there is no big deal about this 
        $prRequestBody = @{title="$prTitle";body="$commitMessage";head="$newBranchName";base="$baseBranch"} | ConvertTo-Json
        $githubApiEndpoint = "https://api.github.com/repos/nanoframework/$library/pulls"
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

        $headers = @{}
        $headers.Add("Authorization","$auth")
        $headers.Add("Accept","application/vnd.github.symmetra-preview+json")

        try 
        {
            $result = Invoke-RestMethod -Method Post -UserAgent [Microsoft.PowerShell.Commands.PSUserAgent]::InternetExplorer -Uri  $githubApiEndpoint -Header $headers -ContentType "application/json" -Body $prRequestBody
            'Started PR with dependencies update...' | Write-Host -NoNewline
            'OK' | Write-Host -ForegroundColor Green
        }
        catch 
        {
            $result = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($result)
            $reader.BaseStream.Position = 0
            $reader.DiscardBufferedData()
            $responseBody = $reader.ReadToEnd();

            throw "Error starting PR: $responseBody"
        }
    }
}

Trace-VstsLeavingInvocation $MyInvocation
