# Copyright (c) .NET Foundation and Contributors
# See LICENSE file in the project root for full license information.

# This PS1 compares the versions of the NuGets in the packages.config against 
# the respective nfproj file.

# parameter (mandatory):
# SolutionToCheck: solution to check
# WorkingDirectory: working location
# NuspecFile: name of the nuspec file to check
param (
    [Parameter(Mandatory=$true)]
    [string]$SolutionToCheck,
    [string]$WorkingDirectory,
    [string]$NuspecFile
)

if("$WorkingDirectory" -ne $null)
{
    Set-Location "$WorkingDirectory" | Out-Null
}

"Reading content of '$SolutionToCheck'" | Write-Host

# the exception here is mscorlib which doesn't have references to anything
if($SolutionToCheck.Contains("nanoFramework.CoreLibrary.sln"))
{
    "This is mscorlib, skipping this check!"
    $global:LASTEXITCODE = 0
    exit 0
}

$slnFileContent = Get-Content "$SolutionToCheck" -Encoding utf8
$hasnfproj = $slnFileContent | Where-Object {$_ -like '*.nfproj*'}

if($null -eq $hasnfproj)
{
    "ERROR: Solution doesn't have any nfproj file??" | Write-Host
    $global:LASTEXITCODE = 1
    exit 1
}

if(-not [string]::IsNullOrEmpty($NuspecFile))
{
    [xml]$nuspecDoc = Get-Content "$NuspecFile" -Encoding UTF8
    $nuspecNodes = $nuspecDoc.SelectNodes("*").SelectNodes("*")

    # set flag
    $nuspecChecked = $false
}
else
{
    "INFO: No nuspec file in the build" | Write-Host
}

# find ALL packages.config files in the solution projects
$packagesConfigs = (Get-ChildItem -Include 'packages.config' -Recurse)

"Found $($packagesConfigs.Count) packages.config..." | Write-Host

foreach ($packagesConfig in $packagesConfigs)
{
    "INFO: working file is $packagesConfig" | Write-Host

    # check if this project is in our solution file
    $pathOfProject = Split-Path -Path $packagesConfig -Parent
    $projectPathInSln = $pathOfProject.Replace("$($(Get-Location).Path)","")

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

    $isProjecInSolution = $slnFileContent | Where-Object {$_.ToString() -match "(?>"", ""$projectPathInSln)(?'project'[a-zA-Z0-9_.]+\.nfproj)("")"}
    if($null -eq $isProjecInSolution)
    {
        Write-Host "Couldn't find a projet matching this packages.config. Skipping."
        continue
    }

    "Checking '"+$Matches['project']+"' project file..." | Write-Host

    $projectToCheck = "$pathOfProject\"+$Matches['project']
    
    "Reading packages.config for '" + $projectToCheck.Name + "'" | Write-Host

    # reset flag
    $checkNuspec = $true

    # load packages.config as XML doc
    [xml]$packagesDoc = Get-Content "$(Split-Path -Path $projectToCheck -Parent)\packages.config" -Encoding utf8

    $nodes = $packagesDoc.SelectNodes("*").SelectNodes("*")

    $packageList = @(,@())

    "" | Write-Host
    "Building package list..." | Write-Host

    foreach ($node in $nodes)
    {
        # filter out Nerdbank.GitVersioning package
        # and also development dependencies
        if(($node.id -notlike "Nerdbank.GitVersioning*") -And !$node.developmentDependency)
        {
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
        "" | Write-Host
        "NuGet packages to check:" | Write-Host
        $packageList | Write-Host

        "--- end of list ---" | Write-Host
        "" | Write-Host

        # get content of nfproj file
        $projecFileContent = Get-Content $projectToCheck -Encoding utf8

        # get assembly name from project file
        $nameRegex = [regex]::new("(?>\<AssemblyName\>)(?'assemblyname'[a-zA-Z0-9_.]+)|(?>\<\/AssemblyName\>)")
        $projectMatches = $nameRegex.Match($projecFileContent) 
        $assemblyName = $projectMatches.Groups['assemblyname'].value

        "INFO: AssemblyName is $assemblyName" | Write-Host

        # reset flags
        $projectCheckFailed = $false
        $nuspecCheckFailed = $false
        $checkNuspec = $true

        "" | Write-Host
        ">>>>>>>>>>>>>>>>>>>>>>>>" | Write-Host

        # check all packages
        foreach ($package in $packageList)
        {
            # get package name and target version
            [string]$packageName = $package[0]
            [string]$packageVersion = $package[1]

            "Checking $packageName $packageVersion... " | Write-Host -NoNewline

            # find package in project file
            $hasProjectAndVersion = $projecFileContent | Where-Object {$_ -like "*packages\$packageName.$packageVersion*"}

            # flag for nuspec missing
            $nuspecPackageMissing = $true

            if($null -ne $nuspecNodes -and $checkNuspec)
            {
                # there is a nuspec, so check it

                foreach ($node in $nuspecNodes)
                {
                    if($node.Name -eq "metadata")
                    {
                        # check if package ID or title matches assembly name
                        $id = $node.ChildNodes | Where-Object Name -eq 'id'
                        $title = $node.ChildNodes | Where-Object Name -eq 'title'

                        if($id.'#text' -ne $assemblyName -and $title.'#text' -ne $assemblyName -and $id.'#text' -ne 'nanoFramework.'+$assemblyName -and $title.'#text' -ne 'nanoFramework.'+$assemblyName)
                        {
                            $checkNuspec = $false
                            break
                        }

                        # nuspec is being checked: set flag
                        $nuspecChecked = $true

                        foreach ($dependency in ($node.ChildNodes | Where-Object Name -eq 'dependencies').dependency)
                        {
                            if($dependency.Attributes["id"].value -eq $packageName -and $dependency.Attributes["version"].value -eq $packageVersion)
                            {
                                $nuspecPackageMissing = $false
                                
                                # done here
                                continue
                            }
                        }
                    }
                }
            }
        
            # project check outcome
            if($null -eq $hasProjectAndVersion)
            {
                "" | Write-Host
                "*****************************************************************" | Write-Host
                "Couldn't find it in $projectToCheck"
                "*****************************************************************" | Write-Host
                "" | Write-Host

                # flag failed check
                $projectCheckFailed = $true
            }
            else
            {
                "nfproj OK! " | Write-Host -NoNewline
            }

            # nuspec check outcome, if any
            if($null -eq $nuspecNodes -or $checkNuspec -eq $false)
            {
                "" | Write-Host
            }
            else
            {
                if($nuspecPackageMissing)
                {
                    "" | Write-Host
                    "*****************************************************************" | Write-Host
                    "Couldn't find it in $NuspecFile" | Write-Host
                    "*****************************************************************" | Write-Host
                    "" | Write-Host

                    # flag failed check
                    $nuspecCheckFailed = $true
                }
                else
                {
                    "nuspec OK!" | Write-Host
                }
            }
        }
        
        "" | Write-Host
        "<<<<<<<<<<<<<<<<<<<<<<<<" | Write-Host

        if($projectCheckFailed -or $nuspecCheckFailed)
        {
            $global:LASTEXITCODE = 1
            exit 1
        }
    }
    else
    {
        "ERROR: Couldn't identify any nuget packages to check for??" | Write-Host
        $global:LASTEXITCODE = 1
        exit 1
    }

}

if(-not $nuspecChecked)
{
    "" | Write-Host
    "********************************************************************" | Write-Host
    "nuspec wasn't checked!! Verify package ID/Title and/or assembly name" | Write-Host
    "********************************************************************" | Write-Host
    "" | Write-Host
    
    $global:LASTEXITCODE = 1
    exit 1
}

"Versions check completed!" | Write-Host

$global:LASTEXITCODE = 0
