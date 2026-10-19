[CmdletBinding()]
param(
    [string]$JellyfinExecutable = 'C:\Program Files\Jellyfin\Server\jellyfin.exe',
    # A folder made by Build-Package.ps1, e.g. artifacts\build-20261019-120000
    [Parameter(Mandatory)][string]$Package,
    [int]$Port = 18201
)
# A new install gets the standard packs once; restarts copy nothing; a deleted standard pack stays deleted.
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version + '.0'
$expected = (Get-ChildItem (Join-Path $projectRoot 'default-packs') -Directory).Count
$webRoot = Join-Path ([IO.Path]::GetDirectoryName($JellyfinExecutable)) 'jellyfin-web'
$runRoot = Join-Path $projectRoot ('test-server\default-packs-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$configRoot = Join-Path $runRoot 'config'
$plugin = Join-Path $runRoot "plugins\ThemeManager_$version"
$packs = Join-Path $runRoot 'data\theme-manager\packs'
$checks = [Collections.Generic.List[string]]::new()
function Expect($Condition, [string]$Name) { if (!$Condition) { throw "FAILED: $Name" }; $checks.Add($Name); Write-Output "PASS: $Name" }

New-Item -ItemType Directory -Path $configRoot, $plugin | Out-Null
"<?xml version=`"1.0`" encoding=`"utf-8`"?><NetworkConfiguration><InternalHttpPort>$Port</InternalHttpPort><PublicHttpPort>$Port</PublicHttpPort><EnableIPv6>false</EnableIPv6><EnableRemoteAccess>false</EnableRemoteAccess><AutoDiscovery>false</AutoDiscovery><EnableUPnP>false</EnableUPnP><LocalNetworkAddresses><string>127.0.0.1</string></LocalNetworkAddresses></NetworkConfiguration>" |
    Set-Content -LiteralPath (Join-Path $configRoot 'network.xml') -Encoding utf8
foreach ($dll in 'Jellyfin.Plugin.ThemeManager.dll', 'ThemeManager.Core.dll') { Copy-Item (Join-Path $projectRoot "$Package\ThemeManager_$version\$dll") $plugin }
Copy-Item -Recurse (Join-Path $projectRoot 'default-packs') (Join-Path $plugin 'default-packs')

function Start-Once([string]$Label) {
    if ([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object Port -eq $Port) { throw "Port $Port is already in use." }
    $logDir = Join-Path $runRoot "log-$Label"
    $arguments = @('--datadir', "`"$runRoot`"", '--configdir', "`"$configRoot`"", '--cachedir', "`"$(Join-Path $runRoot 'cache')`"", '--logdir', "`"$logDir`"", '--webdir', "`"$webRoot`"", '--nonetchange', '--service')
    $server = Start-Process -FilePath $JellyfinExecutable -ArgumentList $arguments -WorkingDirectory $runRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runRoot "stdout-$Label.log") -RedirectStandardError (Join-Path $runRoot "stderr-$Label.log")
    try {
        for ($i = 0; $i -lt 90; $i++) {
            Start-Sleep 1
            $log = Get-ChildItem $logDir -Filter 'log_*.log' -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($log -and (Select-String -LiteralPath $log.FullName -Pattern 'Theme Manager catalog "loaded"' -Quiet)) { return Get-Content -LiteralPath $log.FullName -Raw }
        }
        throw "$Label`: the catalog was not loaded within 90 s"
    }
    finally { if (!$server.HasExited) { Stop-Process -Id $server.Id; $server.WaitForExit(10000) | Out-Null } }
}

$first = Start-Once 'first'
Expect ($first -match "installed $expected standard theme packs") "first start installs all $expected standard packs"
Expect ($first -match "catalog `"loaded`": $expected valid packs, 0 rejected") "all $expected standard packs are valid"
Expect ((Get-ChildItem $packs -Directory).Count -eq $expected) "pack folder holds exactly the standard packs"
Expect (@(Get-ChildItem $packs -Recurse -File -Filter '*.png').Count -eq 0) "standard packs use WebP artwork"

$second = Start-Once 'second'
Expect (!($second -match 'installed \d+ standard theme packs')) "second start copies nothing"
Expect ($second -match "catalog `"loaded`": $expected valid packs") "second start still has $expected packs"

Remove-Item -LiteralPath (Join-Path $packs 'halloween') -Recurse -Force
$third = Start-Once 'third'
Expect (!(Test-Path (Join-Path $packs 'halloween'))) "a deleted standard pack is not restored"
Expect ($third -match "catalog `"loaded`": $($expected - 1) valid packs") "catalog has one pack fewer"
Expect (!($third -match '\[ERR\].*ThemeManager')) "no Theme Manager errors"
Write-Output "Completed $($checks.Count) checks. Evidence: $runRoot"
