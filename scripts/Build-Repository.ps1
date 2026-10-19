[CmdletBinding()]
param(
    # A folder made by Build-Package.ps1, e.g. artifacts\build-20261019-095639
    [Parameter(Mandatory)][string]$Package,
    # Defaults to the version in Directory.Build.props.
    [string]$Version,
    [string]$TargetAbi = '12.2.0.0',
    [string]$Changelog = 'Preview alpha.',
    [string]$BaseUrl = 'https://raw.githubusercontent.com/KsH1/jellyfin-theme-manager/main/repository'
)
# Builds the Jellyfin plugin repository: repository/manifest.json plus one ZIP per version.
# Add the manifest's raw URL in Jellyfin under Dashboard -> Plugins -> Repositories.
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$Version) { $Version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version + '.0' }
$repository = Join-Path $projectRoot 'repository'
$guid = '8fa53208-c9f3-4e99-9858-2963ed658aae'
# Jellyfin downloads this when it installs the plugin and shows it on the plugin card.
$imageUrl = 'https://raw.githubusercontent.com/KsH1/jellyfin-theme-manager/main/logo.png'
$description = 'Seasonal and permanent themes, a personal theme per user, library banners and theme packs for Jellyfin Web.'
$overview = 'Each user chooses their own theme; server branding is not changed. Install File Transformation to show themes in the normal Jellyfin Web address.'
# Invariant culture: a Danish locale would write 13.24.08, which Jellyfin cannot parse.
$timestamp = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture)

$stage = Join-Path ([IO.Path]::GetTempPath()) ('tm-repo-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    # Only the plugin's own assemblies: never Jellyfin or framework DLLs, docs or local logs.
    foreach ($name in 'Jellyfin.Plugin.ThemeManager.dll', 'ThemeManager.Core.dll') {
        Copy-Item -LiteralPath (Join-Path $projectRoot "$Package\ThemeManager_$Version\$name") -Destination $stage
    }
    # Standard themes; the plugin copies them into the server's pack folder on first start (DefaultPacks).
    Copy-Item -LiteralPath (Join-Path $projectRoot 'default-packs') -Destination (Join-Path $stage 'default-packs') -Recurse
    Copy-Item -LiteralPath (Join-Path $projectRoot 'logo.png') -Destination $stage
    [ordered]@{
        category = 'General'; changelog = $Changelog; description = $description; guid = $guid; name = 'Theme Manager'
        overview = $overview; owner = 'KsH1'; targetAbi = $TargetAbi; timestamp = $timestamp; version = $Version
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'meta.json') -Encoding utf8
    New-Item -ItemType Directory -Path $repository -Force | Out-Null
    $zipName = "theme-manager_$Version.zip"
    $zip = Join-Path $repository $zipName
    if (Test-Path $zip) { throw "$zipName already exists. Published versions are never overwritten; bump -Version." }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
}
finally { Remove-Item -LiteralPath $stage -Recurse -Force }

$manifestPath = Join-Path $repository 'manifest.json'
$existing = if (Test-Path $manifestPath) { @((Get-Content $manifestPath -Raw | ConvertFrom-Json)[0].versions) } else { @() }
$entry = [ordered]@{
    version = $Version; changelog = $Changelog; targetAbi = $TargetAbi; sourceUrl = "$BaseUrl/$zipName"
    checksum = (Get-FileHash -LiteralPath $zip -Algorithm MD5).Hash; timestamp = $timestamp
}
$manifest = @([ordered]@{
    guid = $guid; name = 'Theme Manager'; description = $description; overview = $overview; owner = 'KsH1'; category = 'General'; imageUrl = $imageUrl
    versions = @($entry) + @($existing | Where-Object version -ne $Version)
})
ConvertTo-Json -InputObject $manifest -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Output "Repository updated: $manifestPath ($zipName, MD5 $($entry.checksum))"
