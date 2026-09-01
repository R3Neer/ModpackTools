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
        throw "No se encontró el resource pack '$Selector'. Consulta: modpack inventory --type resourcepack"
    }
    if ($matches.Count -gt 1) {
        $names = @($matches | ForEach-Object { "'$($_.Name)' [$($_.DefaultId)]" }) -join ', '
        throw "El selector '$Selector' es ambiguo: $names. Usa el ID o filename exacto."
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
    $target = Resolve-ModpackResourcePack -Inventory $inventory -Selector $Selector
    $remaining = @($inventory.ActiveResources | Where-Object { -not $_.Id.Equals($target.DefaultId, [System.StringComparison]::OrdinalIgnoreCase) } | ForEach-Object Id)
    $maximum = $remaining.Count + 1
    if ($Position -lt 1 -or $Position -gt $maximum) {
        throw "Posición inválida '$Position'. Usa un valor entre 1 y $maximum (1 = prioridad más alta)."
    }
    $ordered = [System.Collections.Generic.List[string]]::new()
    foreach ($id in $remaining) { $ordered.Add($id) }
    $ordered.Insert($Position - 1, $target.DefaultId)
    Set-DefaultResourcePackOrder -Project $Project -Ids @($ordered)

    $updated = Get-ModpackInventory -Project $Project
    $item = $updated.ActiveResources | Where-Object { $_.Id.Equals($target.DefaultId, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
    return [pscustomobject]@{ Item = $item; Inventory = $updated; WasActive = $target.Active }
}
