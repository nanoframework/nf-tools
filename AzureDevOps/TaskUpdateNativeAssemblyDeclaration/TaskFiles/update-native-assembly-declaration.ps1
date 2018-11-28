[CmdletBinding()]
param()

Trace-VstsEnteringInvocation $MyInvocation

Import-Module $PSScriptRoot\ps_modules\VstsTaskSdk

Import-VstsLocStrings "$PSScriptRoot\Task.json"

# Get the inputs.
[string]$sourceFileName = Get-VstsInput -Name SourceFileName -Require
[string]$classLibName = Get-VstsInput -Name ClassLibName -Require
[string]$gitHubToken = Get-VstsInput -Name GitHubToken -Require
[string]$nuGetVersion = Get-VstsInput -Name NuGetVersion -Require
[string]$assemblyVersion = Get-VstsInput -Name AssemblyVersion -Require

# working directory is agent temp directory
Write-Debug "Changing working directory to $env:Agent_TempDirectory"
cd "$env:Agent_TempDirectory" > $null

#  find assembly declaration
$assemblyDeclarationPath = (Get-ChildItem -Path "$env:Build_SourcesDirectory\*" -Include $sourceFileName -Recurse)

Write-Debug "Found assembly declaration in stubs: $assemblyDeclarationPath"

$filecontent = Get-Content($assemblyDeclarationPath)
$assemblyChecksum  = $filecontent -match '(0x.{8})'
$assemblyChecksum  = $assemblyChecksum -replace "," , ""
$assemblyChecksum  = $assemblyChecksum -replace "    " , ""

Write-Debug "New assembly checksum is: $assemblyChecksum."

# compute authorization header in format "AUTHORIZATION: basic 'encoded token'"
# 'encoded token' is the Base64 of the string "nfbot:personal-token"
$auth = "basic $([System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("nfbot:$gitHubToken"))))"

# init and fetch GitHub repo
Write-Debug "Init and featch nf-interpreter repo"
mkdir nf-interpreter > $null
cd nf-interpreter > $null
git init
git remote add origin https://github.com/nanoframework/nf-interpreter
git config gc.auto 0
git config user.name nfbot
git config user.email nfbot@users.noreply.github.com
git -c http.extraheader="AUTHORIZATION: $auth" fetch --depth 1 origin -q

# new branch name
$newBranch = "develop-nfbot/update-version/$classLibName/$nuGetVersion"

Write-Debug "Update branch name is: $newBranch."

# create branch to perform updates
git checkout develop
git checkout -b $newBranch develop

# replace version in assembly declaration
$newVersion = $assemblyVersion -replace "\." , ", "
$newVersion = "{ $newVersion }"

$versionRegex = "\{\s*\d+\,\s*\d+\,\s*\d+\,\s*\d+\s*}"
$assemblyFiles = (Get-ChildItem -Path ".\*" -Include $sourceFileName -Recurse)

foreach($file in $assemblyFiles)
{
    Write-Debug "Updating source file with assembly declaration: $file."

    # replace checksum
    $filecontent = Get-Content($file)
    attrib $file -r
    $filecontent -replace  "0x.{8}", $assemblyChecksum | Out-File $file -Encoding utf8

    # replace version
    $filecontent = Get-Content($file)
    attrib $file -r
    $filecontent -replace $versionRegex, $newVersion | Out-File $file -Encoding utf8
}
   
# check if anything was changed
$repoStatus = "$(git status --short --porcelain)"

if ($repoStatus -ne "")
{
    Write-Debug "Commiting changes in assembly declaration source files."

    $commitMessage = "Update $classLibName version to $nuGetVersion"

    git add "**/$sourceFileName"
    git commit -m"$commitMessage ***NO_CI***" -m"[version update]"
    
    git -c http.extraheader="AUTHORIZATION: $auth" push --set-upstream origin $newBranch

    $prRequestBody = @{title="$commitMessage";body="$commitMessage`n`nStarted from $env:Build_Repository_Uri/releases/tag/v$nuGetVersion`n`n[version update]";head="$newBranch";base="develop"} | ConvertTo-Json

    # start PR
    Write-Debug "Starting PR with updates."

    $githubApiEndpoint = "https://api.github.com/repos/nanoframework/nf-interpreter/pulls"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

    try 
    {
        $result = Invoke-RestMethod -Method Post -UserAgent [Microsoft.PowerShell.Commands.PSUserAgent]::InternetExplorer -Uri  $githubApiEndpoint -Header @{"Authorization"="$auth"} -ContentType "application/json" -Body $prRequestBody
        'Started PR with version update...' | Write-Host -ForegroundColor White -NoNewline
        'OK' | Write-Host -ForegroundColor Green
    }
    catch 
    {
        $result = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($result)
        $reader.BaseStream.Position = 0
        $reader.DiscardBufferedData()
        $responseBody = $reader.ReadToEnd();

        Write-Host "Error starting PR: $responseBody"

        throw "Error starting PR: $responseBody"
    }
}
else
{
    Write-Host "Nothing to udpate."
}

Trace-VstsLeavingInvocation $MyInvocation
