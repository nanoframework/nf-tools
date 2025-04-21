# Copyright (c) .NET Foundation and Contributors
# See LICENSE file in the project root for full license information.

# This script updates the secret of the sign tool from the .NET Foundation code signing certificate in all repositories
# in the nanoFramework organization that use it
# Requires a Personal Access Token (PAT) with the "Variable Groups" scope. 

#############################################################################################################################
# For security reasons, it is recommended to use a PAT with the least privileges necessary and that it's deleted after use. #
#############################################################################################################################

Param(
  
    [Parameter(Mandatory=$true)]
    [string]$PAT,
    
    [Parameter(Mandatory=$true)]
    [string]$signSecret
)

$organization = "nanoFramework"

# Create the auth header from the PAT
$base64AuthInfo = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":$($PAT)"))
$headers = @{
    Authorization = "Basic $base64AuthInfo"
    "Content-Type" = "application/json"
}

# Get all projects from the organization
$projectsUrl = "https://dev.azure.com/$organization/_apis/projects?api-version=6.0"
$projectsResponse = Invoke-RestMethod -Method Get -Uri $projectsUrl -Headers $headers
$projects = $projectsResponse.value

foreach ($project in $projects) {
    Write-Output "Processing project: $($project.name)"
    
    # Get all variable groups in the project
    $variableGroupsUrl = "https://dev.azure.com/$organization/$($project.name)/_apis/distributedtask/variablegroups?api-version=6.0-preview.2"
    $variableGroupsResponse = Invoke-RestMethod -Method Get -Uri $variableGroupsUrl -Headers $headers

    # Filter for the variable group named "sign-client-credentials"
    $targetVGs = $variableGroupsResponse.value | Where-Object { $_.name -eq "sign-client-credentials" }
    
    foreach ($vg in $targetVGs) {
        Write-Output "Found variable group '$($vg.name)' (ID: $($vg.id)) in project '$($project.name)'. Updating secret variable."
        
        # Retrieve detailed variable group info
        $vgUrl = "https://dev.azure.com/$organization/$($project.name)/_apis/distributedtask/variablegroups/$($vg.id)?api-version=6.0-preview.2"
        $vgDetails = Invoke-RestMethod -Method Get -Uri $vgUrl -Headers $headers
        
        # Update or add the secret variable "SignClientSecret"
        if ($vgDetails.variables.PSObject.Properties.Name -contains "SignClientSecret") {
            $vgDetails.variables.SignClientSecret.value = $signSecret
            $vgDetails.variables.SignClientSecret.isSecret = $true
        }
        else {
            $vgDetails.variables.SignClientSecret = @{
                value   = $signSecret
                isSecret = $true
            }
        }
        
        # Convert the updated variable group back to JSON (using sufficient depth)
        $body = $vgDetails | ConvertTo-Json -Depth 10
        
        # Update the variable group
        $updateResponse = Invoke-RestMethod -Method Put -Uri $vgUrl -Headers $headers -Body $body
        
        Write-Output "Updated variable group '$($vg.name)' in project '$($project.name)' successfully."
    }
}

Write-Output "Processing complete."
