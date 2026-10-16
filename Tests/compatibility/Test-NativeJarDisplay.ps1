param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$LegacyRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-jardisplay-'+[guid]::NewGuid().ToString('N'))
try {
    [void][IO.Directory]::CreateDirectory($fixture)
    $runner=Join-Path $fixture 'legacy.ps1'
    [IO.File]::WriteAllText($runner,@'
param($ModulePath,$Path)
Import-Module $ModulePath -Force
& (Get-Module ModpackTools) {param($path) Get-LocalJarMetadata -Path $path|ConvertTo-Json -Compress} $Path
'@)
    function Write-TestJar([string]$Path,[AllowNull()][object]$Text) {
        $archive=[IO.Compression.ZipFile]::Open($Path,[IO.Compression.ZipArchiveMode]::Create)
        try {
            if($null -ne $Text) {
                $stream=$archive.CreateEntry('fabric.mod.json').Open()
                $writer=[IO.StreamWriter]::new($stream,[Text.UTF8Encoding]::new($false))
                try {$writer.Write($Text)} finally {$writer.Dispose();$stream.Dispose()}
            }
        } finally {$archive.Dispose()}
    }
    $cases=@(
        @{id='missing-name';text='{"environment":"client"}';status='unreadable'},
        @{id='missing-environment';text='{"name":"Partial"}';status='unreadable'},
        @{id='array';text='{"name":["One","Two"],"environment":null}';status='read'},
        @{id='mixed';text='{"name":[1.5,true,null,{"id":1}],"environment":"client"}';status='read'},
        @{id='date';text='{"name":"2024-10-10T12:34:56Z","environment":"server"}';status='read'},
        @{id='case-conflict';text='{"name":"One","NAME":"Two","environment":"client"}';status='unreadable'},
        @{id='comment';text='{/* comment */"name":"Tolerated","environment":"*",}';status='read'},
        @{id='root-array';text='[{"name":["A","B"],"environment":"client"},{"name":"C","environment":null}]';status='read'},
        @{id='isolated-surrogate';text='{"name":"\ud800","environment":"client"}';status='read'},
        @{id='surrogate-key-duplicate';text='{"name":{"\ud800":1,"\udc00":2},"environment":"client"}';status='read'},
        @{id='surrogate-key-case';text='{"name":{"X\ud800":1,"x\udc00":2},"environment":"client"}';status='unreadable'}
    )
    foreach($case in $cases) {
        $path=Join-Path $fixture ($case.id+'.jar'); Write-TestJar $path $case.text
        $legacy=Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoProfile','-File',$runner,'-ModulePath',(Join-Path $LegacyRoot 'ModpackTools.psd1'),'-Path',$path)
        $native=Invoke-NativeTestProcess $Executable @('--inspect-jar-display',$path)
        $old=$legacy.Stdout|ConvertFrom-Json; $new=$native.Stdout|ConvertFrom-Json
        if($legacy.Exit -ne 0 -or $native.Exit -ne 0 -or $native.Stderr -cne '' -or $old.Name -cne $new.name -or $old.Side -cne $new.side -or $new.status -cne $case.status){throw "JAR display mismatch $($case.id): $($legacy.Stdout) $($native.Stdout) $($native.Stderr)"}
        if(($new.status -eq 'read' -and $null -ne $new.diagnostic) -or ($new.status -eq 'unreadable' -and -not $new.diagnostic)){throw 'JAR status diagnostic mismatch'}
    }
    $boundary=Join-Path $fixture 'boundary.jar'; $text='{"name":"Boundary","environment":"client"}'; Write-TestJar $boundary $text
    $entryBytes=[Text.Encoding]::UTF8.GetByteCount($text)
    foreach($pair in @(@('--max-entry-bytes',$entryBytes),@('--max-central-bytes',61),@('--max-entries',1))) {
        $exact=Invoke-NativeTestProcess $Executable @('--inspect-jar-display',$boundary,$pair[0],[string]$pair[1])
        if($exact.Exit -ne 0 -or $exact.Stderr -cne '' -or ($exact.Stdout|ConvertFrom-Json).status -cne 'read'){throw "Exact JAR budget failed: $($pair[0])"}
    }
    foreach($pair in @(@('--max-entry-bytes',($entryBytes-1)),@('--max-central-bytes',60))) {
        $over=Invoke-NativeTestProcess $Executable @('--inspect-jar-display',$boundary,$pair[0],[string]$pair[1])
        $result=$over.Stdout|ConvertFrom-Json
        if($over.Exit -ne 0 -or $over.Stderr -cne '' -or $result.status -cne 'limited' -or $result.name -cne 'boundary' -or $result.side -cne 'unknown' -or -not $result.diagnostic){throw "Exceeded JAR budget failed: $($pair[0])"}
    }
    $empty=Join-Path $fixture 'empty.jar'; Write-TestJar $empty $null
    $missing=Invoke-NativeTestProcess $Executable @('--inspect-jar-display',$empty)
    if($missing.Exit -ne 0 -or $missing.Stderr -cne '' -or ($missing.Stdout|ConvertFrom-Json).status -cne 'missing'){throw 'Missing JAR metadata was not distinguished'}
    $corrupt=Join-Path $fixture 'corrupt.jar'; [IO.File]::WriteAllText($corrupt,'not an archive')
    $unreadable=Invoke-NativeTestProcess $Executable @('--inspect-jar-display',$corrupt)
    $result=$unreadable.Stdout|ConvertFrom-Json
    if($unreadable.Exit -ne 0 -or $unreadable.Stderr -cne '' -or $result.status -cne 'unreadable' -or $result.name -cne 'corrupt' -or $result.side -cne 'unknown' -or -not $result.diagnostic){throw 'Corrupt JAR escaped typed display failure'}
    $file=[IO.File]::Open($boundary,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$file.Dispose()
    $integer=Join-Path $fixture 'large-integer.jar'; $digits='9'*1048576
    Write-TestJar $integer ('{"name":'+$digits+',"environment":"client"}')
    $capture=Invoke-NativeTestProcess $Executable @('--inspect-jar-display',$integer)
    $result=$capture.Stdout|ConvertFrom-Json
    if($capture.Exit -ne 0 -or $capture.Stderr -cne '' -or $result.status -cne 'read' -or $result.name -cne $digits -or $result.side -cne 'client'){throw 'Large canonical JSON integer was altered or failed'}
    'Native JAR display: 11 differential cases, 5 exact/over budgets, missing/corrupt metadata, exclusive handle reopening and large-integer regression passed'
} finally {
    $resolved=[IO.Path]::GetFullPath($fixture)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-jardisplay-*'){throw 'Unsafe fixture cleanup target'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
