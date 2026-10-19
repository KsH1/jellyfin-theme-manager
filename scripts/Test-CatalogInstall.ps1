[CmdletBinding()]
param(
    [string]$JellyfinExecutable = 'C:\Program Files\Jellyfin\Server\jellyfin.exe',
    [string]$RepositoryUrl = 'https://raw.githubusercontent.com/KsH1/jellyfin-theme-manager/main/repository/manifest.json',
    [int]$Port = 18202
)
# The way other servers get the plugin: add the repository, install from the catalog, restart.
# Checks the plugin is active with the published version, shows its logo and installs the standard themes.
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$guid = '8fa53208-c9f3-4e99-9858-2963ed658aae'
# raw.githubusercontent.com keeps a copy for up to five minutes (separately for compressed requests, and query strings
# do not bypass it). Wait until it serves the version in this checkout's repository/manifest.json.
$localManifest = Join-Path $projectRoot 'repository\manifest.json'
$expected = if (Test-Path $localManifest) { (Get-Content $localManifest -Raw | ConvertFrom-Json)[0].versions[0].version } else { $null }
for ($i = 0; $i -lt 40; $i++) {
    $latest = (Invoke-RestMethod $RepositoryUrl)[0].versions[0].version
    if (!$expected -or $latest -eq $expected) { break }
    Start-Sleep 10
}
if ($expected -and $latest -ne $expected) { throw "GitHub still serves $latest, not $expected, after 400 s." }
$webRoot = Join-Path ([IO.Path]::GetDirectoryName($JellyfinExecutable)) 'jellyfin-web'
$runRoot = Join-Path $projectRoot ('test-server\catalog-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$configRoot = Join-Path $runRoot 'config'
$base = "http://127.0.0.1:$Port"
$checks = [Collections.Generic.List[string]]::new()
function Expect($Condition, [string]$Name) { if (!$Condition) { throw "FAILED: $Name" }; $checks.Add($Name); Write-Output "PASS: $Name" }

New-Item -ItemType Directory -Path $configRoot | Out-Null
"<?xml version=`"1.0`" encoding=`"utf-8`"?><NetworkConfiguration><InternalHttpPort>$Port</InternalHttpPort><PublicHttpPort>$Port</PublicHttpPort><EnableIPv6>false</EnableIPv6><EnableRemoteAccess>false</EnableRemoteAccess><AutoDiscovery>false</AutoDiscovery><EnableUPnP>false</EnableUPnP><LocalNetworkAddresses><string>127.0.0.1</string></LocalNetworkAddresses></NetworkConfiguration>" |
    Set-Content -LiteralPath (Join-Path $configRoot 'network.xml') -Encoding utf8

function Start-Server([string]$Label) {
    if ([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object Port -eq $Port) { throw "Port $Port is already in use." }
    $arguments = @('--datadir', "`"$runRoot`"", '--configdir', "`"$configRoot`"", '--cachedir', "`"$(Join-Path $runRoot 'cache')`"", '--logdir', "`"$(Join-Path $runRoot 'log')`"", '--webdir', "`"$webRoot`"", '--nonetchange', '--service')
    $server = Start-Process -FilePath $JellyfinExecutable -ArgumentList $arguments -WorkingDirectory $runRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runRoot "stdout-$Label.log") -RedirectStandardError (Join-Path $runRoot "stderr-$Label.log")
    for ($i = 0; $i -lt 120; $i++) {
        try { if ((Invoke-WebRequest "$base/System/Info/Public" -SkipHttpErrorCheck -TimeoutSec 2).StatusCode -eq 200) { return $server } } catch {}
        Start-Sleep 1
    }
    throw "$Label`: server did not start"
}
function Stop-Server($Server) { if (!$Server.HasExited) { Stop-Process -Id $Server.Id; $Server.WaitForExit(15000) | Out-Null } }

$server = Start-Server 'first'
try {
    $password = [Guid]::NewGuid().ToString('N') + '!aA9'  # throw-away, this server only
    for ($i = 0; $i -lt 60; $i++) { if ((Invoke-WebRequest "$base/Startup/User" -SkipHttpErrorCheck).StatusCode -eq 200) { break }; Start-Sleep 1 }
    Invoke-RestMethod "$base/Startup/User" -Method Post -ContentType 'application/json' -Body (@{ Name = 'tm-admin'; Password = $password } | ConvertTo-Json) | Out-Null
    Invoke-RestMethod "$base/Startup/Complete" -Method Post | Out-Null
    $auth = 'MediaBrowser Client="ThemeManagerCatalogTest", Device="Test", DeviceId="tm-catalog-test", Version="1"'
    $login = Invoke-RestMethod "$base/Users/AuthenticateByName" -Method Post -ContentType 'application/json' -Headers @{ Authorization = $auth } -Body (@{ Username = 'tm-admin'; Pw = $password } | ConvertTo-Json)
    $h = @{ Authorization = $auth + ', Token="' + $login.AccessToken + '"' }
    $repos = @((Invoke-WebRequest "$base/Repositories" -Headers $h).Content | ConvertFrom-Json) + @([pscustomobject]@{ Name = 'Theme Manager'; Url = $RepositoryUrl; Enabled = $true })
    Invoke-WebRequest "$base/Repositories" -Method Post -Headers $h -ContentType 'application/json' -Body (ConvertTo-Json -InputObject @($repos) -Depth 3 -Compress) | Out-Null
    $package = (Invoke-WebRequest "$base/Packages/Theme%20Manager?assemblyGuid=$guid" -Headers $h).Content | ConvertFrom-Json
    Expect ($package.versions.version -contains $latest) "catalog offers Theme Manager $latest"
    $install = Invoke-WebRequest "$base/Packages/Installed/Theme%20Manager?assemblyGuid=$guid&version=$latest&repositoryUrl=$([uri]::EscapeDataString($RepositoryUrl))" -Method Post -Headers $h -SkipHttpErrorCheck
    Expect ($install.StatusCode -eq 204) "install request accepted"
    $folder = $null
    for ($i = 0; $i -lt 60; $i++) { $folder = Get-ChildItem (Join-Path $runRoot 'plugins') -Directory -ErrorAction SilentlyContinue | Where-Object Name -like "*$latest"; if ($folder -and (Test-Path (Join-Path $folder.FullName 'meta.json'))) { break }; Start-Sleep 1 }
    Expect ($null -ne $folder) "plugin downloaded and unpacked"
    $meta = Get-Content (Join-Path $folder.FullName 'meta.json') -Raw | ConvertFrom-Json
    Expect ($meta.imagePath -and (Test-Path $meta.imagePath)) "Jellyfin stored the plugin logo ($([IO.Path]::GetFileName([string]$meta.imagePath)))"
}
finally { Stop-Server $server }

$server = Start-Server 'second'
try {
    # /System/Info/Public answers before Jellyfin has finished loading; logins are refused until then.
    $login = $null
    for ($i = 0; $i -lt 90 -and !$login; $i++) {
        try { $login = Invoke-RestMethod "$base/Users/AuthenticateByName" -Method Post -ContentType 'application/json' -Headers @{ Authorization = $auth } -Body (@{ Username = 'tm-admin'; Pw = $password } | ConvertTo-Json) }
        catch { Start-Sleep 1 }
    }
    $h = @{ Authorization = $auth + ', Token="' + $login.AccessToken + '"' }
    for ($i = 0; $i -lt 60; $i++) { $plugin = ((Invoke-WebRequest "$base/Plugins" -Headers $h).Content | ConvertFrom-Json) | Where-Object Name -eq 'Theme Manager'; if ($plugin) { break }; Start-Sleep 1 }
    Expect ($plugin.Version -eq $latest -and $plugin.Status -eq 'Active') "Theme Manager $latest is active after restart"
    $image = Invoke-WebRequest "$base/Plugins/$($plugin.Id)/$($plugin.Version)/Image" -Headers $h -SkipHttpErrorCheck
    Expect ($image.StatusCode -eq 200 -and $image.Headers.'Content-Type' -match 'image/') "plugin card image is served ($($image.Headers.'Content-Type'))"
    for ($i = 0; $i -lt 60; $i++) { $catalog = Invoke-WebRequest "$base/ThemeManager/Catalog" -Headers $h -SkipHttpErrorCheck; if ($catalog.StatusCode -eq 200) { break }; Start-Sleep 1 }
    Expect (@(($catalog.Content | ConvertFrom-Json).themes).Count -eq 29) "the 29 standard themes are installed"
}
finally { Stop-Server $server }
Write-Output "Completed $($checks.Count) checks. Evidence: $runRoot"
