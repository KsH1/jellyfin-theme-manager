[CmdletBinding()]
param(
    [string]$JellyfinExecutable = 'C:\Program Files\Jellyfin\Server\jellyfin.exe',
    [int]$Port = 18197
)
# Starts throwaway loopback-only Jellyfin servers with a deliberately broken plugin or broken plugin data,
# and checks that Jellyfin itself still starts and serves its web client, and that the log says why.
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$bin = Join-Path $projectRoot 'src\Jellyfin.Plugin.ThemeManager\bin\Release\net10.0'
$webRoot = Join-Path ([IO.Path]::GetDirectoryName($JellyfinExecutable)) 'jellyfin-web'
$base = "http://127.0.0.1:$Port"
$checks = [Collections.Generic.List[string]]::new()
function Expect($Condition,[string]$Name) { if (!$Condition) { throw "FAILED: $Name" }; $checks.Add($Name); Write-Output "PASS: $Name" }

function Invoke-Scenario([string]$Name, [scriptblock]$Prepare, [scriptblock]$Verify) {
    if ([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object Port -eq $Port) { throw "Port $Port is already in use." }
    $runRoot = Join-Path $projectRoot ('test-server\failsafe-' + $Name + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    $configRoot = Join-Path $runRoot 'config'
    $pluginRoot = Join-Path $runRoot 'plugins\ThemeManager_0.1.0.0'
    New-Item -ItemType Directory -Path $configRoot,$pluginRoot | Out-Null
    @"
<?xml version="1.0" encoding="utf-8"?>
<NetworkConfiguration>
 <InternalHttpPort>$Port</InternalHttpPort><PublicHttpPort>$Port</PublicHttpPort>
 <EnableIPv4>true</EnableIPv4><EnableIPv6>false</EnableIPv6>
 <EnableHttps>false</EnableHttps><EnableRemoteAccess>false</EnableRemoteAccess>
 <AutoDiscovery>false</AutoDiscovery><EnableUPnP>false</EnableUPnP>
 <LocalNetworkAddresses><string>127.0.0.1</string></LocalNetworkAddresses>
</NetworkConfiguration>
"@ | Set-Content -LiteralPath (Join-Path $configRoot 'network.xml') -Encoding utf8
    foreach ($file in 'Jellyfin.Plugin.ThemeManager.dll','ThemeManager.Core.dll') { Copy-Item -LiteralPath (Join-Path $bin $file) -Destination $pluginRoot }
    & $Prepare $runRoot $pluginRoot
    $arguments = @('--datadir',('"'+$runRoot+'"'),'--configdir',('"'+$configRoot+'"'),'--cachedir',('"'+(Join-Path $runRoot 'cache')+'"'),'--logdir',('"'+(Join-Path $runRoot 'log')+'"'),'--webdir',('"'+$webRoot+'"'),'--nonetchange','--service')
    $server = Start-Process -FilePath $JellyfinExecutable -ArgumentList $arguments -WorkingDirectory $runRoot -WindowStyle Hidden -PassThru
    try {
        $ready = $false
        for ($i = 0; $i -lt 120 -and !$server.HasExited; $i++) {
            # The web files are served during startup already; wait until the API itself is up.
            try { if ((Invoke-WebRequest "$base/System/Info/Public" -TimeoutSec 3 -SkipHttpErrorCheck).StatusCode -eq 200) { $ready = $true; break } } catch { }
            Start-Sleep -Seconds 1
        }
        Expect (!$server.HasExited) "${Name}: Jellyfin process keeps running"
        Expect $ready "${Name}: Jellyfin finishes starting and its API answers"
        Expect ((Invoke-WebRequest "$base/web/index.html" -TimeoutSec 5 -SkipHttpErrorCheck).StatusCode -eq 200) "${Name}: Jellyfin web client is served"
        Start-Sleep -Seconds 8 # let startup tasks write their log lines
        $log = (Get-ChildItem -LiteralPath (Join-Path $runRoot 'log') -Filter 'log_*.log' | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
        & $Verify $log
    }
    finally {
        # Stop only the process created here; never the production server.
        if (!$server.HasExited) { Stop-Process -Id $server.Id -Force; $server.WaitForExit(10000) | Out-Null }
        Write-Output "${Name}: evidence in $runRoot"
    }
}

# A: the plugin loads, but its data is broken.
Invoke-Scenario 'bad-data' {
    param($runRoot, $pluginRoot)
    New-Item -ItemType Directory -Path (Join-Path $runRoot 'data\theme-manager\users') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $runRoot 'data\theme-manager\packs') -Value 'a file where the packs folder should be'
    Set-Content -LiteralPath (Join-Path $runRoot 'data\theme-manager\users\0123456789abcdef0123456789abcdef.json') -Value '{ broken'
} {
    param($log)
    Expect ($log -match 'Loaded plugin: "Theme Manager"') 'bad-data: plugin still loads'
    Expect ($log -match 'Theme Manager rejected theme pack "?\(catalog\)"?: "?Theme packs folder could not be read') 'bad-data: log names the unreadable packs folder'
    Expect ($log -match 'Theme Manager catalog "?(loaded|refreshed)"?: 0 valid packs') 'bad-data: log reports an empty catalog'
    Expect ((Invoke-WebRequest "$base/web/theme-manager.html" -TimeoutSec 5 -SkipHttpErrorCheck).StatusCode -eq 200) 'bad-data: themed entry still opens'
}

# B: the plugin assembly itself is corrupt and cannot be loaded.
Invoke-Scenario 'corrupt-dll' {
    param($runRoot, $pluginRoot)
    [IO.File]::WriteAllBytes((Join-Path $pluginRoot 'Jellyfin.Plugin.ThemeManager.dll'), [Text.Encoding]::ASCII.GetBytes('this is not a .NET assembly'))
} {
    param($log)
    Expect ($log -notmatch 'Loaded plugin: "Theme Manager"') 'corrupt-dll: broken plugin is not loaded'
    Expect ($log -match '(?s)ThemeManager.{0,400}(BadImageFormat|Failed to load|Error|not a valid)') 'corrupt-dll: log says why the plugin failed'
    Expect ((Invoke-WebRequest "$base/web/theme-manager.html" -TimeoutSec 5 -SkipHttpErrorCheck).StatusCode -ne 500) 'corrupt-dll: no server error for the themed link'
}

Write-Output "Completed $($checks.Count) fail-safe checks."
