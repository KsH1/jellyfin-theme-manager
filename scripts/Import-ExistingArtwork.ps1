[CmdletBinding()]
param(
    [string]$SourceRoot = (Join-Path $PSScriptRoot '..\..'),
    [string]$Destination = (Join-Path $PSScriptRoot '..\ThemePacks')
)
$ErrorActionPreference = 'Stop'
$SourceRoot = [IO.Path]::GetFullPath($SourceRoot)
$Destination = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $Destination) { throw "Destination already exists; refusing to overwrite: $Destination" }

# Only read the old files. Never execute the old seasonal script or access its API token.
$months = @('January','February','March','April','May','June','July','August','September','October','November','December')
$accents = @('#82e8ff','#a6dfff','#78cfd6','#76dfc3','#65e2ba','#59dce8','#53d7e8','#69cfc5','#68bfaa','#d99a54','#75c9dc','#8de5ff')
$secondary = @('#9a79e8','#d58aae','#b58d57','#e0c25d','#c08ce5','#f09a67','#ff9a64','#e4a54f','#c98245','#a45e92','#dc9854','#d5b66e')
$packs = @()
for ($month = 1; $month -le 12; $month++) {
    $number = '{0:D2}' -f $month
    $packs += @{ Id="month-$number"; Name=$months[$month-1]; Folder="Month$number"; Background="jellyfin-month-$number-$($months[$month-1].ToLowerInvariant())-v1.png"; Accent=$accents[$month-1]; Secondary=$secondary[$month-1]; Rule=@{kind='Month';month=$month;priority=0} }
}
$packs += @(
    @{Id='newyear';Name='New Year';Folder='NewYear';Accent='#ffd76b';Secondary='#8de5ff';Rule=@{kind='Range';start='12-30';end='01-02';priority=100}},
    @{Id='valentine';Name='Valentine';Folder='Valentine';Accent='#ff79b0';Secondary='#bb79ff';Rule=@{kind='Range';start='02-14';end='02-14';priority=90}},
    @{Id='fastelavn';Name='Fastelavn';Folder='Fastelavn';Accent='#b594ff';Secondary='#ffcc70';Rule=@{kind='EasterOffset';startOffset=-51;endOffset=-49;priority=80}},
    @{Id='easter';Name='Easter';Folder='Easter';Accent='#ffe182';Secondary='#a3e8c1';Rule=@{kind='EasterOffset';startOffset=-3;endOffset=1;priority=80}},
    @{Id='sankthans';Name='Midsummer';Folder='SanktHans';Accent='#ffb569';Secondary='#68deeb';Rule=@{kind='Range';start='06-23';end='06-24';priority=80}},
    @{Id='halloween';Name='Halloween';Folder='Halloween';Accent='#ff983f';Secondary='#a879ff';Rule=@{kind='Range';start='10-01';end='10-31';priority=80}},
    @{Id='christmas';Name='Christmas';Folder='Christmas';Accent='#e7c57b';Secondary='#83ca9d';Rule=@{kind='Range';start='11-24';end='12-26';priority=80}}
)
# Validate every input before writing. Asset names remain English; source artwork is unchanged.
foreach ($pack in $packs) {
    if (!$pack.Background) { $pack.Background = "jellyfin-season-$($pack.Id)-v1.png" }
    $pack.BackgroundPath = Join-Path $SourceRoot "outputs\seasonal\$($pack.Background)"
    $pack.BannersPath = Join-Path $SourceRoot "seasonal-theme\library-banners\$($pack.Folder)"
    foreach ($path in @($pack.BackgroundPath,(Join-Path $pack.BannersPath 'collections.png'),(Join-Path $pack.BannersPath 'movies.png'),(Join-Path $pack.BannersPath 'tv-shows.png'))) {
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing input: $path" }
    }
}
New-Item -ItemType Directory -Path $Destination | Out-Null
foreach ($pack in $packs) {
    $target = Join-Path $Destination $pack.Id
    New-Item -ItemType Directory -Path $target | Out-Null
    Copy-Item -LiteralPath $pack.BackgroundPath -Destination (Join-Path $target 'background.png')
    foreach ($name in @('collections.png','movies.png','tv-shows.png')) { Copy-Item -LiteralPath (Join-Path $pack.BannersPath $name) -Destination (Join-Path $target $name) }
    $manifest = [ordered]@{
        schemaVersion=1;id=$pack.Id;name=$pack.Name;version='1.0.0';author='Local artwork';category='Calendar';calendar=$pack.Rule
        palette=@{accent=$pack.Accent;secondary=$pack.Secondary;surface='#101722';text='#ffffff';background='#080b12'}
        assets=@{background='background.png';splash='background.png';thumbnail='background.png';collections='collections.png';movies='movies.png';tvShows='tv-shows.png'}
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $target 'theme.json') -Encoding utf8
}

# These are functional palette presets, not a claim that ten new artwork sets exist.
$presets = @(
    @('cinematic-night','Cinematic Night','#c9a66b','#8b7bd8','#090913'),
    @('deep-space','Deep Space','#63d5ff','#9377ff','#060b1e'),
    @('epic-fantasy','Epic Fantasy','#d9bd73','#78c1bb','#0b1618'),
    @('anime-city','Anime City','#ff7bb9','#78dcec','#150b24'),
    @('nordic-noir','Nordic Noir','#8eafbd','#adb5be','#0b1115'),
    @('retro-neon','Retro Neon','#ed72fb','#62e4ee','#170b2b'),
    @('cozy-evening','Cozy Evening','#e2af79','#c78b78','#1a1210'),
    @('nature-escape','Nature Escape','#8ed4a2','#c4bb79','#091710'),
    @('minimal-dark','Minimal Dark','#bbbbbb','#9099a5','#0c0d10'),
    @('ocean-blue','Ocean Blue','#68e0d8','#6095df','#06151e')
)
foreach ($preset in $presets) {
    $target = Join-Path $Destination $preset[0]; New-Item -ItemType Directory -Path $target | Out-Null
    [ordered]@{schemaVersion=1;id=$preset[0];name=$preset[1];version='0.1.0';author='Local';category='Permanent';palette=@{accent=$preset[2];secondary=$preset[3];background=$preset[4];surface='#151922';text='#ffffff'};assets=@{}} |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $target 'theme.json') -Encoding utf8
}
Write-Output "Prepared 19 calendar packs (57 original banners) and 10 palette presets in $Destination"
