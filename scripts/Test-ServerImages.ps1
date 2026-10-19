[CmdletBinding()]
param(
    [string]$JellyfinExecutable = 'C:\Program Files\Jellyfin\Server\jellyfin.exe',
    [int]$Port = 18204
)
# Library images for TV and mobile apps: set from the server default, backed up first, put back exactly on restore.
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$webRoot = Join-Path ([IO.Path]::GetDirectoryName($JellyfinExecutable)) 'jellyfin-web'
$runRoot = Join-Path $projectRoot ('test-server\server-images-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$configRoot = Join-Path $runRoot 'config'
$plugin = Join-Path $runRoot 'plugins\ThemeManager_test'
$base = "http://127.0.0.1:$Port"
$checks = [Collections.Generic.List[string]]::new()
function Expect($Condition, [string]$Name) { if (!$Condition) { throw "FAILED: $Name" }; $checks.Add($Name); Write-Output "PASS: $Name" }

New-Item -ItemType Directory -Path $configRoot, $plugin | Out-Null
"<?xml version=`"1.0`" encoding=`"utf-8`"?><NetworkConfiguration><InternalHttpPort>$Port</InternalHttpPort><PublicHttpPort>$Port</PublicHttpPort><EnableIPv6>false</EnableIPv6><EnableRemoteAccess>false</EnableRemoteAccess><AutoDiscovery>false</AutoDiscovery><EnableUPnP>false</EnableUPnP><LocalNetworkAddresses><string>127.0.0.1</string></LocalNetworkAddresses></NetworkConfiguration>" |
    Set-Content -LiteralPath (Join-Path $configRoot 'network.xml') -Encoding utf8
foreach ($dll in 'Jellyfin.Plugin.ThemeManager.dll', 'ThemeManager.Core.dll') { Copy-Item (Join-Path $projectRoot "src\Jellyfin.Plugin.ThemeManager\bin\Release\net10.0\$dll") $plugin }
Copy-Item -Recurse (Join-Path $projectRoot 'default-packs') (Join-Path $plugin 'default-packs')
$arguments = @('--datadir', "`"$runRoot`"", '--configdir', "`"$configRoot`"", '--cachedir', "`"$(Join-Path $runRoot 'cache')`"", '--logdir', "`"$(Join-Path $runRoot 'log')`"", '--webdir', "`"$webRoot`"", '--nonetchange', '--service')
if ([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object Port -eq $Port) { throw "Port $Port is already in use." }
$server = Start-Process -FilePath $JellyfinExecutable -ArgumentList $arguments -WorkingDirectory $runRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runRoot 'stdout.log') -RedirectStandardError (Join-Path $runRoot 'stderr.log')
try {
    for ($i = 0; $i -lt 120; $i++) { try { if ((Invoke-WebRequest "$base/Startup/User" -SkipHttpErrorCheck -TimeoutSec 2).StatusCode -eq 200) { break } } catch {}; Start-Sleep 1 }
    $password = [Guid]::NewGuid().ToString('N') + '!aA9'  # throw-away, this server only
    Invoke-RestMethod "$base/Startup/User" -Method Post -ContentType 'application/json' -Body (@{ Name = 'tm-admin'; Password = $password } | ConvertTo-Json) | Out-Null
    Invoke-RestMethod "$base/Startup/Complete" -Method Post | Out-Null
    $auth = 'MediaBrowser Client="ThemeManagerServerImages", Device="Test", DeviceId="tm-server-images", Version="1"'
    $login = $null
    for ($i = 0; $i -lt 90 -and !$login; $i++) { try { $login = Invoke-RestMethod "$base/Users/AuthenticateByName" -Method Post -ContentType 'application/json' -Headers @{ Authorization = $auth } -Body (@{ Username = 'tm-admin'; Pw = $password } | ConvertTo-Json) } catch { Start-Sleep 1 } }
    $h = @{ Authorization = $auth + ', Token="' + $login.AccessToken + '"' }
    function Api([string]$Path, [string]$Method = 'GET', $Body = $null) {
        $p = @{ Uri = "$base/$Path"; Method = $Method; Headers = $h }
        if ($null -ne $Body) { $p.Body = ($Body | ConvertTo-Json -Depth 6); $p.ContentType = 'application/json' }
        $r = Invoke-WebRequest @p -SkipHttpErrorCheck
        if ([int]$r.StatusCode -ge 400) { throw "HTTP $($r.StatusCode): $Method $Path $($r.Content)" }
        if ($r.Content) { return $r.Content | ConvertFrom-Json }
    }
    foreach ($lib in @(@('Film', 'movies'), @('TV Shows', 'tvshows'), @('Musik', 'music'))) {
        $path = Join-Path $runRoot ('media\' + $lib[1]); New-Item -ItemType Directory $path -Force | Out-Null
        Api ("Library/VirtualFolders?name=" + [uri]::EscapeDataString($lib[0]) + "&collectionType=$($lib[1])&refreshLibrary=false") 'POST' @{ LibraryOptions = @{ PathInfos = @(@{ Path = $path }); EnableRealtimeMonitor = $false } } | Out-Null
    }
    $folders = @{}; (Invoke-WebRequest "$base/Library/VirtualFolders" -Headers $h).Content | ConvertFrom-Json | ForEach-Object { $folders[$_.Name] = $_.ItemId }
    function PrimarySize([string]$Name) {
        $images = @((Invoke-WebRequest "$base/Items/$($folders[$Name])/Images" -Headers $h).Content | ConvertFrom-Json) | Where-Object ImageType -eq 'Primary'
        if ($images) { [long]$images[0].Size } else { 0 }
    }
    # The movie library starts with an image of its own; TV Shows and Musik have none.
    $original = [IO.File]::ReadAllBytes((Join-Path $projectRoot 'logo.png'))
    Invoke-WebRequest "$base/Items/$($folders['Film'])/Images/Primary" -Method Post -Headers $h -ContentType 'image/png' -Body ([Convert]::ToBase64String($original)) | Out-Null
    $originalSize = PrimarySize 'Film'
    Expect ($originalSize -gt 0) 'test setup: Film has its own image'
    Expect ((PrimarySize 'TV Shows') -eq 0) 'test setup: TV Shows has no image'

    $moviesSize = (Get-Item (Join-Path $projectRoot 'default-packs\halloween\movies.webp')).Length
    $tvSize = (Get-Item (Join-Path $projectRoot 'default-packs\halloween\tv-shows.webp')).Length
    Api 'ThemeManager/Admin/Settings' 'PUT' @{ choice = @{ mode = 'Fixed'; themeId = 'halloween' }; timeZoneId = 'Europe/Copenhagen' } | Out-Null
    Expect ((Api 'ThemeManager/Admin/ServerImages').enabled -eq $false) 'off by default'
    $run = Api 'ThemeManager/Admin/ServerImages' 'PUT' @{ enabled = $true }
    Expect ($run.themeId -eq 'halloween' -and -not $run.error) 'switching on applies the server default theme'
    Expect ((PrimarySize 'Film') -eq $moviesSize) 'Film gets the theme Movies banner'
    Expect ((PrimarySize 'TV Shows') -eq $tvSize) 'TV Shows gets the theme TV Shows banner'
    Expect ((PrimarySize 'Musik') -gt 0) 'Musik (no banner type) gets a generic banner with its name'
    $generated = @(Get-ChildItem (Join-Path $runRoot 'data/theme-manager/server-images/generated') -Filter '*.png')
    Expect ($generated.Count -eq 1) 'one generic banner was made'
    $again = Api 'ThemeManager/Admin/ServerImages/Apply' 'POST'
    Expect (@($again.changed).Count -eq 0) 'an unchanged theme writes nothing'

    $libraries = @((Api 'ThemeManager/Admin/Libraries').libraries)
    $bannerOf = @{}; $libraries | ForEach-Object { $bannerOf[$_.name] = $_.banner }
    Expect ($bannerOf['Film'] -eq 'movies' -and $bannerOf['TV Shows'] -eq 'tvShows' -and $bannerOf['Musik'] -eq 'generic') 'library list shows the automatic banners'
    $musik = ($libraries | Where-Object name -eq 'Musik').id; $film = ($libraries | Where-Object name -eq 'Film').id
    Api "ThemeManager/Admin/Libraries/$musik" 'PUT' @{ banner = 'movies' } | Out-Null
    Expect ((PrimarySize 'Musik') -eq $moviesSize) 'a library can be given another banner'
    Api "ThemeManager/Admin/Libraries/$film" 'PUT' @{ banner = 'none' } | Out-Null
    Expect ((PrimarySize 'Film') -eq $originalSize) "'own image' gives a library its original image back"
    Expect ((Api 'ThemeManager/Effective').libraryBanners.$film -eq 'none') 'Jellyfin Web gets the choices'
    $bad = Invoke-WebRequest "$base/ThemeManager/Admin/Libraries/$film" -Method Put -Headers $h -ContentType 'application/json' -Body '{"banner":"poster"}' -SkipHttpErrorCheck
    Expect ($bad.StatusCode -eq 400) 'an unknown banner choice is rejected'
    Api "ThemeManager/Admin/Libraries/$musik" 'PUT' @{ banner = 'auto' } | Out-Null
    Api "ThemeManager/Admin/Libraries/$film" 'PUT' @{ banner = 'auto' } | Out-Null
    Expect ((PrimarySize 'Film') -eq $moviesSize -and (PrimarySize 'Musik') -gt 0 -and (PrimarySize 'Musik') -ne $moviesSize) 'automatic again restores the automatic banners'

    Api 'ThemeManager/Admin/Settings' 'PUT' @{ choice = @{ mode = 'Original' }; timeZoneId = 'Europe/Copenhagen' } | Out-Null
    $restored = Api 'ThemeManager/Admin/ServerImages/Apply' 'POST'
    Expect (@($restored.restored).Count -eq 3) 'Original Jellyfin as server default restores all three libraries'
    Expect ((PrimarySize 'Film') -eq $originalSize) 'Film gets its own image back'
    Expect ((PrimarySize 'TV Shows') -eq 0 -and (PrimarySize 'Musik') -eq 0) 'TV Shows and Musik have no image again'

    Api 'ThemeManager/Admin/Settings' 'PUT' @{ choice = @{ mode = 'Fixed'; themeId = 'christmas' }; timeZoneId = 'Europe/Copenhagen' } | Out-Null
    Api 'ThemeManager/Admin/ServerImages/Apply' 'POST' | Out-Null
    Expect ((PrimarySize 'Film') -eq (Get-Item (Join-Path $projectRoot 'default-packs\christmas\movies.webp')).Length) 'a new server default theme is applied'
    $off = Api 'ThemeManager/Admin/ServerImages' 'PUT' @{ enabled = $false }
    Expect ((PrimarySize 'Film') -eq $originalSize -and (PrimarySize 'TV Shows') -eq 0) 'switching off restores the original images'
    $stateFile = Join-Path $runRoot 'data\theme-manager\server-images\state.json'
    Expect ((Get-Content $stateFile -Raw).Trim() -eq '{}' -and @(Get-ChildItem (Split-Path $stateFile) -Filter 'original-*').Count -eq 0) 'no records or backups are left after restoring'
    $log = Get-Content (Get-ChildItem (Join-Path $runRoot 'log') -Filter 'log_*.log' | Select-Object -First 1).FullName -Raw
    Expect (!($log -match '\[ERR\].*ThemeManager')) 'no Theme Manager errors'
}
finally { if (!$server.HasExited) { Stop-Process -Id $server.Id; $server.WaitForExit(15000) | Out-Null } }
Write-Output "Completed $($checks.Count) checks. Evidence: $runRoot"
