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

Write-Debug "VsWherePath is: $VsWherePath"

$VsInstance = $(&$VSWherePath -latest -property displayName)

Write-Output "VS is: $VsInstance"

# handle preview version
if($isPreview -eq $true)
{
    # get extension information from Open VSIX Gallery feed
    $vsixFeedXml = Join-Path $($env:Agent_TempDirectory) "vs-extension-feed.xml"
    Invoke-WebRequest -Uri "https://www.vsixgallery.com/feed/author/nanoframework" -OutFile $vsixFeedXml
    [xml]$feedDetails = Get-Content $vsixFeedXml

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
            Write-Debug "VS2022 extension with ID $vs2022Id not found."
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
            Write-Debug "VS2019 extension with ID $vs2019Id not found."
        }
    }
}
else
{
    # Build headers for the request
    $headers = @{
        "User-Agent"    = "request"
        "Accept"        = "application/vnd.github.v3+json"
        "ContentType"   = "application/json"
    }

    if($gitHubToken)
    {
        Write-Debug "Adding authentication header"
        $headers["Authorization"] = "Bearer $gitHubToken"
    }

    Write-Debug "Get latest releases of nanoFramework VS extension from GitHub"

    # Use Invoke-WebRequest to get the release JSON
    $response = Invoke-WebRequest -Uri 'https://api.github.com/repos/nanoframework/nf-Visual-Studio-extension/releases?per_page=10' -Headers $headers

    # Convert the response content to JSON objects
    $releases = $response.Content | ConvertFrom-Json

    # Filter out draft releases
    $finalReleases = $releases | Where-Object { -not $_.draft }

    # Extract tags using the tag_name property
    $vs2022Release = $finalReleases | Where-Object { $_.tag_name -match '^v2022\.\d+\.\d+\.\d+$' } | Select-Object -First 1
    if($vs2022Release)
    {
        $vs2022Tag = $vs2022Release.tag_name
    }

    $vs2019Release = $finalReleases | Where-Object { $_.tag_name -match '^v2019\.\d+\.\d+\.\d+$' } | Select-Object -First 1
    if($vs2019Release)
    {
        $vs2019Tag = $vs2019Release.tag_name
    }

    # Get extension details according to VS version, starting from VS2022 down to VS2019
    if($vsInstance.Contains('2022'))
    {
        $extensionUrl = "https://github.com/nanoframework/nf-Visual-Studio-extension/releases/download/$vs2022Tag/nanoFramework.Tools.VS2022.Extension.vsix"
        $vsixPath = Join-Path $tempDir "nanoFramework.Tools.VS2022.Extension.zip"
        $extensionVersion = $vs2022Tag
    }
    elseif($vsInstance.Contains('2019'))
    {
        $extensionUrl = "https://github.com/nanoframework/nf-Visual-Studio-extension/releases/download/$vs2019Tag/nanoFramework.Tools.VS2019.Extension.vsix"
        $vsixPath = Join-Path $tempDir "nanoFramework.Tools.VS2019.Extension.zip"
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
