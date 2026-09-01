function Get-ModpackDefaultOptionsStatus {
    param([Parameter(Mandatory)]$Project, $Inventory)

    if (-not $Inventory) { $Inventory = Get-ModpackInventory -Project $Project }
    $mod = $Inventory.Mods | Where-Object {
        ([string]$_.Id).Equals('modrinth:WEg59z5b', [System.StringComparison]::OrdinalIgnoreCase) -or
        ([string]$_.Name).Equals('Default Options', [System.StringComparison]::OrdinalIgnoreCase) -or
        ([string]$_.Filename) -match '^defaultoptions(?:-|\.|_).*\.jar$'
    } | Select-Object -First 1
    $configPath = Join-Path $Project.Root 'config/defaultoptions-common.toml'
    return [pscustomobject]@{
        Installed = $null -ne $mod
        ConfigPresent = Test-Path -LiteralPath $configPath -PathType Leaf
        Ready = ($null -ne $mod) -and (Test-Path -LiteralPath $configPath -PathType Leaf)
        Mod = $mod
        ConfigPath = $configPath
    }
}

function Assert-ModpackDefaultOptionsInstalled {
    param([Parameter(Mandatory)]$Project, $Inventory)

    $status = Get-ModpackDefaultOptionsStatus -Project $Project -Inventory $Inventory
    if (-not $status.Installed) {
        Throw-MpError -Message "Default Options is not installed in project '$($Project.Id)', so resource pack activation and order cannot be managed" -Hint "modpack add WEg59z5b --project $($Project.Id)" -ErrorId 'ResourcePack.DefaultOptionsRequired' -Category NotInstalled -TargetObject $Project.Id
    }
    return $status
}

function Resolve-ModpackResourcePack {
    param([Parameter(Mandatory)]$Inventory, [Parameter(Mandatory)][string]$Selector)

    $candidates = [System.Collections.Generic.List[object]]::new()
    foreach ($item in $Inventory.ActiveResources) {
        $candidates.Add([pscustomobject]@{
            DefaultId = $item.Id; Id = $item.Id; Name = $item.Name; Filename = $item.Filename; Active = $true
        })
    }
    foreach ($item in $Inventory.InactiveResources) {
        $candidates.Add([pscustomobject]@{
            DefaultId = "file/$($item.Filename)"; Id = $item.Id; Name = $item.Name; Filename = $item.Filename; Active = $false
        })
    }
    foreach ($key in $Inventory.Metadata.ResourcePacks.Keys) {
        if ($candidates.DefaultId -contains $key) { continue }
        $entry = $Inventory.Metadata.ResourcePacks[$key]
        $name = if ($entry.ContainsKey('Name')) { [string]$entry.Name } else { [string]$key }
        $candidates.Add([pscustomobject]@{
            DefaultId = [string]$key; Id = [string]$key; Name = $name; Filename = $null; Active = $false
        })
    }

    $matches = @($candidates | Where-Object {
        foreach ($value in @($_.DefaultId, $_.Id, $_.Name, $_.Filename)) {
            if ($value -and $value.Equals($Selector, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
        }
        return $false
    })
    if ($matches.Count -eq 0) {
        Throw-MpError -Message "Resource pack '$Selector' was not found" -Hint 'modpack inventory --type resourcepack' -ErrorId 'ResourcePack.NotFound' -Category ObjectNotFound -TargetObject $Selector
    }
    if ($matches.Count -gt 1) {
        $names = @($matches | ForEach-Object { "'$($_.Name)' [$($_.DefaultId)]" }) -join ', '
        Throw-MpError -Message "Resource pack selector '$Selector' matches more than one pack" -Details "Matches: $names" -Hint 'use an exact ID or filename' -ErrorId 'ResourcePack.AmbiguousSelector' -Category InvalidArgument -TargetObject $Selector
    }
    return $matches[0]
}

function Enable-ModpackResourcePack {
    param(
        [Parameter(Mandatory)]$Project,
        [Parameter(Mandatory)][string]$Selector,
        [Parameter(Mandatory)][int]$Position
    )

    $inventory = Get-ModpackInventory -Project $Project
    [void](Assert-ModpackDefaultOptionsInstalled -Project $Project -Inventory $inventory)
    $target = Resolve-ModpackResourcePack -Inventory $inventory -Selector $Selector
    $remaining = @($inventory.ActiveResources | Where-Object { -not $_.Id.Equals($target.DefaultId, [System.StringComparison]::OrdinalIgnoreCase) } | ForEach-Object Id)
    $maximum = $remaining.Count + 1
    if ($Position -lt 1 -or $Position -gt $maximum) {
        Throw-MpError -Message "Resource pack position '$Position' is outside the allowed range 1-$maximum" -Hint "--position <1-$maximum>" -ErrorId 'ResourcePack.InvalidPosition' -Category InvalidArgument -TargetObject $Position
    }
    $ordered = [System.Collections.Generic.List[string]]::new()
    foreach ($id in $remaining) { $ordered.Add($id) }
    $ordered.Insert($Position - 1, $target.DefaultId)
    Set-DefaultResourcePackOrder -Project $Project -Ids @($ordered)

    $updated = Get-ModpackInventory -Project $Project
    $item = $updated.ActiveResources | Where-Object { $_.Id.Equals($target.DefaultId, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
    return [pscustomobject]@{ Item = $item; Inventory = $updated; WasActive = $target.Active }
}

function Move-ModpackResourcePack {
    param(
        [Parameter(Mandatory)]$Project,
        [Parameter(Mandatory)][string]$Selector,
        [Parameter(Mandatory)][int]$Position
    )

    $inventory = Get-ModpackInventory -Project $Project
    [void](Assert-ModpackDefaultOptionsInstalled -Project $Project -Inventory $inventory)
    $target = Resolve-ModpackResourcePack -Inventory $inventory -Selector $Selector
    if (-not $target.Active) {
        Throw-MpError -Message "Resource pack '$($target.Name)' is disabled and cannot be moved" -Hint "modpack resource enable '$Selector' --position $Position --project $($Project.Id)" -ErrorId 'ResourcePack.NotEnabled' -Category InvalidOperation -TargetObject $Selector
    }
    return Enable-ModpackResourcePack -Project $Project -Selector $Selector -Position $Position
}

function Disable-ModpackResourcePack {
    param(
        [Parameter(Mandatory)]$Project,
        [Parameter(Mandatory)][string]$Selector
    )

    $inventory = Get-ModpackInventory -Project $Project
    [void](Assert-ModpackDefaultOptionsInstalled -Project $Project -Inventory $inventory)
    $target = Resolve-ModpackResourcePack -Inventory $inventory -Selector $Selector
    if (-not $target.Active) {
        return [pscustomobject]@{ Item = $target; Inventory = $inventory; WasActive = $false }
    }
    $remaining = @(
        $inventory.ActiveResources |
            Where-Object { -not $_.Id.Equals($target.DefaultId, [System.StringComparison]::OrdinalIgnoreCase) } |
            ForEach-Object Id
    )
    Set-DefaultResourcePackOrder -Project $Project -Ids $remaining
    return [pscustomobject]@{
        Item      = $target
        Inventory = Get-ModpackInventory -Project $Project
        WasActive = $true
    }
}
