function Get-MpHash {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Resolve-MpContainedPath {
    param([string]$Root, [string]$Relative)
    $base = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $path = [IO.Path]::GetFullPath((Join-Path $base $Relative))
    if (-not $path.StartsWith($base + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        Throw-MpError -Message "Path '$Relative' escapes the project" -Hint 'repair the referenced path' -ErrorId 'Transaction.UnsafePath' -Category InvalidData
    }
    return $path
}

function Get-MpTreeState {
    param([string]$Root)
    if (Test-MpFileSystemLink (Get-Item -LiteralPath $Root -Force)) {
        Throw-MpError -Message "Linked path '$Root' cannot be transacted safely" -Hint 'use a project with regular files and directories' -ErrorId 'Transaction.LinkedPath' -Category InvalidOperation
    }
    $state = @{}
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($Root)
    while ($pending.Count) {
        foreach ($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            if ($entry.Name -eq '.git') { continue }
            if (Test-MpFileSystemLink $entry) {
                Throw-MpError -Message "Linked path '$($entry.FullName)' cannot be transacted safely" -Hint 'use a project with regular files and directories' -ErrorId 'Transaction.LinkedPath' -Category InvalidOperation
            }
            if ($entry.PSIsContainer) { $pending.Push($entry.FullName); continue }
            $relative = [IO.Path]::GetRelativePath($Root, $entry.FullName).Replace('\', '/')
            $state[$relative] = (Get-FileHash -LiteralPath $entry.FullName -Algorithm SHA256).Hash
        }
    }
    return $state
}

function Get-MpTreeChanges {
    param([hashtable]$Before, [hashtable]$After)
    foreach ($path in @(@($Before.Keys) + @($After.Keys) | Sort-Object -Unique)) {
        if ($Before[$path] -cne $After[$path]) {
            [pscustomobject]@{ Path = $path; Before = $Before[$path]; After = $After[$path] }
        }
    }
}

function Copy-MpFileAtomic {
    param([string]$Source, [string]$Destination)
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $Destination))
    $temporary = $Destination + '.modpacktools-' + [guid]::NewGuid().ToString('N')
    try {
        [IO.File]::Copy($Source, $temporary)
        [IO.File]::Move($temporary, $Destination, $true)
    }
    finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
}

function Restore-MpJournal {
    param([string]$Directory, [string]$Root)
    $path = Join-Path $Directory 'journal.json'
    if (-not [IO.File]::Exists($path)) { return }
    $journal = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($journal.Root -cne $Root) {
        Throw-MpError -Message 'Transaction journal belongs to another project' -Hint 'inspect the transaction journal' -ErrorId 'Transaction.InvalidJournal' -Category InvalidData
    }
    if ($journal.Status -eq 'committed') { return }
    # Validate every destination before restoring any file. Preserve external edits.
    foreach ($change in $journal.Changes) {
        $target = Resolve-MpContainedPath -Root $Root -Relative $change.Path
        $current = if ([IO.File]::Exists($target)) { (Get-FileHash -LiteralPath $target).Hash } else { $null }
        if ($current -cne $change.Before -and $current -cne $change.After) {
            Throw-MpError -Message "Recovery would overwrite an external edit to '$($change.Path)'" -Details $path -Hint 'preserve the external edit and reconcile the pending transaction' -ErrorId 'Transaction.RecoveryConflict' -Category InvalidOperation
        }
    }
    foreach ($change in $journal.Changes) {
        if ($change.Before) {
            $backup = Resolve-MpContainedPath -Root (Join-Path $Directory 'backup') -Relative $change.Path
            if (-not [IO.File]::Exists($backup) -or (Get-FileHash -LiteralPath $backup).Hash -cne $change.Before) {
                Throw-MpError -Message 'Transaction backup is damaged' -Details $backup -Hint 'restore the affected file from a trusted backup' -ErrorId 'Transaction.InvalidBackup' -Category InvalidData
            }
        }
    }
    foreach ($change in $journal.Changes) {
        $target = Resolve-MpContainedPath -Root $Root -Relative $change.Path
        if ($change.Before) {
            $backup = Resolve-MpContainedPath -Root (Join-Path $Directory 'backup') -Relative $change.Path
            if ((Get-FileHash -LiteralPath $backup).Hash -cne $change.Before) {
                Throw-MpError -Message 'Transaction backup is damaged' -Details $backup -Hint 'restore the affected file from a trusted backup' -ErrorId 'Transaction.InvalidBackup' -Category InvalidData
            }
            Copy-MpFileAtomic -Source $backup -Destination $target
        }
        elseif ([IO.File]::Exists($target)) { [IO.File]::Delete($target) }
    }
    foreach ($relative in @((Get-MpPropertyValue $journal 'CreatedDirectories') | Sort-Object { $_.Length } -Descending)) {
        if (-not $relative) { continue }
        $created = Resolve-MpContainedPath $Root $relative
        if ([IO.Directory]::Exists($created) -and [IO.Directory]::GetFileSystemEntries($created).Length -eq 0) { [IO.Directory]::Delete($created) }
    }
    [IO.File]::Delete($path)
}

function Invoke-MpProjectTransaction {
    param([Parameter(Mandatory)]$Project, [Parameter(Mandatory)][scriptblock]$Prepare,
        [switch]$DryRun, [scriptblock]$Verify, [AllowNull()][array]$ExpectedChanges = $null)
    $ErrorActionPreference = 'Stop'
    $root = [IO.Path]::GetFullPath($Project.Root)
    $homePath = Join-Path (Get-ModpackToolsConfigDirectory) ('transactions/' + (Get-MpHash $root.ToLowerInvariant()))
    [void][IO.Directory]::CreateDirectory($homePath)
    $lock = $null
    try { $lock = [IO.File]::Open((Join-Path $homePath 'lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { Throw-MpError -Message 'Another operation owns this project transaction' -Hint 'wait for it to finish and retry' -ErrorId 'Transaction.Busy' -Category ResourceBusy }
    $directory = Join-Path $homePath ([guid]::NewGuid().ToString('N'))
    try {
        [void](Get-MpTreeState $root)
        foreach ($pending in Get-ChildItem -LiteralPath $homePath -Directory) {
            Restore-MpJournal -Directory $pending.FullName -Root $root
            # The directory was enumerated immediately below our verified transaction root.
            Remove-Item -LiteralPath $pending.FullName -Recurse -Force
        }
        $before = Get-MpTreeState -Root $root
        $stage = Join-Path $directory 'stage'
        [void][IO.Directory]::CreateDirectory($stage)
        foreach ($relative in $before.Keys) {
            $destination = Resolve-MpContainedPath -Root $stage -Relative $relative
            [void][IO.Directory]::CreateDirectory((Split-Path -Parent $destination))
            [IO.File]::Copy((Resolve-MpContainedPath -Root $root -Relative $relative), $destination)
        }
        if (@(Get-MpTreeChanges $before (Get-MpTreeState $stage)).Count) {
            Throw-MpError -Message 'The project changed while it was copied' -Hint 'retry after other writers finish' -ErrorId 'Transaction.ConcurrentChange' -Category InvalidOperation
        }
        $stagedProject = $Project.PSObject.Copy()
        $stagedProject.Root = $stage
        $stagedProject.IndexPath = Join-Path $stage $Project.IndexFile
        $stagedProject.DescriptorPath = Join-Path $stage '.modpack/project.psd1'
        $result = & $Prepare $stagedProject
        if ($Verify) { & $Verify $stagedProject | Out-Null }
        $after = Get-MpTreeState -Root $stage
        $changes = @(Get-MpTreeChanges $before $after)
        if (@(Get-MpTreeChanges $before (Get-MpTreeState $root)).Count) {
            Throw-MpError -Message 'The project changed during preparation; nothing was applied' -Hint 'retry after other writers finish' -ErrorId 'Transaction.ConcurrentChange' -Category InvalidOperation
        }
        if ($null -ne $ExpectedChanges -and ($changes | ConvertTo-Json -Depth 8 -Compress) -cne ($ExpectedChanges | ConvertTo-Json -Depth 8 -Compress)) {
            Throw-MpError -Message 'The repair plan changed since it was reviewed' -Hint 'run doctor --fix again to review the new plan' -ErrorId 'Transaction.PlanChanged' -Category InvalidOperation
        }
        if ($DryRun -or -not $changes.Count) { return [pscustomobject]@{ Changes = $changes; Result = $result; Applied = $false } }
        foreach ($change in $changes) {
            if ($change.Before) {
                $backup = Resolve-MpContainedPath -Root (Join-Path $directory 'backup') -Relative $change.Path
                [void][IO.Directory]::CreateDirectory((Split-Path -Parent $backup))
                [IO.File]::Copy((Resolve-MpContainedPath -Root $root -Relative $change.Path), $backup)
            }
        }
        $journalPath = Join-Path $directory 'journal.json'
        $createdDirectories = @{}
        foreach ($change in $changes) {
            $parent = Split-Path -Parent (Resolve-MpContainedPath $root $change.Path)
            while (-not [IO.Directory]::Exists($parent)) {
                $createdDirectories[[IO.Path]::GetRelativePath($root, $parent)] = $true
                $parent = Split-Path -Parent $parent
            }
        }
        $journal = @{ Root = $root; Status = 'pending'; Changes = $changes; CreatedDirectories = @($createdDirectories.Keys) }
        Write-Utf8TextFileAtomic -Path $journalPath -Text ($journal | ConvertTo-Json -Depth 8)
        try {
            foreach ($change in $changes) {
                $target = Resolve-MpContainedPath -Root $root -Relative $change.Path
                $current = if ([IO.File]::Exists($target)) { (Get-FileHash -LiteralPath $target).Hash } else { $null }
                if ($current -cne $change.Before) { Throw-MpError -Message 'A destination changed during commit' -Hint 'inspect the pending journal before retrying' -ErrorId 'Transaction.ConcurrentChange' -Category InvalidOperation }
                if ($change.After) { Copy-MpFileAtomic -Source (Resolve-MpContainedPath -Root $stage -Relative $change.Path) -Destination $target }
                else { [IO.File]::Delete($target) }
            }
            if (@(Get-MpTreeChanges $after (Get-MpTreeState $root)).Count) {
                Throw-MpError -Message 'Written state differs from the prepared state' -Hint 'inspect the transaction recovery result' -ErrorId 'Transaction.VerificationFailed' -Category InvalidResult
            }
            $journal.Status = 'committed'
            Write-Utf8TextFileAtomic -Path $journalPath -Text ($journal | ConvertTo-Json -Depth 8)
        }
        catch { Restore-MpJournal -Directory $directory -Root $root; throw }
        [IO.File]::Delete($journalPath)
        return [pscustomobject]@{ Changes = $changes; Result = $result; Applied = $true }
    }
    finally {
        # Keep failed recovery journals and backups. Never delete a computed path outside homePath.
        if ([IO.Directory]::Exists($directory) -and -not [IO.File]::Exists((Join-Path $directory 'journal.json'))) {
            $safe = Resolve-MpContainedPath -Root $homePath -Relative ([IO.Path]::GetFileName($directory))
            Remove-Item -LiteralPath $safe -Recurse -Force
        }
        if ($lock) { $lock.Dispose() }
    }
}

function Write-MpTransactionSummary {
    param($Transaction, [switch]$DryRun)
    foreach ($change in $Transaction.Changes) {
        $action = if (-not $change.Before) { 'Add' } elseif (-not $change.After) { 'Remove' } else { 'Change' }
        Write-R3Status (Get-MpConsole) info "$action $($change.Path)"
    }
    if ($DryRun) { Write-R3Status (Get-MpConsole) info 'Dry run: no project changes applied.' }
    else { Write-R3Status (Get-MpConsole) success "$($Transaction.Changes.Count) file change(s) applied." }
}
