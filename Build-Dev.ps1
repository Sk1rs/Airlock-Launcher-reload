param([string]$Destination = (Join-Path $PSScriptRoot 'dist\Airlock Launcher - Dev'))

$ErrorActionPreference = 'Stop'
$baseArchive = Join-Path $PSScriptRoot 'vendor\AirlockLauncher-4.2.0-win-x64.zip'
$project = Join-Path $PSScriptRoot 'Patcher\Patcher.csproj'
if (Test-Path -LiteralPath $Destination) {
    if ($PSBoundParameters.ContainsKey('Destination')) {
        throw "The destination already exists: $Destination"
    }

    $defaultDestination = $Destination
    $buildNumber = 2
    do {
        $Destination = "$defaultDestination ($buildNumber)"
        $buildNumber++
    } while (Test-Path -LiteralPath $Destination)
}

$originalLauncher = Join-Path $PSScriptRoot '.build\Airlock Launcher'
$originalDll = Join-Path $originalLauncher 'bin_x64\SS14.Launcher.dll'
if (-not (Test-Path -LiteralPath $baseArchive -PathType Leaf)) {
    throw "Base archive is missing: $baseArchive"
}

$archiveHash = (Get-FileHash -LiteralPath $baseArchive -Algorithm SHA256).Hash
$buildStamp = Join-Path $originalLauncher '..\archive.sha256'
$hasCurrentBase = (Test-Path -LiteralPath $buildStamp -PathType Leaf) -and
    (Test-Path -LiteralPath $originalDll -PathType Leaf) -and
    ((Get-Content -LiteralPath $buildStamp -Raw).Trim() -eq $archiveHash)
if (-not $hasCurrentBase) {
    Expand-Archive -LiteralPath $baseArchive -DestinationPath $originalLauncher -Force
    Set-Content -LiteralPath $buildStamp -Value $archiveHash
}

dotnet build $project -c Release -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) {
    throw 'Build failed.'
}

$hooks = Join-Path $PSScriptRoot 'Hooks\bin\Release\net10.0\Airlock.Features.dll'
$patcher = Join-Path $PSScriptRoot 'Patcher\bin\Release\net10.0\Patcher.dll'
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Destination)))
Copy-Item -LiteralPath $originalLauncher -Destination $Destination -Recurse
$binDirectory = Join-Path $Destination 'bin_x64'
$launcherDll = Join-Path $binDirectory 'SS14.Launcher.dll'
$patchedDll = Join-Path $binDirectory 'SS14.Launcher.patched.dll'

dotnet $patcher $originalDll $hooks $patchedDll
if ($LASTEXITCODE -ne 0) {
    throw 'Patching failed.'
}

Copy-Item -LiteralPath $patchedDll -Destination $launcherDll -Force
Remove-Item -LiteralPath $patchedDll
Copy-Item -LiteralPath $hooks -Destination $binDirectory

$depsPath = Join-Path $binDirectory 'SS14.Launcher.deps.json'
$deps = Get-Content -LiteralPath $depsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$target = $deps.targets.($deps.runtimeTarget.name)
$target.'SS14.Launcher/4.2.0'.dependencies | Add-Member -NotePropertyName 'Airlock.Features' -NotePropertyValue '1.0.0'
$target | Add-Member -NotePropertyName 'Airlock.Features/1.0.0' -NotePropertyValue @{
    runtime = @{ 'Airlock.Features.dll' = @{} }
}
$deps.libraries | Add-Member -NotePropertyName 'Airlock.Features/1.0.0' -NotePropertyValue @{
    type = 'project'
    serviceable = $false
    sha512 = ''
}
[IO.File]::WriteAllText((Resolve-Path -LiteralPath $depsPath).Path, ($deps | ConvertTo-Json -Depth 60))
Write-Host "Launcher: $((Resolve-Path -LiteralPath (Join-Path $Destination 'Airlock Launcher.exe')).Path)"
