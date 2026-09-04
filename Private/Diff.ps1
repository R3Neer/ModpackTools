function Get-Sha256HexFromStream {
    param([Parameter(Mandatory)][System.IO.Stream]$Stream)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [Convert]::ToHexString($algorithm.ComputeHash($Stream)).ToLowerInvariant()
    }
    finally { $algorithm.Dispose() }
}

function Get-Sha256HexFromText {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [Convert]::ToHexString($algorithm.ComputeHash($bytes)).ToLowerInvariant()
    }
    finally { $algorithm.Dispose() }
}

function Get-MrpackFileFingerprint {
    param([Parameter(Mandatory)]$File)

    $parts = [System.Collections.Generic.List[string]]::new()
    foreach ($property in @($File.hashes.PSObject.Properties | Sort-Object Name)) {
        $parts.Add("hash:$($property.Name)=$($property.Value)")
    }
    foreach ($download in @($File.downloads | Sort-Object)) { $parts.Add("download=$download") }
    foreach ($property in @($File.env.PSObject.Properties | Sort-Object Name)) {
        $parts.Add("env:$($property.Name)=$($property.Value)")
    }
    $parts.Add("size=$($File.fileSize)")
    return Get-Sha256HexFromText ($parts -join "`n")
}

function Get-MrpackSnapshot {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { Throw-MpError -Message "MRPack file '$Path' does not exist" -Hint 'modpack build' -ErrorId 'Diff.ArtifactNotFound' -Category ObjectNotFound -TargetObject $Path }
    $records = [System.Collections.Generic.List[object]]::new()
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $manifestEntry = $archive.GetEntry('modrinth.index.json')
        if (-not $manifestEntry) { Throw-MpError -Message "MRPack file '$Path' does not contain 'modrinth.index.json'" -Hint 'modpack build' -ErrorId 'Diff.ManifestMissing' -Category InvalidData -TargetObject $Path }
        $reader = [System.IO.StreamReader]::new($manifestEntry.Open(), [System.Text.Encoding]::UTF8)
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json }
        finally { $reader.Dispose() }

        foreach ($property in @('name', 'versionId')) {
            $records.Add([pscustomobject]@{
                Key = "pack:$property"; Kind = 'PACK'; Path = $property; Fingerprint = [string]$manifest.$property
            })
        }
        foreach ($dependency in @($manifest.dependencies.PSObject.Properties | Sort-Object Name)) {
            $records.Add([pscustomobject]@{
                Key = "dependency:$($dependency.Name)"; Kind = 'DEPENDENCY'; Path = $dependency.Name; Fingerprint = [string]$dependency.Value
            })
        }
        foreach ($file in @($manifest.files)) {
            $kind = switch -Regex ([string]$file.path) {
                '^mods/'          { 'MOD'; break }
                '^resourcepacks/' { 'RESOURCE'; break }
                '^shaderpacks/'   { 'SHADER'; break }
                default           { 'FILE' }
            }
            $records.Add([pscustomobject]@{
                Key = "manifest:$($file.path)"; Kind = $kind; Path = [string]$file.path; Fingerprint = Get-MrpackFileFingerprint $file
            })
        }
        foreach ($entry in @($archive.Entries | Where-Object { $_.FullName.StartsWith('overrides/') -and -not $_.FullName.EndsWith('/') })) {
            $relative = $entry.FullName.Substring('overrides/'.Length)
            $kind = switch -Regex ($relative) {
                '^mods/'          { 'MOD'; break }
                '^resourcepacks/' { 'RESOURCE'; break }
                '^shaderpacks/'   { 'SHADER'; break }
                '^config/'        { 'CONFIG'; break }
                '^\.modpack/'     { 'METADATA'; break }
                default           { 'OVERRIDE' }
            }
            $stream = $entry.Open()
            try { $fingerprint = Get-Sha256HexFromStream $stream }
            finally { $stream.Dispose() }
            $records.Add([pscustomobject]@{
                Key = "override:$relative"; Kind = $kind; Path = $relative; Fingerprint = $fingerprint
            })
        }
    }
    finally { $archive.Dispose() }
    return @($records)
}

function Compare-MrpackSnapshots {
    param([Parameter(Mandatory)][array]$Baseline, [Parameter(Mandatory)][array]$Current)

    $old = @{}
    $new = @{}
    foreach ($record in $Baseline) { $old[$record.Key] = $record }
    foreach ($record in $Current) { $new[$record.Key] = $record }

    $added = @($new.Keys | Where-Object { -not $old.ContainsKey($_) } | ForEach-Object { $new[$_] } | Sort-Object Kind, Path)
    $removed = @($old.Keys | Where-Object { -not $new.ContainsKey($_) } | ForEach-Object { $old[$_] } | Sort-Object Kind, Path)
    $changed = @($new.Keys | Where-Object { $old.ContainsKey($_) -and $old[$_].Fingerprint -cne $new[$_].Fingerprint } | ForEach-Object {
        [pscustomobject]@{ Key = $_; Kind = $new[$_].Kind; Path = $new[$_].Path; Before = $old[$_].Fingerprint; After = $new[$_].Fingerprint }
    } | Sort-Object Kind, Path)
    return [pscustomobject]@{ Added = $added; Removed = $removed; Changed = $changed; Total = $added.Count + $removed.Count + $changed.Count }
}

function Get-MpLatestBuildFile {
    param([Parameter(Mandatory)]$Project)
    $dist = Join-Path $Project.Root 'dist'
    return Get-ChildItem -LiteralPath $dist -Filter '*.mrpack' -File -ErrorAction SilentlyContinue |
        Where-Object { -not $_.Name.StartsWith('.modpacktools-') } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
}

function Compare-MpBuildArtifactToProject {
    param([Parameter(Mandatory)]$Project, [Parameter(Mandatory)][string]$ArtifactPath)
    $dist = Join-Path $Project.Root 'dist'
    [void][IO.Directory]::CreateDirectory($dist)
    $temporary = Join-Path $dist ('.modpacktools-compare-' + [guid]::NewGuid().ToString('N') + '.mrpack')
    try {
        [void](Invoke-Packwiz -Arguments @('modrinth', 'export', '--output', $temporary) -WorkingDirectory $Project.Root)
        if (-not (Test-Path -LiteralPath $temporary -PathType Leaf)) { Throw-MpError -Message 'Packwiz did not generate the temporary comparison artifact' -Hint 'modpack build --raw-log' -ErrorId 'Diff.TemporaryArtifactMissing' -Category InvalidResult }
        return Compare-MrpackSnapshots -Baseline (Get-MrpackSnapshot $ArtifactPath) -Current (Get-MrpackSnapshot $temporary)
    }
    finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}

function Compare-ModpackBuild {
    param([Parameter(Mandatory)]$Project)

    $dist = Join-Path $Project.Root 'dist'
    $baseline = Get-MpLatestBuildFile $Project
    if (-not $baseline) { Throw-MpError -Message "No previous build exists in '$dist'" -Hint 'modpack build' -ErrorId 'Diff.BaselineNotFound' -Category ObjectNotFound -TargetObject $dist }
    $comparison = Compare-MpBuildArtifactToProject $Project $baseline.FullName
    return [pscustomobject]@{
        Project = $Project; BaselinePath = $baseline.FullName; BaselineTime = $baseline.LastWriteTime; Added = $comparison.Added
        Removed = $comparison.Removed; Changed = $comparison.Changed; Total = $comparison.Total
    }
}
