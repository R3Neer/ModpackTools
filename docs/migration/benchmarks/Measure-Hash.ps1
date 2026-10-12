param([string]$ProjectRoot='D:\Minecraft\VanillaPlus-26.2',[int]$Samples=21)
$ErrorActionPreference='Stop'
$executables=[ordered]@{
    rust=(Join-Path $PSScriptRoot 'rust-probe/target/release/diagnostic-probe.exe')
    go=(Join-Path $PSScriptRoot 'startup-go.exe')
    dotnet_aot=(Join-Path $PSScriptRoot 'dotnet-aot/Startup.exe')
}
$rows=[Collections.Generic.List[object]]::new();$reference=$null
for($i=0;$i -le $Samples;$i++) {
    foreach($language in $executables.Keys) {
        $start=[Diagnostics.ProcessStartInfo]::new($executables[$language])
        $start.UseShellExecute=$false;$start.CreateNoWindow=$true
        $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
        $start.ArgumentList.Add('--hash');$start.ArgumentList.Add($ProjectRoot)
        $sw=[Diagnostics.Stopwatch]::StartNew();$process=[Diagnostics.Process]::Start($start)
        try {
            $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
            $process.WaitForExit();$sw.Stop();if($process.ExitCode -ne 0){throw $stderr.Result}
            $payload=$stdout.Result|ConvertFrom-Json
            if(-not $payload.ok -or $payload.benchmark -ne 'tree-hash-only'){throw 'Invalid hash fixture output'}
            $identity="$($payload.files):$($payload.bytes):$($payload.fingerprint)"
            if($null -eq $reference){$reference=$identity}elseif($identity -cne $reference){throw 'Hash workloads differ or source changed'}
            $rows.Add([pscustomobject]@{language=$language;sample=$i;wall_ms=$sw.Elapsed.TotalMilliseconds;files=$payload.files;bytes=$payload.bytes;fingerprint=$payload.fingerprint})
        }finally{$process.Dispose()}
    }
}
$summary=foreach($language in $executables.Keys) {
    $selected=@($rows|Where-Object {$_.language -eq $language -and $_.sample -gt 0}|Sort-Object wall_ms)
    [pscustomobject]@{language=$language;n=$selected.Count;first_ms=($rows|Where-Object {$_.language -eq $language -and $_.sample -eq 0}).wall_ms;median_ms=$selected[[int][math]::Floor($selected.Count/2)].wall_ms;p95_ms=$selected[[int][math]::Ceiling($selected.Count*0.95)-1].wall_ms}
}
$result=[ordered]@{utc=[datetime]::UtcNow.ToString('o');project=$ProjectRoot;samples=$Samples;workload_identity=$reference;summary=@($summary);raw=@($rows);note='Process launch plus recursive SHA256 of same files and aggregate fingerprint. .git excluded, dist included. Not a full health/inventory/transaction comparison. Warm OS cache; no network. Runtime-default streaming implementations; not a controlled library/algorithm-only shootout.'}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'hash-results.json'),($result|ConvertTo-Json -Depth 8))
$summary|ConvertTo-Json
