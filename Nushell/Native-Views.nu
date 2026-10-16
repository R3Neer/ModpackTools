use ../presentation/r3cli

def field [value: any, name: string, fallback: any = null] {
    if ($value | describe) !~ '^record' {return $fallback}
    $value | get --optional $name | default $fallback
}

def text [value: any] {if $value == null {''} else {$value | into string}}

def header [ui: record, project: record, summary: record] {
    r3cli heading $ui (([(text $project.name) (text $project.version)] | str join ' ') | str trim)
    for row in [[ID $project.id] [Minecraft $project.minecraft] [Loader ($project.loader | default '?' | str capitalize)] [Root $project.root] [Mods $summary.mods] [Resources (($summary.resources_enabled | into string) + ' enabled')] [Shaders $summary.shaders]] {
        r3cli key-value $ui $row.0 (text $row.1) --width 11
    }
}

def reference [number: any, width: int] {
    if $number == null {''} else {$'[(($number | into string | fill --width $width --alignment right))]'}
}

def side-entry [ui: record, item: record, width: int, side: string] {
    mut segments: list<any> = [{text: $'  (reference $item.number $width) ', role: accent}]
    if $side in ['client' 'both'] {$segments = ($segments | append {text: '[C]', role: client})}
    if $side in ['server' 'both'] {$segments = ($segments | append {text: '[H]', role: host})}
    if $side not-in ['client' 'server' 'both'] {$segments = ($segments | append {text: '[?]', role: secondary})}
    $segments = ($segments | append [{text: $' (r3cli symbol $ui success) ', role: success} {text: $item.name, bold: true}])
    if $item.pinned {$segments = ($segments | append {text: ' [pinned]'})}
    if $item.source == 'local' {$segments = ($segments | append {text: '  LOCAL', role: local})}
    if $item.filename != null {$segments = ($segments | append {text: $'  ($item.filename)', role: secondary})}
    r3cli line $ui $segments
}

def resources [ui: record, items: list<any>, width: int, hide_empty: bool] {
    let active = ($items | where state == 'active')
    let inactive = ($items | where state != 'active')
    if not $hide_empty or not ($active | is-empty) or ($inactive | is-empty) {
        r3cli section $ui 'RESOURCE PACKS · ACTUAL PRIORITY' ($active | length)
        if ($active | is-empty) {r3cli line $ui [{text: '  · None', role: secondary}]}
    }
    for item in $active {
        let label = if $item.number == null {$'($item.priority).'} else {reference $item.number $width}
        mut segments: list<any> = [{text: $'  ($label) ', role: accent} {text: $'(r3cli symbol $ui success) ', role: success} {text: $item.name, bold: true}]
        if $item.number != null {$segments = ($segments | append {text: $'  priority ($item.priority)', role: accent})}
        if $item.source == 'local' {$segments = ($segments | append {text: '  LOCAL', role: local})}
        let source = match $item.source {'builtin' => 'built-in', 'missing' => 'file not found', _ => (text $item.filename)}
        if $source != '' {$segments = ($segments | append {text: $'  ($source)', role: secondary})}
        r3cli line $ui $segments
    }
    if not ($inactive | is-empty) {
        r3cli section $ui 'RESOURCE PACKS · PRESENT BUT DISABLED' ($inactive | length)
        for item in $inactive {
            mut segments: list<any> = [{text: $'  (reference $item.number $width) ', role: accent} {text: $'(r3cli symbol $ui inactive) ', role: secondary} {text: $item.name, bold: true}]
            if $item.source == 'local' {$segments = ($segments | append {text: '  LOCAL', role: local})}
            if $item.filename != null {$segments = ($segments | append {text: $'  ($item.filename)', role: secondary})}
            r3cli line $ui $segments
        }
    }
}

def inventory [ui: record, envelope: record] {
    let data = $envelope.data
    let view = $data.inventory
    let display = $envelope.presentation
    header $ui $data.project $data.summary
    let health = if not ($data.health.errors | is-empty) {($data.health.errors | length | into string) + ' dependency issue(s)'} else if not ($data.health.unknown | is-empty) {'Incomplete verification'} else if not ($data.health.warnings | is-empty) {'Verified with ' + ($data.health.warnings | length | into string) + ' warning(s)'} else {'Healthy'}
    r3cli key-value $ui Health $health
    for item in $display.health_items {r3cli status $ui (if $item.status == 'fail' {'error'} else if $item.status == 'warn' {'warning'} else {'info'}) $item.text}
    r3cli line $ui
    r3cli line $ui [{text: 'FILTERS  ', role: accent} {text: (if ($view.filters | is-empty) {'none'} else {$view.filters | str join ' · '}), role: secondary}]
    r3cli line $ui [{text: 'MATCHES  ', role: accent} {text: ($view.total_matches | into string)}]
    if $view.total_matches == 0 {r3cli line $ui [{text: 'No items match the filters.', role: process}]; return}
    let width = ($view.items | each {|item| text $item.number | str length} | math max)
    let hide_empty = not ($view.filters | is-empty)
    if 'mod' in $view.included_types {
        let mods = ($view.items | where type == 'mod')
        if ($mods | is-empty) and not $hide_empty {r3cli section $ui MODS 0; r3cli line $ui [{text: '  · None', role: secondary}]}
        if not ($mods | is-empty) {
            r3cli line $ui
            r3cli line $ui [{text: '  [C]', role: client} {text: ' Client    ', role: secondary} {text: '[H]', role: host} {text: ' Host    ', role: secondary} {text: '[C]', role: client} {text: '[H]', role: host} {text: ' Both', role: secondary}]
            for group in $display.groups {
                r3cli section $ui $'MODS · ($group.name)' ($group.rows | length)
                for row in $group.rows {
                    let item = ($view.items | get $row.index)
                    if $row.invalid_category != null {r3cli status $ui warning $"'($item.name)' references the missing category '($row.invalid_category)' and is shown as unclassified."}
                    side-entry $ui $item $width $row.side
                }
            }
        }
    }
    if 'resourcepack' in $view.included_types {
        let rows = ($view.items | where type == 'resourcepack')
        if not $hide_empty or not ($rows | is-empty) {resources $ui $rows $width $hide_empty}
    }
    if 'shaderpack' in $view.included_types {
        let rows = ($view.items | where type == 'shaderpack')
        if not $hide_empty or not ($rows | is-empty) {
            r3cli section $ui 'SHADER PACKS' ($rows | length)
            if ($rows | is-empty) {r3cli line $ui [{text: '  · None', role: secondary}]}
            for item in $rows {side-entry $ui $item $width 'client'}
        }
    }
    if ($view.items | any {|item| $item.number != null}) {r3cli status $ui info 'Use these numbers with resource-pack, category, mod, or content operations. Content add uses search result numbers.'}
}

def transaction-summary [ui: record, transaction: record] {
    for change in $transaction.changes {
        let action = if $transaction.applied {match $change.action {add => 'Added', remove => 'Removed', _ => 'Changed'}} else {match $change.action {add => 'Would add', remove => 'Would remove', _ => 'Would change'}}
        r3cli status $ui info $'($action) ($change.path)'
    }
    let count = ($transaction.change_count | into string)
    let summary = if $transaction.dry_run {'Dry run: ' + $count + ' file change(s) would be applied; nothing was changed.'} else if $transaction.preview {$count + ' file change(s) planned; nothing has been changed.'} else if $transaction.applied {$count + ' file change(s) applied.'} else if $transaction.change_count == 0 {'No file changes were needed.'} else {$count + ' file change(s) were not applied.'}
    r3cli status $ui (if $transaction.applied or (not $transaction.dry_run and not $transaction.preview and $transaction.change_count == 0) {'success'} else {'info'}) $summary
    for item in ($transaction | get --optional diagnostics | default []) {r3cli status $ui warning $'($item.code): ($item.path) · ($item.message)'}
}

def build-view [ui: record, envelope: record] {
    let data = $envelope.data
    let display = $envelope.presentation
    let health = if not ($data.health.errors | is-empty) {($data.health.errors | length | into string) + ' dependency issue(s)'} else if not ($data.health.unknown | is-empty) {'Incomplete verification'} else if not ($data.health.warnings | is-empty) {'Verified with ' + ($data.health.warnings | length | into string) + ' warning(s)'} else {'Healthy'}
    r3cli key-value $ui Health $health
    for item in $display.health_items {r3cli status $ui (if $item.status == 'fail' {'error'} else if $item.status == 'warn' {'warning'} else {'info'}) $item.text}
    if '--dry-run' in $envelope.arguments {
        r3cli status $ui info 'Dry run: export validated without changing the project.'
        for item in ($data | get --optional diagnostics | default []) {r3cli status $ui warning $'($item.code): ($item.path) · ($item.message)'}
        return
    }
    for line in $display.log {r3cli line $ui [{text: $line, role: secondary}]}
    if ($display.mods | is-empty) {r3cli section $ui MODS 0; r3cli line $ui [{text: '  · None', role: secondary}]} else {
        r3cli line $ui [{text: '  [C]', role: client} {text: ' Client    ', role: secondary} {text: '[H]', role: host} {text: ' Host    ', role: secondary} {text: '[C]', role: client} {text: '[H]', role: host} {text: ' Both', role: secondary}]
        for group in $display.groups {
            r3cli section $ui $'MODS · ($group.name)' ($group.rows | length)
            for row in $group.rows {
                let item = ($display.mods | get $row.index)
                if $row.invalid_category != null {r3cli status $ui warning $"'($item.name)' references the missing category '($row.invalid_category)' and is shown as unclassified."}
                side-entry $ui $item 0 $row.side
            }
        }
    }
    resources $ui $data.resource_packs 0 false
    r3cli section $ui 'SHADER PACKS' ($display.shaders | length)
    if ($display.shaders | is-empty) {r3cli line $ui [{text: '  · None', role: secondary}]}
    for item in $display.shaders {side-entry $ui $item 0 'client'}
    let build = $data.build
    let size = if $build.size_bytes >= 1073741824 {(($build.size_bytes / 1073741824) | into string --decimals 2) + ' GB'} else if $build.size_bytes >= 1048576 {(($build.size_bytes / 1048576) | into string --decimals 2) + ' MB'} else if $build.size_bytes >= 1024 {(($build.size_bytes / 1024) | into string --decimals 2) + ' KB'} else {($build.size_bytes | into string) + ' B'}
    r3cli status $ui success 'Status: successful'
    for row in [[File ($build.path | path basename)] [Size $size] [Duration (($build.duration_seconds | into string --decimals 2) + ' s')] [Mods $build.mods] ['Client only' $build.client_only] ['Host only' $build.host_only] [Both $build.both] [Unknown ($build.mods - $build.client_only - $build.host_only - $build.both)] ['Enabled resources' $build.resources_enabled] [Shaders $build.shaders] [Path $build.path]] {r3cli key-value $ui $row.0 (text $row.1) --width 20}
    for item in ($data | get --optional diagnostics | default []) {r3cli status $ui warning $'($item.code): ($item.path) · ($item.message)'}
}

export def repair [ui: record, envelope: record] {
    let receipt = $envelope.data.doctor.repair
    if $receipt.packwiz_installed {r3cli status $ui success ('Verified Packwiz installed: ' + $receipt.packwiz_path)}
    if $receipt.configuration_updated {r3cli status $ui success 'Packwiz was saved as the ModpackTools executable without changing PATH.'}
    if (field (field $envelope presentation {}) kind '') == 'doctor-repair' {
        if not $receipt.project_applied {
            for item in $envelope.presentation.items {r3cli status $ui info ($item.name + ': ' + $item.previous + ' -> ' + $item.next + ' (' + $item.reason + ')')}
            for item in $envelope.presentation.health_items {r3cli status $ui (if $item.status == 'fail' {'error'} else if $item.status == 'warn' {'warning'} else {'info'}) $item.text}
        }
        if $receipt.change_count == 0 {r3cli status $ui success 'No repairable project changes are required.'} else {
            let transaction = {applied: $receipt.project_applied, dry_run: $receipt.dry_run, preview: $receipt.preview, change_count: $receipt.change_count, changes: ($receipt.changes | each {|item| {action: $item.kind, path: $item.path}})}
            transaction-summary $ui $transaction
        }
    }
    for item in $receipt.diagnostics {
        r3cli status $ui warning $'($item.code): ($item.path) · ($item.message)'
        if $item.retained_evidence {r3cli status $ui warning ('Retained recovery evidence: ' + $item.path)}
    }
    if $receipt.diagnostics_omitted > 0 {r3cli status $ui warning (($receipt.diagnostics_omitted | into string) + ' additional repair diagnostics; inspect native recovery state.')}
}

export def render [ui: record, envelope: record] {
    let data = $envelope.data
    if ($envelope.command | str lowercase) == 'doctor-repair' {repair $ui $envelope; return}
    if ($envelope.command | str lowercase) == 'build' {build-view $ui $envelope; return}
    let operation = ($envelope.arguments | get --optional 0 | default '' | str lowercase)
    if ($envelope.command | str lowercase) == 'resource-pack' {
        transaction-summary $ui $data.transaction
        if not $data.transaction.dry_run {resources $ui $data.resource_packs 1 true}
        return
    }
    if ($envelope.command | str lowercase) == 'content' and $operation in ['add' 'update'] {
        for item in $envelope.presentation.items {r3cli status $ui info ($item.name + ': ' + $item.previous + ' -> ' + $item.next + ' (' + $item.reason + ')')}
        for item in $envelope.presentation.health_items {r3cli status $ui (if $item.status == 'fail' {'error'} else if $item.status == 'warn' {'warning'} else {'info'}) $item.text}
        transaction-summary $ui $data.transaction
        return
    }
    if (($envelope.command | str lowercase) == 'category' and $operation in ['assign' 'clear' 'remove']) or (($envelope.command | str lowercase) == 'mod') or (($envelope.command | str lowercase) == 'content' and $operation in ['pin' 'unpin']) {
        transaction-summary $ui $data.transaction
        return
    }
    match ($envelope.command | str lowercase) {
        'project' => {
            match $operation {
                'register' => {
                    let project = $envelope.presentation.project
                    r3cli status $ui success 'Existing Packwiz project registered with ModpackTools.'
                    r3cli key-value $ui ID $project.id
                    r3cli key-value $ui Minecraft (text $project.minecraft)
                    r3cli key-value $ui Loader ([$project.loader $project.loader_version] | where {|value| $value != null and $value != ''} | str join ' ')
                    r3cli key-value $ui Root $project.root
                    r3cli status $ui info ('Created ' + ($envelope.presentation.created_files | length | into string) + ' file(s). Next: modpack project use ' + $project.id)
                    for item in ($envelope.presentation | get --optional diagnostics | default []) {r3cli status $ui warning $'($item.code): ($item.path) · ($item.message)'}
                }
                'create' => {
                    r3cli status $ui success ('Project created at ' + $envelope.presentation.project.root)
                    for item in ($envelope.presentation | get --optional diagnostics | default []) {r3cli status $ui warning $'($item.code): ($item.path) · ($item.message)'}
                }
                'list' => {
                    r3cli line $ui [{text: $'Root: ($data.projects.root)', role: secondary}]
                    r3cli line $ui
                    r3cli table $ui ['ID' 'NAME' 'MC' 'LOADER'] ($data.projects.items | each {|item| [$item.id (text $item.name) (text $item.minecraft) (text $item.loader)]})
                    r3cli line $ui [{text: (($data.projects.items | length | into string) + ' project(s)'), role: secondary}]
                }
                'status' => {header $ui $data.project $data.summary}
                'current' => {if $data.active_project == null {r3cli status $ui info 'There is no active project in this session.'} else {r3cli key-value $ui ID $data.active_project}}
                'use' => {r3cli status $ui success ('Active project: ' + $data.active_project + ' (' + (text $envelope.presentation.name) + ')')}
            }
        }
        'category' => {
            let view = $data.categories
            if $operation == 'create' {r3cli status $ui success ("Category '" + $envelope.arguments.1 + "' was created.")}
            r3cli key-value $ui Project $view.project.id
            r3cli section $ui CLASSIFICATIONS ($view.items | length)
            let width = ($view.items | each {|item| $item.number | into string | str length} | math max)
            for item in $view.items {
                let order = if $item.unclassified {'not assigned'} else {$'order ($item.order)'}
                let mods = if $item.mod_count == 1 {'1 mod'} else {$'($item.mod_count) mods'}
                r3cli line $ui [{text: $'  (reference $item.number $width) ', role: accent} {text: $item.name, bold: true} {text: $' ($item.id) · ($mods) · ($order)', role: secondary}]
            }
            r3cli line $ui
            r3cli status $ui info 'Use a category ID or number with category assign. Only defined categories can be removed.'
            for item in ($data | get --optional diagnostics | default []) {r3cli status $ui warning $'($item.code): ($item.path) · ($item.message)'}
        }
        'config' => {
            if $operation == 'set' {
                r3cli status $ui success ($envelope.presentation.name + ' = ' + $envelope.presentation.value)
                return
            }
            r3cli key-value $ui $envelope.presentation.name $envelope.presentation.value
            if $envelope.presentation.source != null {r3cli key-value $ui source $envelope.presentation.source}
        }
        'content' => {
            if $operation == 'remove' {
                let transaction = $data.transaction
                if not $transaction.applied {
                    for item in $envelope.presentation.items {
                        let previous = if ($item.version_id | default '') == '' {'installed'} else {$item.version_id}
                        r3cli status $ui info ($item.name + ': ' + $previous + ' -> removed (' + $item.reason + ')')
                    }
                    for item in $envelope.presentation.health_items {r3cli status $ui (if $item.status == 'fail' {'error'} else if $item.status == 'warn' {'warning'} else {'info'}) $item.text}
                }
                transaction-summary $ui $transaction
                return
            }
            if $operation == 'list' {inventory $ui $envelope; return}
            if $operation == 'versions' {
                let view = $data.versions
                r3cli key-value $ui Item $view.item_name
                r3cli key-value $ui Project $'($data.project.id) · Minecraft ($data.project.minecraft) · ($data.project.loader)'
                r3cli key-value $ui Type $view.item_type
                r3cli key-value $ui Compatible ($view.items | length)
                if ($view.items | is-empty) {r3cli status $ui info 'No compatible versions were found.'; return}
                r3cli section $ui 'AVAILABLE VERSIONS' ($view.items | length)
                let width = ($view.items | each {|item| $item.number | into string | str length} | math max)
                for item in $view.items {
                    let marker = if $item.installed {'INSTALLED'} else if $item.number == 1 {'LATEST'} else {''}
                    r3cli line $ui [{text: $'  (reference $item.number $width) ', role: process} {text: $item.version, role: value, bold: true} {text: $' ($marker) ', role: success} {text: $'($item.version_type) · ($item.id)', role: secondary}]
                    r3cli line $ui [{text: ('      ' + ([(text $item.filename) $item.published] | where {|value| $value != ''} | str join ' · ')), role: secondary}]
                }
                r3cli status $ui info 'Select one with modpack content update <content> --to <number>. Exact version IDs also work.'
            } else if $operation == 'search' {
                let view = $data.search
                r3cli key-value $ui Query $view.query
                if $data.project != null {r3cli key-value $ui Project $'($data.project.id) · Minecraft ($data.project.minecraft) · ($data.project.loader)'} else {r3cli key-value $ui Compatibility 'Any Minecraft version / loader'}
                r3cli key-value $ui Type $view.type
                r3cli key-value $ui Found $'(($view.results | length)) shown · ($view.total_hits) total'
                if ($view.results | is-empty) {r3cli status $ui info (if $data.project == null {'No results were found.'} else {'No compatible results were found.'}); return}
                r3cli section $ui RESULTS ($view.results | length)
                let width = ($view.results | each {|item| $item.number | into string | str length} | math max)
                for item in $view.results {
                    let label = match $item.type {'resourcepack' => 'RESOURCE', 'shaderpack' => 'SHADER', _ => ($item.type | str uppercase)}
                    r3cli line $ui [{text: $'  (reference $item.number $width) ', role: process} {text: $'[($label)] ', role: accent} {text: $item.name, role: value}]
                    r3cli line $ui [{text: $'      ID ($item.id)  slug ($item.slug)  by ($item.author)  downloads ($item.downloads)', role: secondary}]
                    if $item.description != '' {r3cli line $ui [{text: $'      ($item.description)', role: secondary}]}
                }
                r3cli status $ui info (if $data.project == null {'Select a project, then install a result with modpack content add <number>. IDs and slugs also work.'} else {'Install a result with modpack content add <number>. You can also use its ID or slug.'})
            }
        }
        'diff' => {
            r3cli key-value $ui Project (text $envelope.presentation.project.name)
            r3cli key-value $ui Baseline $envelope.presentation.baseline
            r3cli key-value $ui 'Built UTC' $envelope.presentation.built_utc
            if $data.diff.total == 0 {r3cli status $ui success 'No differences from the latest build.'; return}
            for group in [{name: added, symbol: '+', role: success} {name: changed, symbol: '~', role: process} {name: removed, symbol: '-', role: error}] {
                let raw = ($data.diff | get $group.name)
                let rows = (if ($raw | describe) =~ '^record' {[$raw]} else {$raw} | where {|row| $row.path != null})
                if ($rows | is-empty) {continue}
                r3cli section $ui $'DIFF · ($group.name | str uppercase)' ($rows | length)
                for item in $rows {r3cli line $ui [{text: $'  ($group.symbol) ', role: $group.role} {text: $'[($item.kind)] ', role: accent} {text: $item.path, role: value}]}
            }
            r3cli key-value $ui Differences $data.diff.total
        }
        _ => {error make {msg: 'This native read view is not available.'}}
    }
}
