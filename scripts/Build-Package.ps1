[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $projectRoot
try {
    dotnet restore ThemeManager.slnx --locked-mode
    if ($LASTEXITCODE) { throw 'Restore failed.' }
    dotnet test ThemeManager.slnx -c Release --no-restore
    if ($LASTEXITCODE) { throw 'Tests failed.' }
    dotnet build src/Jellyfin.Plugin.ThemeManager -c Release --no-restore
    if ($LASTEXITCODE) { throw 'Build failed.' }
    $artifactRoot = Join-Path $projectRoot ('artifacts\build-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
    $stage = Join-Path $artifactRoot 'ThemeManager_0.1.0.0'
    New-Item -ItemType Directory -Path $stage | Out-Null
    # Explicit allow-list: never bundle Jellyfin or framework assemblies.
    foreach ($name in @('Jellyfin.Plugin.ThemeManager.dll','ThemeManager.Core.dll')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot "src\Jellyfin.Plugin.ThemeManager\bin\Release\net10.0\$name") -Destination $stage
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $stage -Recurse
    Copy-Item -LiteralPath (Join-Path $projectRoot 'examples') -Destination $stage -Recurse
    $zip = Join-Path $artifactRoot 'ThemeManager-0.1.0.0.zip'
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
    $sourceStage=Join-Path $artifactRoot 'source'
    New-Item -ItemType Directory -Path $sourceStage | Out-Null
    $sourceFiles=@()
    foreach ($directory in @('src','tests','scripts','docs','examples','.github')) {
        $sourceFiles+=Get-ChildItem -LiteralPath (Join-Path $projectRoot $directory) -Recurse -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
    }
    foreach ($name in @('README.md','LICENSE','Directory.Build.props','ThemeManager.slnx','.gitignore')) {
        $sourceFiles+=Get-Item -LiteralPath (Join-Path $projectRoot $name)
    }
    foreach ($file in $sourceFiles) {
        $target=Join-Path $sourceStage ([IO.Path]::GetRelativePath($projectRoot,$file.FullName))
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
    $sourceZip=Join-Path $artifactRoot 'ThemeManager-0.1.0.0-source.zip'
    Compress-Archive -Path (Join-Path $sourceStage '*') -DestinationPath $sourceZip
    $hashes=Get-FileHash -LiteralPath $zip,$sourceZip -Algorithm SHA256
    $hashes | ForEach-Object { $_.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_.Path) } | Set-Content -LiteralPath (Join-Path $artifactRoot 'SHA256SUMS.txt') -Encoding utf8
    $hashes | Select-Object Hash,Path | Format-List
    Write-Output 'Artwork is separate in ThemePacks; the plugin ZIP contains no private media, credentials or Jellyfin binaries.'
}
finally { Pop-Location }
