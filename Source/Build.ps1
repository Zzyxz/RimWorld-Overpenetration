$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "CEOverpenetration.csproj"
$repo = Split-Path $PSScriptRoot -Parent
$mod = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\..\..\CEOverpenetration"))

foreach ($version in "1.7", "1.6") {
    Write-Host "Building RimWorld $version ..."
    dotnet build $project -c Release "-p:RimWorldVersion=$version" "-p:BaseIntermediateOutputPath=obj/$version/"
    if ($LASTEXITCODE -ne 0) { throw "Build for RimWorld $version failed" }
}

foreach ($folder in "Languages") {
    robocopy (Join-Path $repo $folder) (Join-Path $mod $folder) /MIR /NJH /NJS /NFL /NDL /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying $folder failed" }
}
robocopy (Join-Path $repo "About") (Join-Path $mod "About") /E /NJH /NJS /NFL /NDL /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copying About failed" }
Copy-Item (Join-Path $repo "LoadFolders.xml") (Join-Path $mod "LoadFolders.xml") -Force
$global:LASTEXITCODE = 0
Write-Host "Mod files copied to $mod"
