param(
    [string] $IdentityRepository = (Join-Path $PSScriptRoot "..\..\identity")
)

$source = Join-Path $IdentityRepository "artifacts\packages"
$destination = Join-Path $PSScriptRoot "..\packages\local"

if (-not (Test-Path -LiteralPath $source)) {
    throw "Skopka.Identity packages were not found at '$source'. Pack Skopka.Identity first."
}

New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath (Get-ChildItem -LiteralPath $source -Filter "*.nupkg" -File).FullName `
    -Destination $destination -Force

Write-Host "Copied Skopka.Identity packages to '$destination'."
