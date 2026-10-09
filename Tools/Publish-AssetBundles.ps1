param(
    [string]$Platform = 'StandaloneWindows64',
    [string]$Repository = 'https://github.com/Lemon-Bear34/LemonBear_Adventure.git',
    [string]$ProxyUrl = '',
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
# Honor the user's enabled Windows proxy for this invocation only.
if (-not $ProxyUrl) {
    $proxySettings = Get-ItemProperty 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Internet Settings' -ErrorAction SilentlyContinue
    if ($proxySettings.ProxyEnable -eq 1 -and $proxySettings.ProxyServer -match '^([^=;]+):(\d+)$') {
        $ProxyUrl = 'http://' + $proxySettings.ProxyServer
    }
}
$gitNetworkConfig = @()
if ($ProxyUrl) { $gitNetworkConfig = @('-c', "http.proxy=$ProxyUrl") }
if ($Platform -notmatch '^[A-Za-z0-9]+$') { throw 'Invalid platform name.' }
$artifactPath = Join-Path $projectRoot "Build/AssetBundles/$Platform"
$catalogPath = Join-Path $artifactPath 'catalog.json'
if (-not (Test-Path -LiteralPath $catalogPath)) { throw 'Build bundles in Unity first: LemonBear > Build AssetBundles.' }
$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
if ($catalog.platform -ne $Platform -or $catalog.hash -notmatch '^[a-f0-9]{32}$' -or $catalog.bundle -ne "gamecontent-$($catalog.hash).bundle") {
    throw 'Invalid build catalog.'
}
$bundlePath = Join-Path $artifactPath $catalog.bundle
if (-not (Test-Path -LiteralPath $bundlePath)) { throw "Missing bundle: $bundlePath" }

function Invoke-ResourceGit {
    param([string[]]$GitArguments)
    & git @gitNetworkConfig @GitArguments
    if ($LASTEXITCODE -ne 0) { throw "git failed: $($GitArguments[0])" }
}

# Use a separate checkout so publishing cannot stage or commit project changes.
$publishPath = Join-Path $projectRoot ('Build/ResourcePublish/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $publishPath -Force | Out-Null
Invoke-ResourceGit -GitArguments @('-C', $publishPath, 'init', '-b', 'ab-resources')
Invoke-ResourceGit -GitArguments @('-C', $publishPath, 'remote', 'add', 'origin', $Repository)
$branch = Invoke-ResourceGit -GitArguments @('ls-remote', '--heads', $Repository, 'refs/heads/ab-resources')
if ($branch) {
    Invoke-ResourceGit -GitArguments @('-C', $publishPath, 'fetch', '--depth=1', 'origin', 'ab-resources')
    Invoke-ResourceGit -GitArguments @('-C', $publishPath, 'checkout', '-B', 'ab-resources', 'FETCH_HEAD')
}
$authorName = & git -C $projectRoot config user.name
$authorEmail = & git -C $projectRoot config user.email
if (-not $authorName -or -not $authorEmail) { throw 'Configure git user.name and user.email before publishing.' }
Invoke-ResourceGit -GitArguments @('-C', $publishPath, 'config', 'user.name', $authorName)
Invoke-ResourceGit -GitArguments @('-C', $publishPath, 'config', 'user.email', $authorEmail)
$destination = Join-Path $publishPath $Platform
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath $bundlePath -Destination (Join-Path $destination $catalog.bundle)
Copy-Item -LiteralPath $catalogPath -Destination (Join-Path $destination 'catalog.json')
@'
# LemonBear runtime resources

This branch contains Unity AssetBundles and per-platform catalogs. Game startup
checks catalog.json and loads the bundle named by its content hash. Keep published
bundle filenames immutable so older clients and cached catalogs continue to work.
Rebuild with the same Unity version as the client and publish using
Tools/Publish-AssetBundles.ps1 from the project checkout.
'@ | Set-Content -LiteralPath (Join-Path $publishPath 'README.md') -Encoding utf8
Invoke-ResourceGit -GitArguments @('-C', $publishPath, 'add', '--', "$Platform/catalog.json", "$Platform/$($catalog.bundle)", 'README.md')
& git -C $publishPath diff --cached --quiet
if ($LASTEXITCODE -eq 0) { Write-Host 'This resource version is already published.'; exit 0 }
if ($LASTEXITCODE -ne 1) { throw 'Cannot inspect staged resource changes.' }
Invoke-ResourceGit -GitArguments @('-C', $publishPath, 'commit', '-m', "Publish $Platform resources $($catalog.hash)")
if ($PrepareOnly) { Write-Host "Prepared resource commit in: $publishPath"; exit 0 }
Invoke-ResourceGit -GitArguments @('-C', $publishPath, 'push', 'origin', 'HEAD:refs/heads/ab-resources')
Write-Host "Published: https://github.com/Lemon-Bear34/LemonBear_Adventure/tree/ab-resources/$Platform"
