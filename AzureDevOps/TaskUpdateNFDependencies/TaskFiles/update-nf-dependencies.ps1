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

ForEach($library in $librariesToUpdate)
{
    # remove quotes, if any
    $library = $library -replace "'", ""

    "Updating $library" | Write-Host

    # init/reset these
    $updateCount = 0
    $commitMessage = ""
    $prTitle = ""
    $projectPath = ""
    $baseBranch = "develop"
    $newBranchName = "develop-nfbot/update-dependencies/" + [guid]::NewGuid().ToString()
    $workingPath = '.\'

    # working directory is agent temp directory
    Write-Debug "Changing working directory to $env:Agent_TempDirectory"
    Set-Location "$env:Agent_TempDirectory" | Out-Null

    # clone library repo and checkout develop branch
    Write-Debug "Init and featch $library repo"

    
    git clone --depth 1 https://github.com/nanoframework/$library $library
    Set-Location "$library" | Out-Null
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
        # move to source directory
        Set-Location "source" | Out-Null

        # find solution file in repository
        $solutionFile = (Get-ChildItem -Path ".\" -Include "*.sln" -Recurse)

        # find packages.config
        $packagesConfig = (Get-ChildItem -Path ".\" -Include "packages.config" -Recurse)
        
        $baseBranch = "develop"
    }

    Write-Host "Checkout base branch: $baseBranch..."
    git checkout --quiet "$baseBranch" | Out-Null

    foreach ($packageFile in $packagesConfig)
    {
        # load packages.config as XML doc
        [xml]$packagesDoc = Get-Content $packageFile

        $nodes = $packagesDoc.SelectNodes("*").SelectNodes("*")

        $packageList = @(,@())

        Write-Debug "Building package list to update"

        foreach ($node in $nodes)
        {
            # filter out Nerdbank.GitVersioning package
            if($node.id -notlike "Nerdbank.GitVersioning*")
            {
                Write-Debug "Adding $node.id $node.version"

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

                Write-Debug "Updating package $packageName"

                if ($env:Build_SourceBranchName -like '*release*' -or $env:Build_SourceBranchName -like '*master*')
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
        # need this line so nfbot flags the PR appropriately
        $commitMessage += "`n[version update]`n`n"

        # better add this warning line               
        $commitMessage += "### :warning: This is an automated update. Merge only after all tests pass. :warning:`n"

        
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
