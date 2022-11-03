# Copyright (c) .NET Foundation and Contributors
# See LICENSE file in the project root for full license information.

[System.Net.WebClient]$webClient = New-Object System.Net.WebClient
$webClient.UseDefaultCredentials = $true

function DownloadVsixFile($fileUrl, $downloadFileName)
{
    Write-Host "Download VSIX file from $fileUrl to $downloadFileName"
    $webClient.DownloadFile($fileUrl,$downloadFileName)
}

$tempDir = $($Env:RUNNER_TEMP)

# Get latest releases of nanoFramework VS extension
[System.Net.WebClient]$webClient = New-Object System.Net.WebClient
$webClient.Headers.Add("User-Agent", "request")
$webClient.Headers.Add("Accept", "application/vnd.github.v3+json")

$releaseList = $webClient.DownloadString('https://api.github.com/repos/nanoframework/nf-Visual-Studio-extension/releases?per_page=100')

if($releaseList -match '\"(?<VS2022_version>v2022\.\d+\.\d+\.\d+)\"')
{
    $vs2022Tag =  $Matches.VS2022_version
}

if($releaseList -match '\"(?<VS2019_version>v2019\.\d+\.\d+\.\d+)\"')
{
    $vs2019Tag =  $Matches.VS2019_version
}

# Find which VS version is installed
$VsWherePath = "${env:PROGRAMFILES(X86)}\Microsoft Visual Studio\Installer\vswhere.exe"

Write-Output "VsWherePath is: $VsWherePath"

$VsInstance = $(&$VSWherePath -latest -property displayName)

Write-Output "Latest VS is: $VsInstance"

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

# Download VS extension
DownloadVsixFile $extensionUrl $vsixPath

# Unzip extension
Write-Host "Unzip VS extension content"
Expand-Archive -LiteralPath $vsixPath -DestinationPath $tempDir\nf-extension\

# copy build files to msbuild location
$VsPath = $(&$VsWherePath -latest -property installationPath)

Write-Debug "Copy build files to msbuild location"

$msbuildPath = $VsPath + "\MSBuild"

Copy-Item -Path "$tempDir\nf-extension\`$MSBuild\nanoFramework" -Destination $msbuildPath -Recurse

Write-Output "Installed VS extension $extensionVersion"
