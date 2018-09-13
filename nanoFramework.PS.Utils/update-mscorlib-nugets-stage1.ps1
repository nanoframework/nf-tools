# Initial step of a mscorlib update
# After this one is completed and all Nuget packages available on feed, run the script for the second step

[CmdletBinding()]
param (
    [Parameter(Mandatory)]
    [string]$previousCorlibVersion,
    [Parameter(Mandatory)]
    [string]$newCorlibVersion
)

PowerShellGet\Install-Module posh-git -Scope CurrentUser -Force 

# all managed class libraries that depend on mscorlib ONLY
# the order is set to make the ones required for next step available first

$reposToUpdate = ("lib-nanoFramework.Runtime.Events",
"lib-nanoFramework.Runtime.Native",
"lib-Windows.Storage.Streams",
"lib-Windows.Devices.Adc",
"lib-Windows.Devices.I2c",
"lib-Windows.Devices.Pwm",
"lib-Windows.Devices.Spi",
"lib-nanoFramework.Networking.Sntp"
)

foreach($repo in $reposToUpdate)
{
    # clone repo (only a shallow clone with last commit)
    git clone https://github.com/nanoframework/$repo -b develop --depth 1 -q
    cd $repo
    git checkout -b update_nugets develop -q

    # find files to update
    $filesToUpdateCollection = (Get-ChildItem -Include *.nfproj, *.nuproj, packages.config -Recurse)

    # perform the replace of the Nuget version on all files
    foreach($file in $filesToUpdateCollection)
    {
        $filecontent = Get-Content($file)
        attrib $file -r
        $filecontent -replace $previousCorlibVersion, $newCorlibVersion | Out-File $file -Encoding utf8
    }

    # commit changes
    git add -A 2>&1
    git commit -m "Update mscorlib Nuget to $newCorlibVersion" -q
    git push --set-upstream origin update_nugets
    git push origin -q

    # move back to home folder
    cd ..
}
