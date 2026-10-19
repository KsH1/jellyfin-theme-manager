[CmdletBinding()]
param(
    [string]$JellyfinExecutable = 'C:\Program Files\Jellyfin\Server\jellyfin.exe',
    [int]$Port = 18196,
    [ValidatePattern('^(/[a-z0-9-]+)?$')][string]$BaseUrl = '',
    [switch]$KeepRunning
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runRoot = Join-Path $projectRoot ('test-server\run-' + [Guid]::NewGuid().ToString('N'))
$configRoot = Join-Path $runRoot 'config'
$pluginRoot = Join-Path $runRoot 'plugins\ThemeManager_0.1.0.0'
$packsRoot = Join-Path $runRoot 'data\theme-manager\packs'
if ([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object Port -eq $Port) { throw "Port $Port is already in use." }
New-Item -ItemType Directory -Path $configRoot,$pluginRoot,$packsRoot | Out-Null
# All configuration/database/cache/log paths are new isolated workspace folders.
@"
<?xml version="1.0" encoding="utf-8"?>
<NetworkConfiguration>
 <BaseUrl>$BaseUrl</BaseUrl>
 <InternalHttpPort>$Port</InternalHttpPort><PublicHttpPort>$Port</PublicHttpPort>
 <EnableIPv4>true</EnableIPv4><EnableIPv6>false</EnableIPv6>
 <EnableHttps>false</EnableHttps><EnableRemoteAccess>false</EnableRemoteAccess>
 <AutoDiscovery>false</AutoDiscovery><EnableUPnP>false</EnableUPnP>
 <LocalNetworkAddresses><string>127.0.0.1</string></LocalNetworkAddresses>
</NetworkConfiguration>
"@ | Set-Content -LiteralPath (Join-Path $configRoot 'network.xml') -Encoding utf8
foreach ($name in @('Jellyfin.Plugin.ThemeManager.dll','ThemeManager.Core.dll')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot "src\Jellyfin.Plugin.ThemeManager\bin\Release\net10.0\$name") -Destination $pluginRoot
}
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'ThemePacks') -Directory | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $packsRoot -Recurse }
$webRoot=Join-Path ([IO.Path]::GetDirectoryName($JellyfinExecutable)) 'jellyfin-web'
$webHash=(Get-FileHash -LiteralPath (Join-Path $webRoot 'index.html')).Hash
$arguments = @('--datadir',('"'+$runRoot+'"'),'--configdir',('"'+$configRoot+'"'),'--cachedir',('"'+(Join-Path $runRoot 'cache')+'"'),'--logdir',('"'+(Join-Path $runRoot 'log')+'"'),'--webdir',('"'+$webRoot+'"'),'--nonetchange','--service')
$server = Start-Process -FilePath $JellyfinExecutable -ArgumentList $arguments -WorkingDirectory $runRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runRoot 'stdout.log') -RedirectStandardError (Join-Path $runRoot 'stderr.log')
$base = "http://127.0.0.1:$Port$BaseUrl"
$completed=$false
$checks = [Collections.Generic.List[string]]::new()
function Expect($Condition,[string]$Name) { if (!$Condition) { throw "FAILED: $Name" }; $checks.Add($Name); Write-Output "PASS: $Name" }
function Request([string]$Path,[string]$Method='GET',$Body=$null,[hashtable]$Headers=@{}) {
    $parameters=@{Uri="$base/$Path";Method=$Method;Headers=$Headers;TimeoutSec=15}
    if ($null -ne $Body) { $parameters.Body=($Body | ConvertTo-Json -Depth 8);$parameters.ContentType='application/json' }
    $response=Invoke-WebRequest @parameters -SkipHttpErrorCheck
    if ([int]$response.StatusCode -ge 400) { throw "HTTP $($response.StatusCode): $Method $Path" }
    if ($response.Headers.'Content-Type' -match 'application/json') { return $response.Content | ConvertFrom-Json }
    return $response.Content
}
function Status([string]$Path,[string]$Method='GET',[hashtable]$Headers=@{}) {
    (Invoke-WebRequest -Uri "$base/$Path" -Method $Method -Headers $Headers -SkipHttpErrorCheck -TimeoutSec 15).StatusCode
}
try {
    $ready=$false
    for ($attempt=0;$attempt -lt 45;$attempt++) {
        if ($server.HasExited) { throw "Test server exited. Inspect $runRoot\stdout.log" }
        try { $info=Request 'System/Info/Public';$null=Request 'Startup/User';$ready=$true;break } catch { Start-Sleep -Milliseconds 500 }
    }
    Expect $ready 'Isolated Jellyfin responds'
    Expect ($info.Version -eq '12.2.0') 'Correct Jellyfin host version'
    $listeners=@([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object Port -eq $Port)
    Expect ($listeners.Count -gt 0 -and ($listeners | Where-Object { $_.Address.ToString() -ne '127.0.0.1' }).Count -eq 0) 'Test server binds only loopback'
    $password=[Guid]::NewGuid().ToString('N')+'!aA9'
    $null=Request 'Startup/User' 'GET'
    $null=Request 'Startup/User' 'POST' @{Name='tm-admin';Password=$password}
    $null=Request 'Startup/Complete' 'POST'
    $clientHeaders=@{Authorization='MediaBrowser Client="ThemeManagerSmoke", Device="IsolatedTest", DeviceId="theme-manager-test", Version="0.1.0"'}
    $login=Request 'Users/AuthenticateByName' 'POST' @{Username='tm-admin';Pw=$password} $clientHeaders
    Expect (![string]::IsNullOrWhiteSpace($login.AccessToken)) 'Admin login returns a session token'
    $admin=@{Authorization=$clientHeaders.Authorization + ', Token="' + $login.AccessToken + '"'}
    $plugins=@(Request 'Plugins' 'GET' $null $admin)
    $plugin=$plugins | Where-Object { ([string]$_.Id).Replace('-','') -eq '8fa53208c9f34e9998582963ed658aae' }
    Write-Output ($plugin | Select-Object Name,Id,Status,Version | ConvertTo-Json -Compress)
    Expect ($plugin.Status -eq 'Active') 'Plugin discovered and active'
    Expect ((Status 'ThemeManager/Catalog') -eq 401) 'Anonymous catalog access rejected'
    $catalog=Request 'ThemeManager/Catalog' 'GET' $null $admin
    Expect ($catalog.themes.Count -eq 29) 'All 29 theme definitions loaded'
    Expect (@($catalog.themes | Where-Object category -eq 'Calendar').Count -eq 19) '19 artwork calendar packs'
    $reload=Request 'ThemeManager/Admin/Reload' 'POST' $null $admin
    Expect ($reload.problems.Count -eq 0) 'All real manifests and image headers valid'
    Expect (($catalog | ConvertTo-Json -Depth 12) -notmatch 'C:\\') 'Catalog does not expose filesystem paths'
    $asset=Invoke-WebRequest -Uri "$base/ThemeManager/Themes/halloween/Assets/movies" -Headers $admin
    Expect ($asset.Headers.'Content-Type' -contains 'image/png') 'Authenticated artwork delivery'
    $revision=($catalog.themes | Where-Object id -eq 'halloween').assets.movies.revision
    $preview=Invoke-WebRequest -Uri "$base/ThemeManager/Themes/halloween/Assets/movies/Preview?v=$revision" -Headers $admin
    Expect ($preview.Headers.'Content-Type' -contains 'image/webp' -and $preview.RawContentStream.Length -lt $asset.RawContentStream.Length / 5) 'Reduced WebP preview from Jellyfin encoder'
    Expect ($preview.Headers.'Cache-Control' -join ',' -match 'immutable') 'Revisioned preview is cacheable'
    Expect ((Status 'ThemeManager/Themes/halloween/Assets/movies/Preview') -eq 401) 'Anonymous preview access rejected'
    $bgRevision=($catalog.themes | Where-Object id -eq 'halloween').assets.background.revision
    $bgPreview=Invoke-WebRequest -Uri "$base/ThemeManager/Themes/halloween/Assets/background/Preview?v=$bgRevision" -Headers $admin
    $thumb=Invoke-WebRequest -Uri "$base/ThemeManager/Themes/halloween/Assets/background/Preview?size=thumb&v=$bgRevision" -Headers $admin
    Expect ($thumb.Headers.'Content-Type' -contains 'image/webp' -and $thumb.RawContentStream.Length -lt $bgPreview.RawContentStream.Length / 2) 'Small thumbnail for picker tiles'
    # Holidays left out of Automatic Seasonal. Date-independent: whatever holiday is today is excluded and must give way to its month.
    $settings=Request 'ThemeManager/Admin/Settings' 'GET' $null $admin
    Expect ($settings.PSObject.Properties.Name -contains 'automaticExcluded') 'Admin settings list holidays left out of Automatic Seasonal'
    Expect ($settings.allowEffects -eq $false -and (Request 'ThemeManager/Effective' 'GET' $null $admin).effects -eq $false) 'Seasonal effects are off by default'
    $null=Request 'ThemeManager/Me' 'PUT' @{mode='Automatic'} $admin
    $null=Request 'ThemeManager/Admin/Settings' 'PUT' @{choice=@{mode='Automatic'};timeZoneId='Europe/Copenhagen';automaticExcluded=@()} $admin
    $today=(Request 'ThemeManager/Effective' 'GET' $null $admin).resolution.themeId
    if ($today -notmatch '^month-\d\d$') {
        $null=Request 'ThemeManager/Admin/Settings' 'PUT' @{choice=@{mode='Automatic'};timeZoneId='Europe/Copenhagen';automaticExcluded=@($today)} $admin
        Expect ((Request 'ThemeManager/Effective' 'GET' $null $admin).resolution.themeId -match '^month-\d\d$') "Excluded holiday ($today) gives way to the month"
        $null=Request 'ThemeManager/Admin/Settings' 'PUT' @{choice=@{mode='Automatic'};timeZoneId='Europe/Copenhagen';automaticExcluded=@()} $admin
    } else { Write-Output "SKIP: no holiday today ($today), exclusion not exercised" }
    $page=Request 'web/ConfigurationPage?name=theme-manager' 'GET' $null $admin
    Expect ($page -match 'Theme Manager' -and $page -match 'tm-gallery') 'Embedded dashboard resource is served'
    $null=Request 'ThemeManager/Me' 'PUT' @{mode='Fixed';themeId='halloween'} $admin
    $effective=Request 'ThemeManager/Effective' 'GET' $null $admin
    Expect ($effective.theme.id -eq 'halloween') 'Personal fixed choice resolves'
    $null=Request 'ThemeManager/Admin/Settings' 'PUT' @{choice=@{mode='Original'};timeZoneId='Europe/Copenhagen'} $admin
    $effective=Request 'ThemeManager/Effective' 'GET' $null $admin
    Expect ($effective.theme.id -eq 'halloween') 'Personal selection overrides server setting'
    $newUser=Request 'Users/New' 'POST' @{Name='tm-viewer';Password=$password} $admin
    $viewerLogin=Request 'Users/AuthenticateByName' 'POST' @{Username='tm-viewer';Pw=$password} $clientHeaders
    $viewer=@{Authorization=$clientHeaders.Authorization + ', Token="' + $viewerLogin.AccessToken + '"'}
    $mine=Request 'ThemeManager/Me' 'GET' $null $viewer
    Expect ($mine.mode -eq 'FollowServer') 'Second user starts independently'
    Expect ((Status 'ThemeManager/Admin/Reload' 'POST' $viewer) -eq 403) 'Non-admin cannot refresh catalog'
    Expect ((Status 'ThemeManager/Admin/Settings' 'GET' $viewer) -eq 403) 'Non-admin cannot read admin settings'
    $null=Request 'ThemeManager/Me' 'PUT' @{mode='Fixed';themeId='ocean-blue'} $viewer
    $mine=Request 'ThemeManager/Me' 'GET' $null $admin
    Expect ($mine.themeId -eq 'halloween') 'Second user cannot overwrite first user preference'
    $effective=Request 'ThemeManager/Effective' 'GET' $null $viewer
    Expect ($effective.theme.id -eq 'ocean-blue') 'Ordinary user preference API works'
    $preview=Request 'ThemeManager/Preview' 'POST' @{mode='Fixed';themeId='christmas'} $viewer
    Expect ($preview.theme.id -eq 'christmas') 'Ordinary user can preview another theme'
    $mine=Request 'ThemeManager/Me' 'GET' $null $viewer
    Expect ($mine.themeId -eq 'ocean-blue') 'Preview does not change the saved preference'
    $shell=Request 'web/theme-manager.html'
    Expect ($shell -match 'defer src="../ThemeManager/Client/bootstrap.js"') 'Opt-in web entry adds the client module'
    $normal=Request 'web/index.html'
    Expect ($normal -notmatch 'ThemeManager/Client/bootstrap.js') 'Normal Jellyfin entry is untouched'
    Expect ((Status 'ThemeManager/Client/client.mjs') -eq 200) 'Public client module is available without login'
    Expect ((Status 'ThemeManager/Client/not-allowed.txt') -eq 404) 'Client resources use an explicit allow-list'
    Expect ((Get-FileHash -LiteralPath (Join-Path $webRoot 'index.html')).Hash -eq $webHash) 'Installed Jellyfin index was not modified'
    $invalid=Invoke-WebRequest -Uri "$base/ThemeManager/Me" -Method Put -Headers $viewer -ContentType 'application/json' -Body '{"mode":"Fixed","themeId":"does-not-exist"}' -SkipHttpErrorCheck
    Expect ($invalid.StatusCode -eq 400) 'Unknown theme selection rejected'
    $forbidden=Invoke-WebRequest -Uri "$base/ThemeManager/Admin/Settings" -Method Put -Headers $viewer -ContentType 'application/json' -Body '{"choice":{"mode":"Original"},"timeZoneId":"Europe/Copenhagen"}' -SkipHttpErrorCheck
    Expect ($forbidden.StatusCode -eq 403) 'Non-admin cannot change server default'
    # Custom calendars: shared ones made by an administrator, one personal calendar per user.
    $yearRound=@{name='Kids';fallbackThemeId='nordic-noir';entries=@(@{themeId='deep-space';rule=@{kind='Range';start='01-01';end='12-31'}})}
    function Send([string]$Path,[string]$Method,$Body,[hashtable]$Headers) { (Invoke-WebRequest -Uri "$base/$Path" -Method $Method -Headers $Headers -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 8) -SkipHttpErrorCheck).StatusCode }
    Expect ((Send 'ThemeManager/Admin/Schedules/kids' 'PUT' $yearRound $viewer) -eq 403) 'Non-admin cannot create a shared calendar'
    Expect ((Send 'ThemeManager/Admin/Schedules/kids' 'PUT' @{name='Bad';entries=@(@{themeId='does-not-exist';rule=@{kind='Month';month=1}})} $admin) -eq 400) 'Calendar with unknown theme rejected'
    Expect ((Send 'ThemeManager/Admin/Schedules/kids' 'PUT' $yearRound $admin) -eq 204) 'Administrator creates a shared calendar'
    $calendars=Request 'ThemeManager/Schedules' 'GET' $null $viewer
    Expect (@($calendars.shared).Count -eq 1 -and $calendars.shared[0].id -eq 'kids' -and $null -eq $calendars.personal) 'Users see shared calendars'
    $null=Request 'ThemeManager/Me' 'PUT' @{mode='Schedule';scheduleId='kids'} $viewer
    Expect ((Request 'ThemeManager/Effective' 'GET' $null $viewer).theme.id -eq 'deep-space') 'User follows a shared calendar'
    $timeline=Request 'ThemeManager/Schedules/Test' 'POST' @{schedule=$yearRound;from='2026-12-30'} $viewer
    Expect ($timeline.timeline[0].themeId -eq 'deep-space' -and @($timeline.timeline | Where-Object themeId -eq 'deep-space').Count -ge 1) 'Calendar test shows the year ahead'
    Expect ((Send 'ThemeManager/Me/Schedule' 'PUT' @{name='Mine';fallbackThemeId='retro-neon';entries=@()} $viewer) -eq 204) 'User saves a personal calendar'
    $null=Request 'ThemeManager/Me' 'PUT' @{mode='Schedule';scheduleId='personal'} $viewer
    Expect ((Request 'ThemeManager/Effective' 'GET' $null $viewer).theme.id -eq 'retro-neon') 'User follows their personal calendar'
    Expect ((Send 'ThemeManager/Me' 'PUT' @{mode='Schedule';scheduleId='personal'} $admin) -eq 400) 'Personal calendar is private to its owner'
    $settings=Request 'ThemeManager/Admin/Settings' 'GET' $null $admin
    Expect ((Send 'ThemeManager/Admin/Settings' 'PUT' @{choice=@{mode='Schedule';scheduleId='personal'};timeZoneId='Europe/Copenhagen'} $admin) -eq 400) 'Server default cannot use a personal calendar'
    Expect ((Send 'ThemeManager/Admin/Settings' 'PUT' @{choice=@{mode='Schedule';scheduleId='kids'};timeZoneId='Europe/Copenhagen'} $admin) -eq 204) 'Server default can use a shared calendar'
    Expect ((Status 'ThemeManager/Admin/Schedules/kids' 'DELETE' $admin) -eq 409) 'Calendar in use as server default cannot be deleted'
    $null=Request 'ThemeManager/Admin/Settings' 'PUT' $settings $admin
    Expect ((Status 'ThemeManager/Admin/Schedules/kids' 'DELETE' $admin) -eq 204) 'Administrator deletes a shared calendar'
    Expect ((Status 'ThemeManager/Me/Schedule' 'DELETE' $viewer) -eq 204 -and (Request 'ThemeManager/Me' 'GET' $null $viewer).mode -eq 'FollowServer') 'Deleting the personal calendar resets a choice that used it'
    $null=Request 'ThemeManager/Me' 'PUT' @{mode='Fixed';themeId='ocean-blue'} $viewer
    $zip=Join-Path $runRoot 'sample-ocean.zip'
    Compress-Archive -LiteralPath (Join-Path $projectRoot 'examples\sample-ocean\theme.json') -DestinationPath $zip
    $denied=Invoke-WebRequest -Uri "$base/ThemeManager/Admin/Import" -Method Post -Headers $viewer -ContentType 'application/zip' -InFile $zip -SkipHttpErrorCheck
    Expect ($denied.StatusCode -eq 403) 'Non-admin ZIP import rejected'
    $import=Invoke-WebRequest -Uri "$base/ThemeManager/Admin/Import" -Method Post -Headers $admin -ContentType 'application/zip' -InFile $zip -SkipHttpErrorCheck
    Expect ($import.StatusCode -eq 200) 'Admin imports ZIP through live API'
    $afterImport=Request 'ThemeManager/Catalog' 'GET' $null $admin
    Expect (@($afterImport.themes | Where-Object id -eq 'sample-ocean').Count -eq 1) 'New pack appears without server restart'
    $duplicate=Invoke-WebRequest -Uri "$base/ThemeManager/Admin/Import" -Method Post -Headers $admin -ContentType 'application/zip' -InFile $zip -SkipHttpErrorCheck
    Expect ($duplicate.StatusCode -eq 400) 'Duplicate import rejected without overwriting'
    $replaced=Invoke-WebRequest -Uri "$base/ThemeManager/Admin/Import?replace=true" -Method Post -Headers $admin -ContentType 'application/zip' -InFile $zip -SkipHttpErrorCheck
    Expect ($replaced.StatusCode -eq 200) 'Import with replace updates an installed theme'
    Expect ((Status 'ThemeManager/Admin/Themes/retro-neon' 'DELETE' $viewer) -eq 403) 'Non-admin cannot delete a theme'
    Expect ((Send 'ThemeManager/Admin/Themes/ocean-blue/Hidden' 'PUT' @{hidden=$true} $viewer) -eq 403) 'Non-admin cannot hide a theme'
    Expect ((Send 'ThemeManager/Admin/Themes/ocean-blue/Hidden' 'PUT' @{hidden=$true} $admin) -eq 204 -and (Send 'ThemeManager/Admin/Themes/retro-neon/Hidden' 'PUT' @{hidden=$true} $admin) -eq 204) 'Administrator hides themes'
    Expect ((@((Request 'ThemeManager/Catalog' 'GET' $null $viewer).themes | Where-Object hidden).id -join ',') -eq 'ocean-blue,retro-neon') 'Catalog marks hidden themes'
    Expect ((Send 'ThemeManager/Me' 'PUT' @{mode='Fixed';themeId='ocean-blue'} $viewer) -eq 204) 'User keeps a hidden theme they already chose'
    Expect ((Send 'ThemeManager/Me' 'PUT' @{mode='Fixed';themeId='retro-neon'} $viewer) -eq 400) 'User cannot newly choose a hidden theme'
    $null=Send 'ThemeManager/Admin/Themes/ocean-blue/Hidden' 'PUT' @{hidden=$false} $admin;$null=Send 'ThemeManager/Admin/Themes/retro-neon/Hidden' 'PUT' @{hidden=$false} $admin
    $exportZip=Join-Path $runRoot 'exported-ocean.zip'
    $export=Invoke-WebRequest -Uri "$base/ThemeManager/Admin/Themes/sample-ocean/Export" -Headers $admin -OutFile $exportZip -PassThru -SkipHttpErrorCheck
    Expect ($export.StatusCode -eq 200 -and (Get-Item $exportZip).Length -gt 0) 'Administrator exports a theme as ZIP'
    Expect ((Status 'ThemeManager/Admin/Themes/sample-ocean' 'DELETE' $admin) -eq 204 -and @((Request 'ThemeManager/Catalog' 'GET' $null $admin).themes | Where-Object id -eq 'sample-ocean').Count -eq 0) 'Administrator deletes a theme'
    $reimport=Invoke-WebRequest -Uri "$base/ThemeManager/Admin/Import" -Method Post -Headers $admin -ContentType 'application/zip' -InFile $exportZip -SkipHttpErrorCheck
    Expect ($reimport.StatusCode -eq 200) 'Exported theme imports again'
    $settings=Request 'ThemeManager/Admin/Settings' 'GET' $null $admin
    Expect ($settings.choice.mode -eq 'Original') 'Rejected non-admin update preserved server default'
    $null=Request 'Auth/Keys?app=ThemeManagerSmoke' 'POST' $null $admin
    $keys=Request 'Auth/Keys' 'GET' $null $admin
    $testKey=($keys.Items | Where-Object AppName -eq 'ThemeManagerSmoke' | Select-Object -First 1).AccessToken
    Expect (![string]::IsNullOrWhiteSpace($testKey)) 'Isolated API key created for access test'
    $keyHeader=@{Authorization=$clientHeaders.Authorization + ', Token="' + $testKey + '"'}
    Expect ((Status 'ThemeManager/Me' 'GET' $keyHeader) -eq 403) 'Server API key cannot impersonate a personal user'
    $null=Request 'ThemeManager/Admin/Settings' 'PUT' @{choice=@{mode='Automatic'};timeZoneId='Europe/Copenhagen'} $admin
    $tasks=Request 'ScheduledTasks' 'GET' $null $admin
    Expect (@($tasks | Where-Object Key -eq 'ThemeManagerRefreshCatalog').Count -eq 1) 'Native Jellyfin scheduled task registered'
    if ($KeepRunning) {
        foreach ($library in @(@('Movies','movies'),@('TV Shows','tvshows'))) {
            $mediaPath=Join-Path $runRoot ('media\'+$library[1]);New-Item -ItemType Directory -Path $mediaPath | Out-Null
            $null=Request ('Library/VirtualFolders?name='+[uri]::EscapeDataString($library[0])+'&collectionType='+$library[1]+'&refreshLibrary=false') 'POST' @{LibraryOptions=@{PathInfos=@(@{Path=$mediaPath});EnableRealtimeMonitor=$false}} $admin
        }
        # Synthetic credentials, valid only for this empty loopback test server. Never packaged.
        @{ProcessId=$server.Id;BaseUrl=$base;RunRoot=$runRoot;Username='tm-viewer';AdminUsername='tm-admin';Password=$password} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'browser-session.json') -Encoding utf8
    }
    $checks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'passed-checks.json') -Encoding utf8
    $completed=$true
    Write-Output "Completed $($checks.Count) checks. Evidence: $runRoot"
}
finally {
    # Stop only the process created by this run. Never find/stop the production Jellyfin process.
    if (!$KeepRunning -or !$completed) {
        if (!$server.HasExited) { Stop-Process -Id $server.Id; $server.WaitForExit(10000) | Out-Null }
        Write-Output "Isolated test server stopped. Data retained in $runRoot"
    } else { Write-Output "Browser test server remains running at $base/web/theme-manager.html; process $($server.Id). Session metadata: $runRoot\browser-session.json" }
}
