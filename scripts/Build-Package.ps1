[CmdletBinding()]
param(
    # The Jellyfin server version to build for. A plugin only loads on the version it was built for.
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$JellyfinVersion = '12.2.0'
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
# The only place the version is set is Directory.Build.props.
$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version + '.0'
Push-Location $projectRoot
try {
    dotnet restore ThemeManager.slnx --locked-mode
    if ($LASTEXITCODE) { throw 'Restore failed.' }
    dotnet test ThemeManager.slnx -c Release --no-restore
    if ($LASTEXITCODE) { throw 'Tests failed.' }
    $suffix = if ($JellyfinVersion -ne '12.2.0') { "-jf$JellyfinVersion" } else { '' }
    $artifactRoot = Join-Path $projectRoot ('artifacts\build-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + $suffix)
    # 12.2 builds into bin/ (used by the test scripts); other versions build into the artifact folder only.
    $output = if ($JellyfinVersion -eq '12.2.0') { Join-Path $projectRoot 'src\Jellyfin.Plugin.ThemeManager\bin\Release\net10.0' } else { Join-Path $artifactRoot 'bin' }
    dotnet restore src/Jellyfin.Plugin.ThemeManager --locked-mode -p:JellyfinVersion=$JellyfinVersion
    if ($LASTEXITCODE) { throw 'Restore failed.' }
    dotnet build src/Jellyfin.Plugin.ThemeManager -c Release --no-restore -p:JellyfinVersion=$JellyfinVersion -o $output
    if ($LASTEXITCODE) { throw 'Build failed.' }
    $stage = Join-Path $artifactRoot "ThemeManager_$version"
    New-Item -ItemType Directory -Path $stage | Out-Null
    # Explicit allow-list: never bundle Jellyfin or framework assemblies.
    foreach ($name in @('Jellyfin.Plugin.ThemeManager.dll','ThemeManager.Core.dll')) {
        Copy-Item -LiteralPath (Join-Path $output $name) -Destination $stage
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $projectRoot 'examples') -Destination $stage -Recurse
    $zip = Join-Path $artifactRoot "ThemeManager-$version$suffix.zip"
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
    $sourceStage=Join-Path $artifactRoot 'source'
    New-Item -ItemType Directory -Path $sourceStage | Out-Null
    $sourceFiles=@()
    foreach ($directory in @('src','tests','scripts','docs','examples','.github')) {
        $sourceFiles+=Get-ChildItem -LiteralPath (Join-Path $projectRoot $directory) -Recurse -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Name -ne 'PROD-INSTALL.md' }
    }
    foreach ($name in @('README.md','LICENSE','Directory.Build.props','ThemeManager.slnx','.gitignore')) {
        $sourceFiles+=Get-Item -LiteralPath (Join-Path $projectRoot $name)
    }
    foreach ($file in $sourceFiles) {
        $target=Join-Path $sourceStage ([IO.Path]::GetRelativePath($projectRoot,$file.FullName))
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
    $sourceZip=Join-Path $artifactRoot "ThemeManager-$version-source.zip"
    Compress-Archive -Path (Join-Path $sourceStage '*') -DestinationPath $sourceZip
    $hashes=Get-FileHash -LiteralPath $zip,$sourceZip -Algorithm SHA256
    $hashes | ForEach-Object { $_.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_.Path) } | Set-Content -LiteralPath (Join-Path $artifactRoot 'SHA256SUMS.txt') -Encoding utf8
    $hashes | Select-Object Hash,Path | Format-List
    Write-Output "Built for Jellyfin $JellyfinVersion. The plugin ZIP contains no private media, credentials or Jellyfin binaries."
}
finally {
    # Leave obj/ restored for the default 12.2 build, which the IDE and test scripts use.
    if ($JellyfinVersion -ne '12.2.0') { dotnet restore src/Jellyfin.Plugin.ThemeManager --locked-mode | Out-Null }
    Pop-Location
}
