param([Parameter(Mandatory)][string]$Executable)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$directory = Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-protocol-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($directory)
try {
    $cases = @(
        @{ Text='{"schema_version":1,"arguments":["--version"]}'; Exit=0; Id=$null },
        @{ Text='{"schema_version":1,"arguments":[]}'; Exit=1; Id='Core.CapabilityUnavailable' },
        @{ Text=''; Exit=2; Id='Core.InvalidRequest' },
        @{ Text='[]'; Exit=2; Id='Core.InvalidRequest' },
        @{ Text='{"schema_version":2,"arguments":[]}'; Exit=2; Id='Core.InvalidRequest' },
        @{ Text='{"schema_version":"1","arguments":[]}'; Exit=2; Id='Core.InvalidRequest' },
        @{ Text='{"schema_version":1,"arguments":null}'; Exit=2; Id='Core.InvalidRequest' },
        @{ Text='{"schema_version":1,"arguments":[null]}'; Exit=2; Id='Core.InvalidRequest' },
        @{ Text='{"schema_version":1,"arguments":["\u0000"]}'; Exit=2; Id='Core.InvalidRequest' },
        @{ Text='{"schema_version":1,"arguments":["\uD800"]}'; Exit=2; Id='Core.InvalidRequest' },
        @{ Text='{"schema_version":1,"arguments":["\uDC00"]}'; Exit=2; Id='Core.InvalidRequest' },
        @{ Text='{"schema_version":1,"arguments":[],"\uD800":1}'; Exit=2; Id='Core.InvalidRequest' },
        @{ Text='{"schema_version":1,"schema_version":1,"arguments":[]}'; Exit=2; Id='Core.InvalidRequest' },
        @{ Text='{"schema_version":1,"arguments":[],"extra":true}'; Exit=2; Id='Core.InvalidRequest' },
        @{ Text=(' ' * (1024*1024+1)); Exit=2; Id='Core.InvalidRequest' }
    )
    foreach ($case in $cases) {
        $path = Join-Path $directory 'request.json'
        [IO.File]::WriteAllText($path, $case.Text, [Text.UTF8Encoding]::new($false))
        $capture = Invoke-NativeTestProcess $Executable @('--request-path',$path)
        if ($capture.Exit -ne $case.Exit -or $capture.Stderr -cne '' -or ($capture.Stdout.Trim() -split '\r?\n').Count -ne 1) { throw "Protocol transport mismatch: $($case.Text.Substring(0,[Math]::Min(80,$case.Text.Length)))" }
        $result = $capture.Stdout | ConvertFrom-Json
        if ($case.Id -and ($result.error.id -cne $case.Id -or $result.ok -ne $false -or $result.command -cne '' -or $result.arguments.Count -ne 0)) { throw 'Protocol envelope mismatch' }
    }
    "Native protocol: $($cases.Count) cases passed"
} finally {
    # These are the only files created by this test; avoid recursive cleanup.
    [IO.File]::Delete((Join-Path $directory 'request.json'))
    [IO.Directory]::Delete($directory)
}
