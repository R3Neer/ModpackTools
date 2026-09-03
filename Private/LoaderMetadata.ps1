function ConvertFrom-MpToml {
    param([string]$Text)
    if (-not ('Tomlyn.Toml' -as [type])) {
        $path = Join-Path $script:ModuleRoot 'Private/lib/Tomlyn.dll'
        if ((Get-FileHash -LiteralPath $path).Hash -ne 'A7D2EA40533A5A912BC6A64AB8F1347E60FE92EA0AB456D423B64DB2214C904B') {
            Throw-MpError -Message 'The bundled TOML parser failed integrity verification' -Hint 'reinstall ModpackTools' -ErrorId 'Metadata.ParserIntegrity' -Category InvalidData
        }
        Add-Type -Path $path
    }
    return ,([Tomlyn.Toml]::ToModel($Text, $null, $null))
}

function Compare-MpVersion {
    param([string]$Left, [string]$Right, [switch]$Maven)
    if ($Left -ceq $Right) { return 0 }
    if (-not $Maven) {
        $pattern = '^(\d+(?:\.\d+)*)(?:-([0-9A-Za-z.-]*))?(?:\+.*)?$'
        $a = [regex]::Match($Left, $pattern); $b = [regex]::Match($Right, $pattern)
        if (-not $a.Success -or -not $b.Success) { return $null }
        $ap = $a.Groups[1].Value.Split('.'); $bp = $b.Groups[1].Value.Split('.')
        for ($i = 0; $i -lt [Math]::Max($ap.Count, $bp.Count); $i++) {
            $av = if ($i -lt $ap.Count) { [Numerics.BigInteger]::Parse($ap[$i]) } else { [Numerics.BigInteger]::Zero }
            $bv = if ($i -lt $bp.Count) { [Numerics.BigInteger]::Parse($bp[$i]) } else { [Numerics.BigInteger]::Zero }
            $c = $av.CompareTo($bv); if ($c) { return $c }
        }
        if (-not $a.Groups[2].Success -and -not $b.Groups[2].Success) { return 0 }
        if (-not $a.Groups[2].Success) { return 1 }; if (-not $b.Groups[2].Success) { return -1 }
        $ap = $a.Groups[2].Value.Split('.'); $bp = $b.Groups[2].Value.Split('.')
        for ($i = 0; $i -lt [Math]::Min($ap.Count, $bp.Count); $i++) {
            $an = $ap[$i] -match '^\d+$'; $bn = $bp[$i] -match '^\d+$'
            if ($an -and $bn) { $c = ([Numerics.BigInteger]::Parse($ap[$i])).CompareTo([Numerics.BigInteger]::Parse($bp[$i])) }
            elseif ($an -ne $bn) { $c = if ($an) { -1 } else { 1 } }
            else { $c = [string]::CompareOrdinal($ap[$i], $bp[$i]) }
            if ($c) { return $c }
        }
        return $ap.Count.CompareTo($bp.Count)
    }
    # Maven comparison uses a dedicated implementation rather than System.Version.
    if (-not ('ModpackTools.MavenVersion' -as [type])) { Add-Type -Path (Join-Path $script:ModuleRoot 'Private/MavenVersion.cs') }
    return [ModpackTools.MavenVersion]::Compare($Left, $Right)
}

function Test-MpVersionRange {
    param([string]$Version, $Range, [switch]$Maven)
    if ($null -ne $Range -and $Range -isnot [string] -and $Range -isnot [array]) {
        $any = Get-MpPropertyValue $Range 'any'; $all = Get-MpPropertyValue $Range 'all'
        if ($any) { return Test-MpVersionRange $Version @($any) -Maven:$Maven }
        if ($all) {
            $unknown = $false
            foreach ($part in $all) { $r = Test-MpVersionRange $Version $part -Maven:$Maven; if ($r -eq $false) { return $false }; if ($null -eq $r) { $unknown = $true } }
            if ($unknown) { return $null }; return $true
        }
        return $null
    }
    if ($Range -is [array]) {
        $unknown = $false
        foreach ($part in $Range) { $r = Test-MpVersionRange $Version $part -Maven:$Maven; if ($r -eq $true) { return $true }; if ($null -eq $r) { $unknown = $true } }
        if ($unknown) { return $null }; return $false
    }
    $value = ([string]$Range).Trim()
    if (-not $value -or $value -eq '*') { return $true }
    if ($Maven) {
        if ($value -notmatch '^[\[(]') { return $true } # Maven bare versions are recommendations.
        $intervals = [regex]::Matches($value, '([\[(])([^\[\]()]*)([\])])')
        if (-not $intervals.Count -or (($intervals.Value -join ',') -replace '\s','') -ne ($value -replace '\s','')) { return $null }
        foreach ($interval in $intervals) {
            $bounds = $interval.Groups[2].Value.Split(',')
            if ($bounds.Count -eq 1) { if ($interval.Groups[1].Value -ne '[' -or $interval.Groups[3].Value -ne ']') { return $null }; if ((Compare-MpVersion $Version $bounds[0] -Maven) -eq 0) { return $true }; continue }
            if ($bounds.Count -ne 2) { return $null }
            $low = if ($bounds[0].Trim()) { Compare-MpVersion $Version $bounds[0].Trim() -Maven } else { 1 }
            $high = if ($bounds[1].Trim()) { Compare-MpVersion $Version $bounds[1].Trim() -Maven } else { -1 }
            if (($low -gt 0 -or ($low -eq 0 -and $interval.Groups[1].Value -eq '[')) -and ($high -lt 0 -or ($high -eq 0 -and $interval.Groups[3].Value -eq ']'))) { return $true }
        }
        return $false
    }
    if ($value.Contains('||')) { return Test-MpVersionRange $Version @($value -split '\s*\|\|\s*') }
    foreach ($part in @($value -split '\s+' | Where-Object { $_ })) {
        $m = [regex]::Match($part, '^(>=|<=|>|<|=|~|\^)?(.+)$')
        if (-not $m.Success) { return $null }
        $op = $m.Groups[1].Value; $bound = $m.Groups[2].Value
        if ($bound -match '^(\d+(?:\.\d+)*)\.(?:x|X|\*)(?:\.(?:x|X|\*))*$') {
            if ($op -and $op -ne '=') { return $null }
            $base = $Matches[1]; $parts = $base.Split('.'); $upper = @($parts); $upper[-1] = ([Numerics.BigInteger]::Parse($upper[-1]) + 1).ToString()
            $r = Test-MpVersionRange $Version ">=$base <$(($upper -join '.'))"
        }
        elseif ($op -in @('~','^')) {
            if ($bound -notmatch '^\d+(?:\.\d+)*(?:-[0-9A-Za-z.-]+)?$') { return $null }
            $parts = @(($bound -split '-')[0].Split('.') | ForEach-Object { [int]$_ })
            $index = if ($op -eq '~') { [Math]::Min(1, $parts.Count - 1) } else { 0 }
            if ($op -eq '^') { while ($index -lt $parts.Count - 1 -and $parts[$index] -eq 0) { $index++ } }
            $parts[$index]++; for ($i = $index + 1; $i -lt $parts.Count; $i++) { $parts[$i] = 0 }
            $r = Test-MpVersionRange $Version ">=$bound <$(($parts -join '.'))"
        }
        else {
            $c = Compare-MpVersion $Version $bound
            if ($null -eq $c) { if (-not $op -or $op -eq '=') { $r = $Version -ceq $bound } else { return $null } }
            else { $r = switch ($op) { '>' { $c -gt 0 }; '>=' { $c -ge 0 }; '<' { $c -lt 0 }; '<=' { $c -le 0 }; default { $c -eq 0 } } }
        }
        if ($null -eq $r) { return $null }; if (-not $r) { return $false }
    }
    return $true
}

function New-MpRequirement {
    param([string]$Target, $Range = '*', [string]$Kind = 'required', [string]$Side = 'both', [string]$Scope = 'mod', [bool]$Maven = $false)
    [pscustomobject]@{ Target = $Target; Range = $Range; Kind = $Kind; Side = $Side; Scope = $Scope; Maven = $Maven; Any = @(); All = @(); Unless = $null }
}

function ConvertFrom-MpQuiltRequirement {
    param($Value, [string]$Kind = 'required')
    if ($Value -is [string]) { return New-MpRequirement -Target $Value -Kind $Kind }
    $r = New-MpRequirement -Target ([string](Get-MpPropertyValue $Value 'id')) -Range (Get-MpPropertyValue $Value 'versions') -Kind $Kind
    if ((Get-MpPropertyValue $Value 'optional') -eq $true) { $r.Kind = 'optional' }
    foreach ($op in @('any','all')) {
        $children = Get-MpPropertyValue $Value $op
        if ($children) { $r.$op = @($children | ForEach-Object { ConvertFrom-MpQuiltRequirement $_ 'required' }) }
    }
    if ($Value -is [array]) { $r.Any = @($Value | ForEach-Object { ConvertFrom-MpQuiltRequirement $_ 'required' }) }
    $unless = Get-MpPropertyValue $Value 'unless'
    if ($unless) { $r.Unless = ConvertFrom-MpQuiltRequirement $unless }
    return $r
}

function Read-MpZipText {
    param($Archive, [string]$Name)
    $entry = $Archive.GetEntry($Name)
    if (-not $entry) { return $null }
    if ($entry.Length -gt 4MB) { Throw-MpError -Message "Metadata '$Name' exceeds the read limit" -Hint 'inspect the archive manually' -ErrorId 'Metadata.ArchiveLimit' -Category InvalidData }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}

function Read-MpLoaderArchive {
    param($Archive, [string]$Loader, [int]$Depth = 0)
    $mods = [Collections.Generic.List[object]]::new(); $warnings = [Collections.Generic.List[string]]::new()
    $nested = @()
    if ($Depth -gt 16) { return [pscustomobject]@{ Mods = @(); Warnings = @('Nested JAR depth exceeds 16') } }
    try {
        $fabric = Read-MpZipText $Archive 'fabric.mod.json'
        $quilt = if ($Loader -eq 'quilt') { Read-MpZipText $Archive 'quilt.mod.json' } else { $null }
        if ($quilt) {
            $json = $quilt | ConvertFrom-Json -AsHashtable; $q = $json.quilt_loader
            $requirements = @()
            foreach ($kind in @('depends','breaks')) { foreach ($dep in @($q[$kind])) { if ($null -ne $dep) { $requirements += ConvertFrom-MpQuiltRequirement $dep $(if ($kind -eq 'breaks') { 'incompatible' } else { 'required' }) } } }
            $provides = @{}
            foreach ($alias in @($q['provides'])) {
                if ($alias -is [string]) { $provides[$alias] = [string]$q.version }
                elseif ($alias) { $provides[[string]$alias.id] = $(if ($alias['version']) { [string]$alias.version } else { [string]$q.version }) }
            }
            $side = if ($json.ContainsKey('minecraft') -and $json.minecraft.ContainsKey('environment')) { [string]$json.minecraft.environment } else { 'both' }
            $mods.Add([pscustomobject]@{ Id = [string]$q.id; Version = [string]$q.version; Side = $side; Provides = $provides; Requirements = $requirements })
            $nested = @($q['jars'])
            if ($json.schema_version -ne 1) { $warnings.Add('Unknown Quilt schema version') }
        }
        elseif ($fabric -and $Loader -in @('fabric','quilt')) {
            $json = $fabric | ConvertFrom-Json -AsHashtable
            if ($json.schemaVersion -ne 1) { $warnings.Add('Unknown Fabric schema version') }
            $requirements = @()
            foreach ($key in @('depends','breaks','conflicts','recommends','suggests')) {
                $kind = switch ($key) { 'depends' { 'required' }; 'breaks' { 'incompatible' }; 'conflicts' { 'discouraged' }; default { 'recommended' } }
                if ($json[$key] -is [System.Collections.IDictionary]) {
                    foreach ($id in $json[$key].Keys) { $requirements += New-MpRequirement -Target $id -Range $json[$key][$id] -Kind $kind }
                }
            }
            $provides = @{}; foreach ($id in @($json['provides'])) { if ($id) { $provides[$id] = [string]$json.version } }
            $mods.Add([pscustomobject]@{ Id = [string]$json.id; Version = [string]$json.version; Side = [string]$json['environment']; Provides = $provides; Requirements = $requirements })
            $nested = @($json['jars'] | ForEach-Object { if ($_) { $_.file } })
        }
        elseif ($Loader -in @('forge','neoforge')) {
            $text = if ($Loader -eq 'neoforge') { Read-MpZipText $Archive 'META-INF/neoforge.mods.toml' } else { $null }
            if (-not $text) { $text = Read-MpZipText $Archive 'META-INF/mods.toml' }
            if ($text) {
                $data = ConvertFrom-MpToml $text
                $manifest = [string](Read-MpZipText $Archive 'META-INF/MANIFEST.MF')
                $jarVersion = [regex]::Match($manifest, '(?m)^Implementation-Version:\s*([^\r\n]+)').Groups[1].Value
                foreach ($mod in $data['mods']) {
                    $requirements = @()
                    $deps = $data['dependencies']
                    if ($deps -and $deps.ContainsKey([string]$mod['modId'])) {
                        foreach ($dep in $deps[[string]$mod['modId']]) {
                            $kind = if ($dep.ContainsKey('type')) { [string]$dep['type'] } elseif ($dep['mandatory'] -eq $false) { 'optional' } else { 'required' }
                            $requirements += New-MpRequirement -Target ([string]$dep['modId']) -Range $dep['versionRange'] -Kind $kind -Side ([string]$dep['side']).ToLowerInvariant() -Maven $true
                            if ($dep.ContainsKey('ordering') -and $dep['ordering'] -ne 'NONE') { $warnings.Add("$($mod['modId']): load ordering is not verified") }
                        }
                    }
                    if ($data['loaderVersion']) {
                        $requirements += New-MpRequirement -Target ([string]$data['modLoader']) -Range $data['loaderVersion'] -Maven $true
                    }
                    $features = if ($mod.ContainsKey('features')) { $mod['features'] } elseif ($data.ContainsKey('features') -and $data['features'].ContainsKey([string]$mod['modId'])) { $data['features'][[string]$mod['modId']] } else { $null }
                    if ($features) {
                        foreach ($feature in $features.Keys) {
                            if ($feature -in @('javaVersion','java_version')) { $requirements += New-MpRequirement -Target java -Range $features[$feature] -Maven $true }
                            else { $warnings.Add("$($mod['modId']): runtime feature '$feature' is not verified") }
                        }
                    }
                    $version = ([string]$mod['version']).Replace('${file.jarVersion}', $jarVersion)
                    if ($version.Contains('${') -or -not $version) { $warnings.Add("$($mod['modId']): version substitution could not be evaluated") }
                    $mods.Add([pscustomobject]@{ Id = [string]$mod['modId']; Version = $version; Side = 'both'; Provides = @{}; Requirements = $requirements })
                }
            }
            $jarjar = Read-MpZipText $Archive 'META-INF/jarjar/metadata.json'
            if ($jarjar) {
                $jj = $jarjar | ConvertFrom-Json -AsHashtable
                $nested = @($jj.jars | ForEach-Object { $_.path })
                $warnings.Add('Jar-in-Jar library selection ranges require runtime verification')
            }
        }
        if (-not $mods.Count -and $Depth -eq 0) { $warnings.Add("No supported $Loader mod manifest found") }
        foreach ($name in $nested) {
            if (-not $name) { continue }
            $entry = $Archive.GetEntry([string]$name)
            if (-not $entry -or $entry.Length -gt 128MB) { $warnings.Add("Nested JAR '$name' is unavailable or too large"); continue }
            $stream = $entry.Open(); $memory = [IO.MemoryStream]::new(); $child = $null
            try {
                $stream.CopyTo($memory); $memory.Position = 0
                $child = [IO.Compression.ZipArchive]::new($memory, [IO.Compression.ZipArchiveMode]::Read, $true)
                $parsed = Read-MpLoaderArchive $child $Loader ($Depth + 1)
                foreach ($mod in $parsed.Mods) { $mods.Add($mod) }; foreach ($warning in $parsed.Warnings) { $warnings.Add($warning) }
            }
            finally { if ($child) { $child.Dispose() }; $stream.Dispose(); $memory.Dispose() }
        }
    }
    catch { $warnings.Add("Manifest could not be fully read: $($_.Exception.Message)") }
    foreach ($mod in $mods) { if (-not $mod.PSObject.Properties['Nested']) { $mod | Add-Member -NotePropertyName Nested -NotePropertyValue ($Depth -gt 0) } }
    return [pscustomobject]@{ Mods = @($mods); Warnings = @($warnings) }
}

function Get-MpLoaderMetadata {
    param([string]$Path, [string]$Loader)
    $archive = $null
    try { $archive = [IO.Compression.ZipFile]::OpenRead($Path); return Read-MpLoaderArchive $archive $Loader }
    catch { return [pscustomobject]@{ Mods = @(); Warnings = @("JAR could not be read: $($_.Exception.Message)") } }
    finally { if ($archive) { $archive.Dispose() } }
}
