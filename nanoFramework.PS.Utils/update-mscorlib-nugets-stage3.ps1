# Third step of a mscorlib update
# Can only run after the first and second steps are completed and all Nuget packages are available

[CmdletBinding()]
param (
    [Parameter(Mandatory)]
    [string]$previous_CorlibVersion,
    [Parameter(Mandatory)]
    [string]$new_CorlibVersion,
    [Parameter(Mandatory)]
    [string]$previous_RuntimeEventsVersion,
    [Parameter(Mandatory)]
    [string]$new_RuntimeEventsVersion,
    [Parameter(Mandatory)]
    [string]$previous_DevicesGpioVersion,
    [Parameter(Mandatory)]
    [string]$new_DevicesGpioVersion
)

PowerShellGet\Install-Module posh-git -Scope CurrentUser -Force 

######################
# nanoFramework.Hardware.Esp32 

# clone repo (only a shallow clone with last commit)
git clone https://github.com/nanoframework/lib-nanoFramework.Hardware.Esp32  -b develop --depth 1 -q
cd lib-nanoFramework.Hardware.Esp32 
git checkout -b update_nugets develop -q

# find files to update
$filesToUpdateCollection = (Get-ChildItem -Include *.nfproj, *.nuproj, packages.config -Recurse)

# perform the replace of the Nuget version on all files
foreach($file in $filesToUpdateCollection)
{
    $filecontent = Get-Content($file)
    attrib $file -r

    # replace mscorlib and RuntimeEvents
    $filecontent -replace $previous_CorlibVersion, $new_CorlibVersion -replace $previous_RuntimeEventsVersion, $new_RuntimeEventsVersion -replace $previous_DevicesGpioVersion, $new_DevicesGpioVersion | Out-File $file -Encoding utf8
}

# commit changes
git add -A 2>&1
git commit -m "Update Nugets" -m "- Update mscorlib Nuget to $new_CorlibVersion" -m "- Update Runtime.Events Nuget to $new_RuntimeEventsVersion" -m "- Update Windows.Devices.Gpio Nuget to $new_DevicesGpioVersion"   -q
git push --set-upstream origin update_nugets
git push origin -q

# move back to home folder
cd ..
