$script:MpMachineContext = $null

function Initialize-MpMachineContext {
    param(
        [switch]$Enabled,
        [switch]$NoHuman,
        [AllowEmptyString()][string]$Command = '',
        [object[]]$Arguments = @()
    )
    $script:MpMachineContext = [pscustomobject]@{
        Enabled = [bool]$Enabled
        NoHuman = [bool]$NoHuman
        Command = $Command
        Arguments = @($Arguments | ForEach-Object { [string]$_ })
        Data = [ordered]@{}
        Emitted = $false
    }
    return $script:MpMachineContext
}

function Test-MpMachineEnabled {
    return $null -ne $script:MpMachineContext -and $script:MpMachineContext.Enabled
}

function Set-MpMachineData {
    param([Parameter(Mandatory)][string]$Name, [AllowNull()]$Value)
    if (Test-MpMachineEnabled) { $script:MpMachineContext.Data[$Name] = $Value }
}

function Get-MpMachineProperty {
    param($Object, [Parameter(Mandatory)][string]$Name, $Default = $null)
    if ($null -eq $Object) { return $Default }
    if ($Object -is [Collections.IDictionary]) {
        if ($Object.Contains($Name)) { return $Object[$Name] }
        return $Default
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($property) { return $property.Value }
    return $Default
}

function ConvertTo-MpMachineProject {
    param($Project)
    if ($null -eq $Project) { return $null }
    return [ordered]@{
        id = Get-MpMachineProperty $Project 'Id'
        name = Get-MpMachineProperty $Project 'DisplayName'
        version = Get-MpMachineProperty $Project 'DisplayVersion'
        minecraft = Get-MpMachineProperty $Project 'MinecraftVersion'
        loader = Get-MpMachineProperty $Project 'Loader'
        loader_version = Get-MpMachineProperty $Project 'LoaderVersion'
        root = Get-MpMachineProperty $Project 'Root'
        output_name = Get-MpMachineProperty $Project 'OutputName'
    }
}

function ConvertTo-MpMachineInventoryItem {
    param($Item, [string]$State)
    $kind = Get-MpMachineProperty $Item 'Kind'
    if (-not $kind) { $kind = Get-MpMachineProperty $Item 'Type' }
    return [ordered]@{
        number = Get-MpMachineProperty $Item 'ReferenceNumber'
        id = Get-MpMachineProperty $Item 'Id'
        name = $(if (Get-MpMachineProperty $Item 'Name') { Get-MpMachineProperty $Item 'Name' } else { Get-MpMachineProperty $Item 'Title' })
        type = $kind
        side = Get-MpMachineProperty $Item 'Side'
        source = Get-MpMachineProperty $Item 'Source'
        filename = Get-MpMachineProperty $Item 'Filename'
        category = Get-MpMachineProperty $Item 'Category'
        pinned = Get-MpMachineProperty $Item 'Pinned' $false
        state = $State
        priority = Get-MpMachineProperty $Item 'Priority'
        version_id = Get-MpMachineProperty $Item 'VersionId'
        project_id = Get-MpMachineProperty $Item 'ProjectId'
        slug = Get-MpMachineProperty $Item 'Slug'
    }
}

function ConvertTo-MpMachineInventory {
    param($View)
    $items = [Collections.Generic.List[object]]::new()
    foreach ($item in @(Get-MpMachineProperty $View 'Mods' @())) { $items.Add((ConvertTo-MpMachineInventoryItem $item)) }
    foreach ($item in @(Get-MpMachineProperty $View 'ActiveResources' @())) { $items.Add((ConvertTo-MpMachineInventoryItem $item 'active')) }
    foreach ($item in @(Get-MpMachineProperty $View 'InactiveResources' @())) { $items.Add((ConvertTo-MpMachineInventoryItem $item 'inactive')) }
    foreach ($item in @(Get-MpMachineProperty $View 'Shaders' @())) { $items.Add((ConvertTo-MpMachineInventoryItem $item)) }
    return [ordered]@{
        filters = @(Get-MpMachineProperty $View 'Filters' @())
        included_types = @(Get-MpMachineProperty $View 'IncludedTypes' @())
        total_matches = Get-MpMachineProperty $View 'TotalMatches' $items.Count
        items = @($items)
    }
}

function ConvertTo-MpMachineHealth {
    param($Report)
    if ($null -eq $Report) { return $null }
    return [ordered]@{
        errors = @(Get-MpMachineProperty $Report 'Errors' @())
        warnings = @(Get-MpMachineProperty $Report 'Warnings' @())
        unknown = @(Get-MpMachineProperty $Report 'Unknown' @())
    }
}

function ConvertTo-MpMachineTransaction {
    param($Transaction, [switch]$DryRun, [switch]$Preview)
    $changes = @(
        foreach ($change in @(Get-MpMachineProperty $Transaction 'Changes' @())) {
            $before = Get-MpMachineProperty $change 'Before'
            $after = Get-MpMachineProperty $change 'After'
            [ordered]@{
                action = $(if ($null -eq $before) { 'add' } elseif ($null -eq $after) { 'remove' } else { 'change' })
                path = Get-MpMachineProperty $change 'Path'
                reason = Get-MpMachineProperty $change 'Reason'
            }
        }
    )
    return [ordered]@{
        applied = [bool](Get-MpMachineProperty $Transaction 'Applied' $false)
        dry_run = [bool]$DryRun
        preview = [bool]$Preview
        change_count = $changes.Count
        changes = $changes
    }
}

function ConvertTo-MpMachineDoctor {
    param($Report)
    return [ordered]@{
        failures = [int](Get-MpMachineProperty $Report 'Failures' 0)
        warnings = [int](Get-MpMachineProperty $Report 'Warnings' 0)
        checks = @(
            foreach ($check in @(Get-MpMachineProperty $Report 'Checks' @())) {
                [ordered]@{
                    section = Get-MpMachineProperty $check 'Section'
                    status = Get-MpMachineProperty $check 'Status'
                    label = Get-MpMachineProperty $check 'Label'
                    value = Get-MpMachineProperty $check 'Value'
                    detail = Get-MpMachineProperty $check 'Detail'
                    items = @(Get-MpMachineProperty $check 'Items' @())
                }
            }
        )
    }
}

function New-MpMachineSuccessEnvelope {
    return [ordered]@{
        schema_version = 1
        ok = $true
        command = $script:MpMachineContext.Command
        arguments = @($script:MpMachineContext.Arguments)
        data = $script:MpMachineContext.Data
    }
}

function New-MpMachineErrorEnvelope {
    param([Parameter(Mandatory)]$ErrorRecord)
    $exception = $ErrorRecord.Exception
    $errorId = if ($exception.Data.Contains('ModpackTools.ErrorId')) { [string]$exception.Data['ModpackTools.ErrorId'] } else { [string]$ErrorRecord.FullyQualifiedErrorId }
    $category = if ($exception.Data.Contains('ModpackTools.ErrorCategory')) {
        ([System.Management.Automation.ErrorCategory][int]$exception.Data['ModpackTools.ErrorCategory']).ToString()
    } else { [string]$ErrorRecord.CategoryInfo.Category }
    $target = if ($exception.Data.Contains('ModpackTools.TargetObject')) { $exception.Data['ModpackTools.TargetObject'] } else { $ErrorRecord.TargetObject }
    return [ordered]@{
        schema_version = 1
        ok = $false
        command = $(if ($script:MpMachineContext) { $script:MpMachineContext.Command } else { '' })
        arguments = $(if ($script:MpMachineContext) { @($script:MpMachineContext.Arguments) } else { @() })
        error = [ordered]@{
            id = $errorId
            message = $exception.Message
            category = $category
            target = $target
        }
    }
}

function Write-MpMachineEnvelope {
    param([Parameter(Mandatory)]$Envelope)
    if (-not (Test-MpMachineEnabled)) { return }
    $json = $Envelope | ConvertTo-Json -Depth 30 -Compress -EnumsAsStrings
    $script:MpMachineContext.Emitted = $true
    Write-Output $json
}

# Machine hooks decorate existing renderers. The renderer remains the human source of truth;
# these hooks only snapshot the same domain objects into a stable, lower-case data contract.
$script:MpOriginalWriteModpackList = (Get-Item Function:\Write-ModpackList).ScriptBlock
function Write-ModpackList {
    param([Parameter(Mandatory)][array]$Projects, [Parameter(Mandatory)][string]$Root)
    Set-MpMachineData projects ([ordered]@{ root=$Root; items=@($Projects | ForEach-Object { ConvertTo-MpMachineProject $_ }) })
    & $script:MpOriginalWriteModpackList @PSBoundParameters
}

$script:MpOriginalWriteModpackHeader = (Get-Item Function:\Write-ModpackHeader).ScriptBlock
function Write-ModpackHeader {
    param([Parameter(Mandatory)]$Project, [Parameter(Mandatory)]$Inventory)
    Set-MpMachineData project (ConvertTo-MpMachineProject $Project)
    Set-MpMachineData summary ([ordered]@{
        mods = @($Inventory.Mods).Count
        resources_enabled = @($Inventory.ActiveResources).Count
        resources_disabled = @($Inventory.InactiveResources).Count
        shaders = @($Inventory.Shaders).Count
    })
    & $script:MpOriginalWriteModpackHeader @PSBoundParameters
}

$script:MpOriginalWriteInventoryView = (Get-Item Function:\Write-InventoryView).ScriptBlock
function Write-InventoryView {
    param([Parameter(Mandatory)]$View, [switch]$ShowFilters)
    Set-MpMachineData inventory (ConvertTo-MpMachineInventory $View)
    & $script:MpOriginalWriteInventoryView @PSBoundParameters
}

$script:MpOriginalWriteResourcePackInventory = (Get-Item Function:\Write-ResourcePackInventory).ScriptBlock
function Write-ResourcePackInventory {
    param([Parameter(Mandatory)]$Inventory, [switch]$HideEmptySections, [int]$ReferenceWidth = 0)
    $items = @()
    $items += @($Inventory.ActiveResources | ForEach-Object { ConvertTo-MpMachineInventoryItem $_ 'active' })
    $items += @($Inventory.InactiveResources | ForEach-Object { ConvertTo-MpMachineInventoryItem $_ 'inactive' })
    Set-MpMachineData resource_packs $items
    & $script:MpOriginalWriteResourcePackInventory @PSBoundParameters
}

$script:MpOriginalWriteModpackDiff = (Get-Item Function:\Write-ModpackDiff).ScriptBlock
function Write-ModpackDiff {
    param([Parameter(Mandatory)]$Diff)
    $convert = {
        param($items)
        @($items | ForEach-Object { [ordered]@{ kind=(Get-MpMachineProperty $_ 'Kind'); path=(Get-MpMachineProperty $_ 'Path') } })
    }
    Set-MpMachineData diff ([ordered]@{
        total = [int](Get-MpMachineProperty $Diff 'Total' 0)
        added = & $convert (Get-MpMachineProperty $Diff 'Added' @())
        changed = & $convert (Get-MpMachineProperty $Diff 'Changed' @())
        removed = & $convert (Get-MpMachineProperty $Diff 'Removed' @())
    })
    & $script:MpOriginalWriteModpackDiff @PSBoundParameters
}

$script:MpOriginalWriteModrinthSearchResults = (Get-Item Function:\Write-ModrinthSearchResults).ScriptBlock
function Write-ModrinthSearchResults {
    param([Parameter(Mandatory)]$Search, [Parameter(Mandatory)]$Project)
    Set-MpMachineData project (ConvertTo-MpMachineProject $Project)
    Set-MpMachineData search ([ordered]@{
        query = Get-MpMachineProperty $Search 'Query'
        type = Get-MpMachineProperty $Search 'Type'
        total_hits = Get-MpMachineProperty $Search 'TotalHits' 0
        results = @(
            foreach ($item in @(Get-MpMachineProperty $Search 'Results' @())) {
                [ordered]@{
                    number = Get-MpMachineProperty $item 'Index'
                    id = Get-MpMachineProperty $item 'ProjectId'
                    slug = Get-MpMachineProperty $item 'Slug'
                    name = Get-MpMachineProperty $item 'Title'
                    type = Get-MpMachineProperty $item 'Type'
                    author = Get-MpMachineProperty $item 'Author'
                    downloads = Get-MpMachineProperty $item 'Downloads' 0
                    description = Get-MpMachineProperty $item 'Description'
                }
            }
        )
    })
    & $script:MpOriginalWriteModrinthSearchResults @PSBoundParameters
}

$script:MpOriginalWriteModrinthVersionResults = (Get-Item Function:\Write-ModrinthVersionResults).ScriptBlock
function Write-ModrinthVersionResults {
    param([Parameter(Mandatory)]$View, [Parameter(Mandatory)]$Project)
    Set-MpMachineData project (ConvertTo-MpMachineProject $Project)
    Set-MpMachineData versions ([ordered]@{
        item_name = Get-MpMachineProperty $View 'ItemName'
        item_type = Get-MpMachineProperty $View 'ItemKind'
        items = @(
            foreach ($version in @(Get-MpMachineProperty $View 'Versions' @())) {
                [ordered]@{
                    number = Get-MpMachineProperty $version 'Index'
                    id = Get-MpMachineProperty $version 'Id'
                    version = Get-MpMachineProperty $version 'VersionNumber'
                    version_type = Get-MpMachineProperty $version 'VersionType'
                    filename = Get-MpMachineProperty $version 'Filename'
                    published = Get-MpMachineProperty $version 'Published'
                    installed = [bool](Get-MpMachineProperty $version 'Installed' $false)
                }
            }
        )
    })
    & $script:MpOriginalWriteModrinthVersionResults @PSBoundParameters
}

$script:MpOriginalWriteModpackCategoryList = (Get-Item Function:\Write-ModpackCategoryList).ScriptBlock
function Write-ModpackCategoryList {
    param([Parameter(Mandatory)]$View)
    Set-MpMachineData categories ([ordered]@{
        project = ConvertTo-MpMachineProject (Get-MpMachineProperty $View 'Project')
        items = @(
            foreach ($category in @(Get-MpMachineProperty $View 'Categories' @())) {
                [ordered]@{
                    number = Get-MpMachineProperty $category 'ReferenceNumber'
                    id = Get-MpMachineProperty $category 'Id'
                    name = Get-MpMachineProperty $category 'Name'
                    order = Get-MpMachineProperty $category 'Order'
                    mod_count = Get-MpMachineProperty $category 'ModCount' 0
                    unclassified = [bool](Get-MpMachineProperty $category 'IsUnclassified' $false)
                }
            }
        )
    })
    & $script:MpOriginalWriteModpackCategoryList @PSBoundParameters
}

$script:MpOriginalWriteBuildSummary = (Get-Item Function:\Write-BuildSummary).ScriptBlock
function Write-BuildSummary {
    param([Parameter(Mandatory)]$Build)
    $mods = @(Get-MpMachineProperty (Get-MpMachineProperty $Build 'Inventory') 'Mods' @())
    Set-MpMachineData build ([ordered]@{
        path = Get-MpMachineProperty $Build 'Path'
        size_bytes = Get-MpMachineProperty $Build 'Size' 0
        duration_seconds = $([double](Get-MpMachineProperty (Get-MpMachineProperty $Build 'Duration') 'TotalSeconds' 0))
        dry_run = [bool](Get-MpMachineProperty $Build 'DryRun' $false)
        mods = $mods.Count
        client_only = @($mods | Where-Object Side -eq client).Count
        host_only = @($mods | Where-Object Side -eq server).Count
        both = @($mods | Where-Object Side -eq both).Count
        resources_enabled = @((Get-MpMachineProperty (Get-MpMachineProperty $Build 'Inventory') 'ActiveResources' @())).Count
        shaders = @((Get-MpMachineProperty (Get-MpMachineProperty $Build 'Inventory') 'Shaders' @())).Count
    })
    & $script:MpOriginalWriteBuildSummary @PSBoundParameters
}

$script:MpOriginalWriteMpHealth = (Get-Item Function:\Write-MpHealth).ScriptBlock
function Write-MpHealth {
    param($Report)
    Set-MpMachineData health (ConvertTo-MpMachineHealth $Report)
    & $script:MpOriginalWriteMpHealth @PSBoundParameters
}

$script:MpOriginalWriteMpDoctorReport = (Get-Item Function:\Write-MpDoctorReport).ScriptBlock
function Write-MpDoctorReport {
    param([Parameter(Mandatory)]$Report)
    Set-MpMachineData doctor (ConvertTo-MpMachineDoctor $Report)
    & $script:MpOriginalWriteMpDoctorReport @PSBoundParameters
}

$script:MpOriginalWriteMpTransactionSummary = (Get-Item Function:\Write-MpTransactionSummary).ScriptBlock
function Write-MpTransactionSummary {
    param($Transaction, [switch]$DryRun, [switch]$Preview)
    Set-MpMachineData transaction (ConvertTo-MpMachineTransaction $Transaction -DryRun:$DryRun -Preview:$Preview)
    & $script:MpOriginalWriteMpTransactionSummary @PSBoundParameters
}
