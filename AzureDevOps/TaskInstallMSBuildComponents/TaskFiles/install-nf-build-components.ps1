# Copyright (c) .NET Foundation and Contributors
# See LICENSE file in the project root for full license information.

[CmdletBinding()]
param()

Trace-VstsEnteringInvocation $MyInvocation

Import-Module $PSScriptRoot\ps_modules\VstsTaskSdk

Import-VstsLocStrings "$PSScriptRoot\Task.json"

# Get the inputs2
[string]$gitHubToken = Get-VstsInput -Name GitHubToken
[bool]$isPreview = Get-VstsInput -Name UsePreview -AsBool

function DownloadVsixFile($fileUrl, $downloadFileName)
{
    Write-Debug "Download VSIX file from $fileUrl to $downloadFileName"
    Invoke-WebRequest -Uri $fileUrl -OutFile $downloadFileName
}

$tempDir = $($env:Agent_TempDirectory)

# Find which VS version is installed
$VsWherePath = "${env:PROGRAMFILES(X86)}\Microsoft Visual Studio\Installer\vswhere.exe"

Write-Output "VsWherePath is: $VsWherePath"

$VsInstance = $(&$VSWherePath -latest -property displayName)

Write-Output "Latest VS is: $VsInstance"

# handle preview version
if($isPreview -eq $true)
{
    # get extension information from Open VSIX Gallery feed
    $vsixFeedXml = Join-Path $($env:Agent_TempDirectory) "vs-extension-feed.xml"
    Invoke-WebRequest -Uri "https://www.vsixgallery.com/feed/author/nanoframework" -OutFile $vsixFeedXml
    [xml]$feedDetails = Get-Content $vsixFeedXml

    Write-Output "Host OS is $([System.Environment]::OSVersion.Version)"

    # Define the extension IDs
    $vs2019Id = "455f2be5-bb07-451e-b351-a9faf3018dc9"
    $vs2022Id = "bf694e17-fa5f-4877-9317-6d3664b2689a"

    # feed list VS2019 and VS2022 extensions using the extension ID

    # VS2022
    if($vsInstance.Contains('2022'))
    {
        $vs2022Entry = $feedDetails.feed.entry | Where-Object { $_.Vsix.Id -eq $vs2022Id }

        if($vs2022Entry)
        {
            $extensionUrl = $vs2022Entry.content.src
            $vsixPath = Join-Path $tempDir "nanoFramework.Tools.VS2022.Extension.zip"
            $extensionVersion = $vs2022Entry.Vsix.Version
        }
        else
        {
            Write-Output "VS2022 extension with ID $vs2022Id not found."
        }
    }
    elseif($vsInstance.Contains('2019'))
    {
        $vs2019Entry = $feedDetails.feed.entry | Where-Object { $_.Vsix.Id -eq $vs2019Id }

        if($vs2019Entry)
        {
            $extensionUrl = $vs2019Entry.content.src
            $vsixPath = Join-Path $tempDir "nanoFramework.Tools.VS2019.Extension.zip"
            $extensionVersion = $vs2019Entry.Vsix.Version
        }
        else
        {
            Write-Output "VS2019 extension with ID $vs2019Id not found."
        }
    }
}
else
{
    # Get latest releases of nanoFramework VS extension
    [System.Net.WebClient]$webClient = New-Object System.Net.WebClient
    $webClient.Headers.Add("User-Agent", "request")
    $webClient.Headers.Add("Accept", "application/vnd.github.v3+json")
    $webClient.Headers.Add("ContentType", "application/json")

    if($gitHubToken)
    {
        Write-Output "Adding authentication header"

        # authorization header with github token
        $auth = "Bearer $gitHubToken"

        $webClient.Headers.Add("Authorization", $auth)
    }

    $releaseList = $webClient.DownloadString('https://api.github.com/repos/nanoframework/nf-Visual-Studio-extension/releases?per_page=100')

    if($releaseList -match '\"(?<VS2022_version>v2022\.\d+\.\d+\.\d+)\"')
    {
        $vs2022Tag =  $Matches.VS2022_version
    }

    if($releaseList -match '\"(?<VS2019_version>v2019\.\d+\.\d+\.\d+)\"')
    {
        $vs2019Tag =  $Matches.VS2019_version
    }

    # Get extension details according to VS version, starting from VS2022 down to VS2019
    if($vsInstance.Contains('2022'))
    {
        $extensionUrl = "https://github.com/nanoframework/nf-Visual-Studio-extension/releases/download/$vs2022Tag/nanoFramework.Tools.VS2022.Extension.vsix"
        $vsixPath = Join-Path  $tempDir "nanoFramework.Tools.VS2022.Extension.zip"
        $extensionVersion = $vs2022Tag
    }
    elseif($vsInstance.Contains('2019'))
    {
        $extensionUrl = "https://github.com/nanoframework/nf-Visual-Studio-extension/releases/download/$vs2019Tag/nanoFramework.Tools.VS2019.Extension.vsix"
        $vsixPath = Join-Path  $tempDir "nanoFramework.Tools.VS2019.Extension.zip"
        $extensionVersion = $vs2019Tag
    }
}

# Download VS extension
DownloadVsixFile $extensionUrl $vsixPath

# Unzip extension
Write-Debug "Unzip VS extension content"
Expand-Archive -LiteralPath $vsixPath -DestinationPath $tempDir\nf-extension\

# copy build files to msbuild location
$VsPath = $(&$VsWherePath -latest -property installationPath)

Write-Debug "Copy build files to msbuild location"

$msbuildPath = $VsPath + "\MSBuild"

Copy-Item -Path "$tempDir\nf-extension\`$MSBuild\nanoFramework" -Destination $msbuildPath -Recurse

Write-Output "Installed VS extension $extensionVersion"

Trace-VstsLeavingInvocation $MyInvocation
