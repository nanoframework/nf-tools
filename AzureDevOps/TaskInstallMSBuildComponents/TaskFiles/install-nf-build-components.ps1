[CmdletBinding()]
param()

Trace-VstsEnteringInvocation $MyInvocation

Import-Module $PSScriptRoot\ps_modules\VstsTaskSdk

Import-VstsLocStrings "$PSScriptRoot\Task.json"

[System.Net.WebClient]$webClient = New-Object System.Net.WebClient
$webClient.UseDefaultCredentials = $true

function DownloadVsixFile($fileUrl, $downloadFileName)
{
    Write-Debug "Download VSIX file from $fileUrl to $downloadFileName"
    $webClient.DownloadFile($fileUrl,$downloadFileName)
}

# download VS extension
$vsixPath = Join-Path  $($env:Agent_TempDirectory) "nanoFramework.Tools.VS2017.Extension.zip"
# this was the original download URL that provides the last version, but the marketplace is blocking access to it
# "https://marketplace.visualstudio.com/_apis/public/gallery/publishers/vs-publisher-1470366/vsextensions/nanoFrameworkVS2017Extension/0/vspackage
DownloadVsixFile "https://www.myget.org/F/nanoframework-dev/vsix/47973986-ed3c-4b64-ba40-a9da73b44ef7-1.0.1.0.vsix" $vsixPath

# get path to 7zip
$sevenZip = "$PSScriptRoot\7zip\7z.exe"

# unzip extension
Write-Debug "Unzip extension content"
Invoke-VstsTool -FileName $sevenZip -Arguments " x $vsixPath -bd -o$env:Agent_TempDirectory\nf-extension" > $null

# copy build files to msbuild location
Write-Debug "Copy build files to msbuild location"
$msbuildPath = "C:\Program Files (x86)\Microsoft Visual Studio\2017\Enterprise\MSBuild"
Copy-Item -Path "$env:Agent_TempDirectory\nf-extension\`$MSBuild\nanoFramework" -Destination msbuildPath -Recurse

Trace-VstsLeavingInvocation $MyInvocation
