param([Parameter(Mandatory)][string]$Executable, [string]$LegacyRoot = (Join-Path $PSScriptRoot '../..'))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$directory = Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-version-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($directory)
$previous = $env:MODPACKTOOLS_CONFIG_HOME
try {
    $env:MODPACKTOOLS_CONFIG_HOME = $directory
    $cases = @(
        ,@('--version'); ,@('-V'); ,@('--VERSION'); ,@('--version','extra'); ,@('--version','--help');
        ,@('--version','--'); ,@('--version','--json','--json'); ,@('--version','--json=true');
        ,@('--version','--project','absent'); ,@('--version','--color','invalid');
        ,@('--version','--color','NEVER','--ascii'); ,@('--json','--version');
        ,@('--version','--project='); ,@('--version','--color'); ,@('--version','--wat=hello');
        ,@('--version',"--color=never`n"); ,@('--version',"--color=never`nalways"); ,@('--version',"--json=`n")
    )
    foreach ($tokens in $cases) {
        $public = @($tokens)
        if ($public -notcontains '--json') { $public += '--json' }
        $public += '--no-human'
        $request = Join-Path $directory 'request.json'
        [IO.File]::WriteAllText($request, (@{arguments=$public} | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
        $baseline = Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoLogo','-NoProfile','-File',(Join-Path $LegacyRoot 'Nushell/Invoke-ModpackBridge.ps1'),'-RequestPath',$request)
        $actual = Invoke-NativeTestProcess $Executable $public
        if ($actual.Stderr -cne '' -or ($actual.Stdout.Trim() -split '\r?\n').Count -ne 1) { throw 'Native transport mismatch' }
        $expectedObject = $baseline.Stdout | ConvertFrom-Json
        $actualObject = $actual.Stdout | ConvertFrom-Json
        # Authorized error improvement: public arguments is consistently an array.
        if (-not $expectedObject.ok) { $expectedObject.arguments = @($expectedObject.arguments | Where-Object { $null -ne $_ }) }
        $document = [Text.Json.JsonDocument]::Parse($actual.Stdout)
        try { if ($document.RootElement.GetProperty('arguments').ValueKind -ne [Text.Json.JsonValueKind]::Array) { throw 'Arguments must always be a JSON array' } }
        finally { $document.Dispose() }
        # Canonical field order is the same; escapes are decoded before comparison.
        $expectedJson = $expectedObject | ConvertTo-Json -Depth 30 -Compress
        $actualJson = $actualObject | ConvertTo-Json -Depth 30 -Compress
        if ($expectedJson -cne $actualJson -or $actual.Exit -ne $(if ($expectedObject.ok) {0} else {1})) { throw "Differential version mismatch: $($public -join ' ')`n$expectedJson`n$actualJson" }
    }
    "Version option parity: $($cases.Count) cases passed"
} finally {
    $env:MODPACKTOOLS_CONFIG_HOME = $previous
    [IO.File]::Delete((Join-Path $directory 'request.json'))
    [IO.Directory]::Delete($directory)
}
