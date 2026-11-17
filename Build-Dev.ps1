param([string]$Destination = (Join-Path $PSScriptRoot 'dist\Airlock Launcher - Dev'))

$ErrorActionPreference = 'Stop'
$baseArchive = Join-Path $PSScriptRoot 'vendor\AirlockLauncher-4.2.0-win-x64.zip'
$project = Join-Path $PSScriptRoot 'Patcher\Patcher.csproj'
if (Test-Path -LiteralPath $Destination) {
    throw "The destination already exists: $Destination"
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

[IO.File]::Move((Resolve-Path -LiteralPath $patchedDll).Path, (Resolve-Path -LiteralPath $launcherDll).Path, $true)
Copy-Item -LiteralPath $hooks -Destination $binDirectory

$depsPath = Join-Path $binDirectory 'SS14.Launcher.deps.json'
$deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json -AsHashtable
$target = $deps.targets[$deps.runtimeTarget.name]
$target['SS14.Launcher/4.2.0'].dependencies['Airlock.Features'] = '1.0.0'
$target['Airlock.Features/1.0.0'] = @{ runtime = @{ 'Airlock.Features.dll' = @{} } }
$deps.libraries['Airlock.Features/1.0.0'] = @{ type = 'project'; serviceable = $false; sha512 = '' }
$deps | ConvertTo-Json -Depth 60 | Set-Content -LiteralPath $depsPath -Encoding utf8
