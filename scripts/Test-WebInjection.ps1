[CmdletBinding()]
param(
    [string]$JellyfinExecutable = 'C:\Program Files\Jellyfin\Server\jellyfin.exe',
    [Parameter(Mandatory)][string]$FileTransformationDir,
    [int]$Port = 18199
)
# Checks that the theme appears in the normal /web/ address only through File Transformation, never by editing files.
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$webRoot = Join-Path ([IO.Path]::GetDirectoryName($JellyfinExecutable)) 'jellyfin-web'
$indexHash = (Get-FileHash -LiteralPath (Join-Path $webRoot 'index.html')).Hash
$script = '../ThemeManager/Client/bootstrap.js'
$checks = [Collections.Generic.List[string]]::new()
function Expect($Condition, [string]$Name) { if (!$Condition) { throw "FAILED: $Name" }; $checks.Add($Name); Write-Output "PASS: $Name" }
function Count([string]$Text) { ([regex]::Matches($Text, [regex]::Escape($script))).Count }

function Run-Scenario([string]$Name, [bool]$WithFileTransformation, [bool]$Enabled) {
    if ([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object Port -eq $Port) { throw "Port $Port is already in use." }
    $runRoot = Join-Path $projectRoot ('test-server\web-' + $Name + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    $configRoot = Join-Path $runRoot 'config'
    $plugin = Join-Path $runRoot 'plugins\ThemeManager_0.1.0.0'
    New-Item -ItemType Directory -Path $configRoot, $plugin, (Join-Path $runRoot 'plugins\configurations') | Out-Null
    "<?xml version=`"1.0`" encoding=`"utf-8`"?><NetworkConfiguration><InternalHttpPort>$Port</InternalHttpPort><PublicHttpPort>$Port</PublicHttpPort><EnableIPv6>false</EnableIPv6><EnableRemoteAccess>false</EnableRemoteAccess><AutoDiscovery>false</AutoDiscovery><EnableUPnP>false</EnableUPnP><LocalNetworkAddresses><string>127.0.0.1</string></LocalNetworkAddresses></NetworkConfiguration>" |
        Set-Content -LiteralPath (Join-Path $configRoot 'network.xml') -Encoding utf8
    foreach ($dll in 'Jellyfin.Plugin.ThemeManager.dll', 'ThemeManager.Core.dll') { Copy-Item (Join-Path $projectRoot "src\Jellyfin.Plugin.ThemeManager\bin\Release\net10.0\$dll") $plugin }
    if (!$Enabled) {
        "<?xml version=`"1.0`" encoding=`"utf-8`"?><PluginConfiguration><ShowInWebClient>false</ShowInWebClient></PluginConfiguration>" |
            Set-Content -LiteralPath (Join-Path $runRoot 'plugins\configurations\Jellyfin.Plugin.ThemeManager.xml') -Encoding utf8
    }
    if ($WithFileTransformation) { Copy-Item -Recurse $FileTransformationDir (Join-Path $runRoot 'plugins\File Transformation_3.0.2.0') }
    $arguments = @('--datadir', "`"$runRoot`"", '--configdir', "`"$configRoot`"", '--cachedir', "`"$(Join-Path $runRoot 'cache')`"", '--logdir', "`"$(Join-Path $runRoot 'log')`"", '--webdir', "`"$webRoot`"", '--nonetchange', '--service')
    $server = Start-Process -FilePath $JellyfinExecutable -ArgumentList $arguments -WorkingDirectory $runRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runRoot 'stdout.log') -RedirectStandardError (Join-Path $runRoot 'stderr.log')
    $base = "http://127.0.0.1:$Port"
    try {
        # Wait for the startup task that registers (or reports the absence of) File Transformation.
        $log = $null
        for ($i = 0; $i -lt 90; $i++) {
            Start-Sleep 1
            $log = Get-ChildItem (Join-Path $runRoot 'log') -Filter 'log_*.log' -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($log -and (Select-String -LiteralPath $log.FullName -Pattern 'Theme Manager: (registered with|File Transformation is not installed)' -Quiet)) { break }
        }
        # Plugin endpoints answer 503 until Jellyfin has finished starting; static /web/ files are served earlier.
        for ($i = 0; $i -lt 60; $i++) { $ready = Invoke-WebRequest "$base/ThemeManager/Client/bootstrap.js" -SkipHttpErrorCheck; if ($ready.StatusCode -eq 200) { break }; Start-Sleep 1 }
        if ($ready.StatusCode -ne 200) { Write-Output "DIAG not ready: $($ready.StatusCode) $($ready.Content)" }
        $text = Get-Content -LiteralPath $log.FullName -Raw
        $root = (Invoke-WebRequest "$base/web/" -SkipHttpErrorCheck).Content
        $index = (Invoke-WebRequest "$base/web/index.html" -SkipHttpErrorCheck).Content
        $entryResponse = Invoke-WebRequest "$base/web/theme-manager.html" -SkipHttpErrorCheck -MaximumRedirection 0
        $entry = [string]$entryResponse.Content
        if ((Count $entry) -ne 1) { Write-Output "DIAG entry: $($entryResponse.StatusCode) $($entryResponse.Headers.Location) $($entry.Length) chars"; Select-String -LiteralPath $log.FullName -Pattern 'web entry|theme-manager.html' | Select-Object -Last 3 | ForEach-Object { "DIAG log: " + $_.Line.Substring(0, [Math]::Min(300, $_.Line.Length)) } }
        $expected = if ($WithFileTransformation -and $Enabled) { 1 } else { 0 }
        Expect ((Count $root) -eq $expected) "$Name`: /web/ has the loader $expected time(s)"
        Expect ((Count $index) -eq $expected) "$Name`: /web/index.html has the loader $expected time(s)"
        Expect ($root -match '</html>') "$Name`: /web/ is still a complete Jellyfin page"
        if ($expected -eq 1) {
            # A browser that cached /web/ before File Transformation was installed asks "changed since?".
            $since = [DateTime]::UtcNow.ToString('R', [Globalization.CultureInfo]::InvariantCulture)
            $cached = Invoke-WebRequest "$base/web/" -Headers @{ 'If-Modified-Since' = $since; 'If-None-Match' = '"0123456789abcde"' } -SkipHttpErrorCheck
            Expect ($cached.StatusCode -eq 200 -and (Count ([string]$cached.Content)) -eq 1) "$Name`: a browser with an old cached page still gets the loader"
        }
        Expect ((Count $entry) -eq 1) "$Name`: /web/theme-manager.html still works as fallback"
        Expect ((Invoke-WebRequest "$base/ThemeManager/Client/bootstrap.js" -SkipHttpErrorCheck).StatusCode -eq 200) "$Name`: loader script is served"
        $state = if ($WithFileTransformation) { 'Theme Manager: registered with File Transformation' } else { 'Theme Manager: File Transformation is not installed' }
        Expect ($text -match [regex]::Escape($state)) "$Name`: log explains the state"
        Expect (!($text -match '\[ERR\].*(FileTransformation|ThemeManager)')) "$Name`: no File Transformation or Theme Manager errors"
        Expect ((Get-FileHash -LiteralPath (Join-Path $webRoot 'index.html')).Hash -eq $indexHash) "$Name`: index.html on disk unchanged"
    }
    finally {
        if (!$server.HasExited) { Stop-Process -Id $server.Id; $server.WaitForExit(10000) | Out-Null }
    }
}

Run-Scenario 'with-ft' $true $true
Run-Scenario 'without-ft' $false $true
Run-Scenario 'disabled' $true $false
Write-Output "Completed $($checks.Count) checks."
