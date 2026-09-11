[CmdletBinding()]
param(
    [string]$Repository,
    [string]$Token,
    [switch]$CheckRemote
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repositoryRoot 'ModpackTools.psd1'
$manifest = Import-PowerShellDataFile -LiteralPath $manifestPath
$version = [string]$manifest.ModuleVersion

if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "ModuleVersion '$version' is not a stable semantic version."
}

$tag = "v$version"
$notesRelative = "docs/releases/$version.md"
$notesPath = Join-Path $repositoryRoot $notesRelative
if (-not (Test-Path -LiteralPath $notesPath -PathType Leaf)) {
    throw "Release notes are missing: $notesRelative"
}

$firstLine = Get-Content -LiteralPath $notesPath -Encoding UTF8 -TotalCount 1
if ($firstLine -notmatch '^# (.+)$') {
    throw 'The first release-note line must be the GitHub release title.'
}
$title = $Matches[1]
if ($title -notmatch [regex]::Escape($version)) {
    throw "Release title '$title' does not contain version $version."
}

$releaseExists = $false
$tagExists = $false
if ($CheckRemote) {
    if ([string]::IsNullOrWhiteSpace($Repository)) {
        throw 'Repository is required when checking remote release metadata.'
    }
    if ([string]::IsNullOrWhiteSpace($Token)) {
        throw 'Token is required when checking remote release metadata.'
    }

    $headers = @{
        Accept = 'application/vnd.github+json'
        Authorization = "Bearer $Token"
        'X-GitHub-Api-Version' = '2022-11-28'
    }
    $apiRoot = "https://api.github.com/repos/$Repository"

    $releaseResponse = Invoke-WebRequest -Uri "$apiRoot/releases/tags/$tag" -Headers $headers -SkipHttpErrorCheck
    $releaseExists = switch ([int]$releaseResponse.StatusCode) {
        200 { $true }
        404 { $false }
        default { throw "Could not check release ${tag}: GitHub API returned HTTP $([int]$releaseResponse.StatusCode)." }
    }

    $tagResponse = Invoke-WebRequest -Uri "$apiRoot/git/ref/tags/$tag" -Headers $headers -SkipHttpErrorCheck
    $tagExists = switch ([int]$tagResponse.StatusCode) {
        200 { $true }
        404 { $false }
        default { throw "Could not check tag ${tag}: GitHub API returned HTTP $([int]$tagResponse.StatusCode)." }
    }
}

[pscustomobject]@{
    Version = $version
    Tag = $tag
    Title = $title
    NotesPath = $notesRelative
    ReleaseExists = $releaseExists
    TagExists = $tagExists
}
