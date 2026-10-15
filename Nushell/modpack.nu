const MODULE_DIR = path self .
const BRIDGE = ($MODULE_DIR | path join 'Invoke-ModpackBridge.ps1')

def field [value: any, name: string, default_value: any = null] {
    if (($value | describe) !~ '^record') { return $default_value }
    let result = ($value | get --optional $name)
    if $result == null { $default_value } else { $result }
}

def data-field [data: any, name: string] {
    field $data $name
}

def wants-machine-output [args: list<string>] {
    $args | any {|token| $token == '--no-human' }
}

def validate-nu-option-style [args: list<string>] {
    mut parse_options = true

    for token in $args {
        if not $parse_options {
            continue
        }

        if $token == '--' {
            $parse_options = false
            continue
        }

        if ($token | str starts-with '--') {
            if $token !~ '^--[a-z][a-z0-9-]*(=.*)?$' {
                error make {
                    msg: $"Nushell options must use lowercase '--long-option' or one-letter '-s' syntax. Option '($token)' is not valid Nushell style."
                }
            }
            continue
        }

        if ($token =~ '^-[A-Za-z]') and ($token != '-V') and ($token !~ '^-[a-z]$') {
            error make {
                msg: $"Nushell options must use lowercase '--long-option' or one-letter '-s' syntax. PowerShell-style option '($token)' is not supported by the Nu adapter."
            }
        }
    }
}

def invoke-bridge [request: string] {
    # Keep stdin attached to the terminal so interactive confirmations inside the
    # PowerShell engine can wait for an answer. The request and stdout use separate
    # temporary files; explicit decoding preserves the external UTF-8 byte stream.
    let request_path = ($nu.temp-dir | path join $'modpacktools-((random uuid)).request.json')
    let capture_path = ($nu.temp-dir | path join $'modpacktools-((random uuid)).json')
    $request | save --force $request_path
    let stdout = try {
        ^pwsh -NoLogo -NoProfile -File $BRIDGE -RequestPath $request_path o> $capture_path
        if ($capture_path | path exists) {
            let captured = (open --raw $capture_path)
            if (($captured | describe) == 'binary') {
                $captured | decode utf-8
            } else {
                $captured
            }
        } else {
            ''
        }
    } catch {|err|
        if ($request_path | path exists) { rm --force $request_path }
        if ($capture_path | path exists) { rm --force $capture_path }
        error make { msg: $'Could not run the ModpackTools bridge: ($err.msg)' }
    }

    if ($request_path | path exists) { rm --force $request_path }
    if ($capture_path | path exists) { rm --force $capture_path }
    $stdout
}

def owns-native-command [args: list<string>] {
    let executable = ($env.MODPACKTOOLS_CORE_PATH? | default '')
    if ($executable == '') or not (wants-machine-output $args) { return false }
    let command = ($args | get --optional 0 | default '')
    if $command in ['--version' '-V'] { return true }
    let operation = ($args | get --optional 1 | default '')
    if (($command | str lowercase) in ['content' 'category']) and (($operation | str lowercase) == 'list') {
        return (not ($args | any {|token| $token in ['--help' '-h'] }))
    }
    if (($command | str lowercase) == 'content') and (($operation | str lowercase) in ['versions' 'search' 'add' 'update' 'pin' 'unpin' 'remove']) {
        return (not ($args | any {|token| $token in ['--help' '-h'] }))
    }
    if ($command | str lowercase) in ['resource-pack' 'config' 'mod' 'build' 'diff'] {
        return (not ($args | any {|token| $token in ['--help' '-h'] }))
    }
    if (($command | str lowercase) == 'category') and (($operation | str lowercase) in ['assign' 'clear' 'remove' 'create']) {
        return (not ($args | any {|token| $token in ['--help' '-h'] }))
    }
    (($command | str lowercase) == 'project') and (($operation | str lowercase) in ['list' 'current' 'use' 'status' 'register' 'create']) and not ($args | any {|token| $token in ['--help' '-h'] })
}

def validate-string-fields [value: any, names: list<string>, nullable: bool = false] {
    if (($value | describe) !~ '^record') {
        error make { msg: 'ModpackTools native executable returned an invalid record.' }
    }
    for name in $names {
        if not ($name in ($value | columns)) {
            error make { msg: $'ModpackTools native executable omitted field ($name).' }
        }
        let item = ($value | get $name)
        if ($item != null or not $nullable) and (($item | describe) != 'string') {
            error make { msg: $'ModpackTools native executable returned an invalid field ($name).' }
        }
    }
}

def validate-native-requirement [value: any] {
    mut pending = [$value]
    mut depth = 1
    while not ($pending | is-empty) {
        if $depth > 64 { error make { msg: 'ModpackTools native requirement exceeds the depth limit.' } }
        $pending = ($pending | each --flatten {|node|
            validate-string-fields $node ['Target' 'Kind' 'Side' 'Scope' 'Source' 'SuggestedVersionId']
            if not ('Range' in ($node | columns)) or ((field $node Maven null | describe) != 'bool') {
                error make { msg: 'ModpackTools native executable returned an invalid requirement.' }
            }
            for name in ['Any' 'All'] {
                if ((field $node $name null | describe) !~ '^(list|table)') {
                    error make { msg: 'ModpackTools native executable returned invalid requirement alternatives.' }
                }
            }
            if not ('Unless' in ($node | columns)) {
                error make { msg: 'ModpackTools native executable omitted a requirement guard.' }
            }
            let children = ($node.Any | append $node.All)
            if $node.Unless == null { $children } else { $children | append $node.Unless }
        })
        $depth = $depth + 1
    }
}

def validate-native-health [health: any] {
    for name in ['errors' 'warnings' 'unknown'] {
        if ((field $health $name null | describe) !~ '^(list|table)') {
            error make {msg: 'ModpackTools native executable returned invalid health data.'}
        }
        for issue in (field $health $name []) {
            validate-string-fields $issue ['Key' 'Code' 'Owner' 'OwnerName' 'Side' 'Severity' 'Message']
            let severity = if $name == 'errors' {'error'} else if $name == 'warnings' {'warning'} else {'unknown'}
            if not ('Requirement' in ($issue | columns)) or $issue.Severity != $severity {
                error make {msg: 'ModpackTools native executable returned an invalid health issue.'}
            }
            if $issue.Requirement != null and (($issue.Requirement | describe) != 'string') {validate-native-requirement $issue.Requirement}
        }
    }
}

def validate-native-transaction [result: record, args: list<string>] {
    let transaction = (field (field $result data {}) transaction {})
    if $result.command != $args.0 or ($result.arguments | length) == 0 or $result.arguments.0 != $args.1 {
        error make { msg: 'ModpackTools native executable returned an invalid mutation command.' }
    }
    for name in ['applied' 'dry_run' 'preview'] {
        if ((field $transaction $name null | describe) != 'bool') {
            error make { msg: 'ModpackTools native executable returned an invalid transaction state.' }
        }
    }
    if ((field $transaction changes null | describe) !~ '^(list|table)') or ((field $transaction change_count null | describe) != 'int') {
        error make { msg: 'ModpackTools native executable returned invalid transaction changes.' }
    }
    let folded = ($args | each {|token| $token | str lowercase})
    let removal = ($folded.0 == 'content') and ($folded.1 == 'remove')
    let should_preview = $removal and (not $transaction.dry_run) and (('--yes' not-in $folded) or $transaction.change_count == 0)
    let should_apply = (not $transaction.dry_run) and (not $transaction.preview) and ($transaction.change_count > 0)
    if $transaction.change_count < 0 or $transaction.change_count != ($transaction.changes | length) or $transaction.preview != $should_preview or $transaction.applied != $should_apply or $transaction.dry_run != ('--dry-run' in $folded) {
        error make { msg: 'ModpackTools native executable returned contradictory transaction fields.' }
    }
    for change in $transaction.changes {
        validate-string-fields $change ['action' 'path']
        validate-string-fields $change ['reason'] true
        if not ($change.action in ['add' 'remove' 'change']) {
            error make { msg: 'ModpackTools native executable returned an invalid file action.' }
        }
    }
    if 'diagnostics' in ($transaction | columns) {
        if (($transaction.diagnostics | describe) !~ '^(list|table)') {
            error make { msg: 'ModpackTools native executable returned invalid transaction diagnostics.' }
        }
        for diagnostic in $transaction.diagnostics { validate-string-fields $diagnostic ['code' 'path' 'message'] }
    }
}

def invoke-native-command [args: list<string>] {
    let executable = $env.MODPACKTOOLS_CORE_PATH
    if not ($executable | path exists) {
        error make { msg: 'The configured ModpackTools native executable does not exist.' }
    }
    let native_args = if '--json' in $args { $args } else { $args | append '--json' }
    let request_path = ($nu.temp-dir | path join $'modpacktools-((random uuid)).native.json')
    # The child inherits Nu's exported environment, including ENV_CONVERSIONS.
    # A raw JSON snapshot would bypass those conversions for session values.
    let request = {schema_version: 1, arguments: $native_args}
    $request | to json | save --force $request_path
    let capture = try {
        ^$executable --request-path $request_path | complete
    } catch {|err|
        rm --force $request_path
        error make { msg: $'Could not run the ModpackTools native executable: ($err.msg)' }
    }
    rm --force $request_path
    if $capture.stderr != '' {
        error make { msg: $'ModpackTools native transport failed: ($capture.stderr)' }
    }
    let result = try { $capture.stdout | from json } catch {
        error make { msg: 'ModpackTools native executable returned invalid JSON.' }
    }
    if ((field $result schema_version 0 | describe) != 'int') or (field $result schema_version 0) != 1 {
        error make { msg: 'ModpackTools native executable returned an unsupported result schema.' }
    }
    if (($result | describe) !~ '^record') or ((field $result ok null | describe) != 'bool') or ((field $result command null | describe) != 'string') or ((field $result arguments null | describe) !~ '^list') {
        error make { msg: 'ModpackTools native executable returned an invalid result envelope.' }
    }
    if ($result.arguments | any {|token| ($token | describe) != 'string' }) {
        error make { msg: 'ModpackTools native executable returned invalid argument types.' }
    }
    if (field $result ok false) {
        if 'error' in ($result | columns) {
            error make { msg: 'ModpackTools native executable returned contradictory success fields.' }
        }
        if ($args | get 0) in ['--version' '-V'] {
            if $result.command != '--version' or ((field (field (field $result data {}) version {}) version null | describe) != 'string') {
                error make { msg: 'ModpackTools native executable returned an invalid version result.' }
            }
        } else if ($args.0 | str lowercase) == 'diff' {
            let diff = (field (field $result data {}) diff {})
            if $result.command != $args.0 or not ($result.arguments | is-empty) or ((field $diff total null | describe) != 'int') or $diff.total < 0 {
                error make {msg: 'Native diff returned invalid statistics.'}
            }
            mut count = 0
            for name in ['added' 'changed' 'removed'] {
                if not ($name in ($diff | columns)) {error make {msg: 'Native diff omitted a change section.'}}
                let value = ($diff | get $name)
                let rows = if $value == null {[]} else if (($value | describe) =~ '^record') {[$value]} else {$value}
                if (($rows | describe) !~ '^(list|table)') {error make {msg: 'Native diff returned invalid changes.'}}
                let ghost = if (($value | describe) =~ '^record') {('kind' in ($value | columns)) and ('path' in ($value | columns)) and ($value.kind == null) and ($value.path == null)} else {false}
                if not $ghost {
                    for row in $rows {validate-string-fields $row ['kind' 'path']}
                    $count = $count + ($rows | length)
                }
            }
            if $count != $diff.total {error make {msg: 'Native diff returned contradictory counts.'}}
            if 'diagnostics' in ($result.data | columns) {
                if (($result.data.diagnostics | describe) !~ '^(list|table)') {error make {msg: 'Native diff diagnostics are invalid.'}}
                for diagnostic in $result.data.diagnostics {validate-string-fields $diagnostic ['code' 'path' 'message']}
            }
        } else if ($args.0 | str lowercase) == 'build' {
            if $result.command != $args.0 or ($result.arguments | any {|token| $token not-in $args}) {
                error make {msg: 'Native build returned an invalid command.'}
            }
            let data = (field $result data {})
            validate-native-health (field $data health null)
            let dry = ($args | any {|token| ($token | str lowercase) in ['--dry-run' '-n']})
            if $dry != ($result.arguments | any {|token| ($token | str lowercase) == '--dry-run'}) {error make {msg: 'Native build returned contradictory arguments.'}}
            if $dry {
                if 'build' in ($data | columns) or 'resource_packs' in ($data | columns) {error make {msg: 'Native build dry-run returned a published artifact.'}}
            } else {
                let build = (field $data build {})
                validate-string-fields $build ['path']
                if ((field $build dry_run null | describe) != 'bool') or $build.dry_run {error make {msg: 'Native build returned an invalid dry-run state.'}}
                for name in ['size_bytes' 'mods' 'client_only' 'host_only' 'both' 'resources_enabled' 'shaders'] {
                    if ((field $build $name null | describe) != 'int') or (field $build $name (-1)) < 0 {error make {msg: 'Native build returned an invalid count.'}}
                }
                if $build.mods < ($build.client_only + $build.host_only + $build.both) or ((field $build duration_seconds null | describe) !~ '^(int|float)$') or $build.duration_seconds < 0 {
                    error make {msg: 'Native build returned contradictory statistics.'}
                }
                if ((field $data resource_packs null | describe) !~ '^(list|table)') {error make {msg: 'Native build returned invalid resource inventory.'}}
                for item in $data.resource_packs {
                    validate-string-fields $item ['id' 'source' 'state']
                    validate-string-fields $item ['name' 'type' 'side' 'filename' 'category' 'version_id' 'project_id' 'slug'] true
                    if $item.type != 'resourcepack' or $item.state not-in ['active' 'inactive'] or ((field $item pinned null | describe) != 'bool') or not ('number' in ($item | columns)) or $item.number != null or not ('priority' in ($item | columns)) {
                        error make {msg: 'Native build returned invalid resource fields.'}
                    }
                    if ($item.state == 'active' and (((field $item priority null | describe) != 'int') or ($item.priority < 1))) or ($item.state == 'inactive' and $item.priority != null) {
                        error make {msg: 'Native build returned invalid resource priority.'}
                    }
                }
            }
            if 'diagnostics' in ($data | columns) {
                if (($data.diagnostics | describe) !~ '^(list|table)') {error make {msg: 'Native build diagnostics are invalid.'}}
                for diagnostic in $data.diagnostics {validate-string-fields $diagnostic ['code' 'path' 'message']}
            }
        } else if (($args.0 | str lowercase) == 'project') and (($args.1 | str lowercase) in ['register' 'create']) {
            if $result.command != $args.0 or ($result.arguments | length) < 2 or $result.arguments.0 != $args.1 or ((field $result data null | describe) !~ '^record') or not ($result.data | columns | is-empty) {
                let message = if ($args.1 | str lowercase) == 'create' {'Native project creation returned an invalid result.'} else {'Native project registration returned an invalid result.'}
                error make {msg: $message}
            }
        } else if ($args.1 | str lowercase) in ['current' 'use'] {
            let selecting = ($args.1 | str lowercase) == 'use'
            let argument_count = if $selecting { 2 } else { 1 }
            if $result.command != $args.0 or ($result.arguments | length) != $argument_count or $result.arguments.0 != $args.1 {
                error make { msg: 'ModpackTools native executable returned an invalid session result.' }
            }
            let session = (field $result data {})
            validate-string-fields $session ['active_project'] (not $selecting)
            if $selecting and (($session.active_project | str trim) == '' or not ($result.arguments.1 in ($args | skip 2))) {
                error make { msg: 'ModpackTools native executable returned an invalid selected project.' }
            }
        } else if ($args.1 | str lowercase) == 'status' {
            let data = (field $result data {})
            let project = (field $data project {})
            let summary = (field $data summary {})
            if $result.command != $args.0 or ($result.arguments | length) != 1 or $result.arguments.0 != $args.1 {
                error make { msg: 'ModpackTools native executable returned an invalid project status.' }
            }
            validate-string-fields $project ['id' 'root' 'output_name']
            validate-string-fields $project ['name' 'version' 'minecraft' 'loader' 'loader_version'] true
            for name in ['mods' 'resources_enabled' 'resources_disabled' 'shaders'] {
                if ((field $summary $name null | describe) != 'int') or (field $summary $name (-1)) < 0 {
                    error make { msg: 'ModpackTools native executable returned an invalid project summary.' }
                }
            }
        } else if ((($result.command | str lowercase) == 'content') and (($args.1 | str lowercase) in ['add' 'update' 'pin' 'unpin' 'remove'])) or ((($result.command | str lowercase) == 'category') and (($args.1 | str lowercase) in ['assign' 'clear' 'remove'])) or (($result.command | str lowercase) == 'mod') {
            validate-native-transaction $result $args
        } else if ($result.command | str lowercase) == 'resource-pack' {
            validate-native-transaction $result $args
            let data = (field $result data {})
            if $data.transaction.dry_run {
                if 'resource_packs' in ($data | columns) { error make {msg: 'Native resource dry-run returned inventory.'} }
            } else {
                if ((field $data resource_packs null | describe) !~ '^(list|table)') { error make {msg: 'Native resource operation returned invalid inventory.'} }
                for item in $data.resource_packs {
                    validate-string-fields $item ['id' 'type' 'source' 'state']
                    validate-string-fields $item ['name' 'side' 'filename' 'category' 'version_id' 'project_id' 'slug'] true
                    if $item.type != 'resourcepack' or $item.state not-in ['active' 'inactive'] or ((field $item pinned null | describe) != 'bool') or not ('number' in ($item | columns)) or $item.number != null or not ('priority' in ($item | columns)) {
                        error make {msg: 'Native resource operation returned invalid item fields.'}
                    }
                    if ($item.state == 'active' and (((field $item priority null | describe) != 'int') or ($item.priority < 1))) or ($item.state == 'inactive' and $item.priority != null) {
                        error make {msg: 'Native resource operation returned invalid priority.'}
                    }
                }
            }
        } else if ($result.command | str lowercase) == 'config' {
            if $result.command != $args.0 or ($result.arguments | length) < 2 or $result.arguments.0 != $args.1 or ((field $result data null | describe) !~ '^record') or not ($result.data | columns | is-empty) {
                error make {msg: 'Native configuration command returned an invalid result.'}
            }
        } else if ($result.command | str lowercase) == 'category' {
            let category_data = (field $result data {})
            let categories = (field $category_data categories {})
            if 'diagnostics' in ($category_data | columns) {
                if (($category_data.diagnostics | describe) !~ '^(list|table)') { error make {msg: 'Native category diagnostics are invalid.'} }
                for diagnostic in $category_data.diagnostics { validate-string-fields $diagnostic ['code' 'path' 'message'] }
            }
            let project = (field $categories project {})
            validate-string-fields $project ['id' 'root' 'output_name']
            validate-string-fields $project ['name' 'version' 'minecraft' 'loader' 'loader_version'] true
            if $result.command != $args.0 or ($result.arguments | length) == 0 or $result.arguments.0 != $args.1 or (($args.1 | str lowercase) == 'list' and ($result.arguments | length) != 1) or (($args.1 | str lowercase) == 'create' and ($result.arguments | length) < 2) or ((field $categories items null | describe) !~ '^(list|table)') {
                error make { msg: 'ModpackTools native executable returned an invalid category list.' }
            }
            for item in $categories.items {
                validate-string-fields $item ['id' 'name']
                for name in ['number' 'mod_count'] {
                    if ((field $item $name null | describe) != 'int') or (field $item $name (-1)) < 0 {
                        error make { msg: 'ModpackTools native executable returned an invalid category count.' }
                    }
                }
                if $item.number < 1 or not ('order' in ($item | columns)) or ($item.order != null and ($item.order | describe) != 'int') or ((field $item unclassified null | describe) != 'bool') {
                    error make { msg: 'ModpackTools native executable returned invalid category fields.' }
                }
            }
        } else if (($result.command | str lowercase) == 'content') and (($args.1 | str lowercase) == 'search') {
            let data = (field $result data {})
            if not ('project' in ($data | columns)) { error make { msg: 'ModpackTools native search omitted project context.' } }
            let project = $data.project
            if $project != null {
                validate-string-fields $project ['id' 'root' 'output_name']
                validate-string-fields $project ['name' 'version' 'minecraft' 'loader' 'loader_version'] true
            }
            let search = (field $data search {})
            validate-string-fields $search ['query' 'type']
            if $result.command != $args.0 or ($result.arguments | length) < 2 or $result.arguments.0 != $args.1 or ((field $search total_hits null | describe) != 'int') or ((field $search results null | describe) !~ '^(list|table)') {
                error make { msg: 'ModpackTools native executable returned invalid search results.' }
            }
            for item in $search.results {
                validate-string-fields $item ['id' 'slug' 'name' 'type' 'author' 'description']
                if ((field $item number null | describe) != 'int') or (field $item number 0) < 1 or ((field $item downloads null | describe) != 'int') {
                    error make { msg: 'ModpackTools native executable returned invalid search fields.' }
                }
            }
        } else if (($result.command | str lowercase) == 'content') and (($args.1 | str lowercase) == 'versions') {
            let data = (field $result data {})
            let versions = (field $data versions {})
            validate-string-fields (field $data project {}) ['id' 'root' 'output_name']
            validate-string-fields (field $data project {}) ['name' 'version' 'minecraft' 'loader' 'loader_version'] true
            validate-string-fields $versions ['item_name' 'item_type']
            if $result.command != $args.0 or ($result.arguments | length) != 2 or $result.arguments.0 != $args.1 or not ($result.arguments.1 in ($args | skip 2)) or ((field $versions items null | describe) !~ '^(list|table)') {
                error make { msg: 'ModpackTools native executable returned an invalid version list.' }
            }
            for item in $versions.items {
                validate-string-fields $item ['id' 'version' 'version_type' 'published']
                validate-string-fields $item ['filename'] true
                if ((field $item number null | describe) != 'int') or (field $item number 0) < 1 or ((field $item installed null | describe) != 'bool') {
                    error make { msg: 'ModpackTools native executable returned invalid version fields.' }
                }
            }
        } else if ($result.command | str lowercase) == 'content' {
            let data = (field $result data {})
            let inventory = (field $data inventory {})
            let summary = (field $data summary {})
            let health = (field $data health {})
            if $result.command != $args.0 or ($result.arguments | length) == 0 or $result.arguments.0 != $args.1 {
                error make { msg: 'ModpackTools native executable returned an invalid content command.' }
            }
            validate-string-fields (field $data project {}) ['id' 'root' 'output_name']
            validate-string-fields (field $data project {}) ['name' 'version' 'minecraft' 'loader' 'loader_version'] true
            for name in ['mods' 'resources_enabled' 'resources_disabled' 'shaders'] {
                if ((field $summary $name null | describe) != 'int') or (field $summary $name (-1)) < 0 {
                    error make { msg: 'ModpackTools native executable returned an invalid inventory summary.' }
                }
            }
            for name in ['errors' 'warnings' 'unknown'] {
                if ((field $health $name null | describe) !~ '^(list|table)') {
                    error make { msg: 'ModpackTools native executable returned invalid health data.' }
                }
                for issue in (field $health $name []) {
                    validate-string-fields $issue ['Key' 'Code' 'Owner' 'OwnerName' 'Side' 'Severity' 'Message']
                    let severity = if $name == 'errors' { 'error' } else if $name == 'warnings' { 'warning' } else { 'unknown' }
                    if not ('Requirement' in ($issue | columns)) or $issue.Severity != $severity {
                        error make { msg: 'ModpackTools native executable returned an invalid health issue.' }
                    }
                    if $issue.Requirement != null and (($issue.Requirement | describe) != 'string') {
                        validate-native-requirement $issue.Requirement
                    }
                }
            }
            for name in ['filters' 'included_types'] {
                if ((field $inventory $name []) | any {|value| ($value | describe) != 'string' }) {
                    error make { msg: 'ModpackTools native executable returned invalid content labels.' }
                }
            }
            for name in ['items' 'filters' 'included_types'] {
                if ((field $inventory $name null | describe) !~ '^(list|table)') {
                    error make { msg: 'ModpackTools native executable returned invalid content data.' }
                }
            }
            if ((field $inventory total_matches null | describe) != 'int') or $inventory.total_matches != ($inventory.items | length) {
                error make { msg: 'ModpackTools native executable returned an invalid content count.' }
            }
            for item in $inventory.items {
                validate-string-fields $item ['id' 'type' 'source']
                validate-string-fields $item ['name' 'side' 'filename' 'category' 'state' 'version_id' 'project_id' 'slug'] true
                for name in ['number' 'priority'] {
                    if not ($name in ($item | columns)) {
                        error make { msg: 'ModpackTools native executable omitted content numbering.' }
                    }
                    let value = (field $item $name null)
                    if $value != null and (($value | describe) != 'int' or $value < 1) {
                        error make { msg: 'ModpackTools native executable returned invalid content numbering.' }
                    }
                }
                if ((field $item pinned null | describe) != 'bool') {
                    error make { msg: 'ModpackTools native executable returned an invalid pin state.' }
                }
            }
        } else {
            let projects = (field (field $result data {}) projects {})
            validate-string-fields $projects ['root']
            if $result.command != $args.0 or ($result.arguments | length) != 1 or $result.arguments.0 != $args.1 or ((field $projects items null | describe) !~ '^(list|table)') {
                error make { msg: 'ModpackTools native executable returned an invalid project catalog.' }
            }
            for item in $projects.items {
                validate-string-fields $item ['id' 'root' 'output_name']
                validate-string-fields $item ['name' 'version' 'minecraft' 'loader' 'loader_version'] true
            }
        }
    } else {
        if 'data' in ($result | columns) {
            error make { msg: 'ModpackTools native executable returned contradictory failure fields.' }
        }
        let failure = (field $result error {})
        if (($failure | describe) !~ '^record') or not ('target' in ($failure | columns)) or ((field $failure id null | describe) != 'string') or ((field $failure message null | describe) != 'string') or ((field $failure category null | describe) != 'string') {
            error make { msg: 'ModpackTools native executable returned an invalid error result.' }
        }
    }
    let valid_exit = if (field $result ok false) { $capture.exit_code == 0 } else { $capture.exit_code in [1 2] }
    if not $valid_exit {
        error make { msg: 'ModpackTools native exit status disagrees with its result.' }
    }
    $capture.stdout
}

def unwrap-result [envelope: record] {
    let command = (field $envelope command '')
    let arguments = (field $envelope arguments [])
    let operation = ($arguments | get --optional 0 | default '')
    let data = (field $envelope data {})

    match $command {
        '--version' => {
            let version = (data-field $data version)
            if $version == null { $data } else { $version }
        }
        'project' => {
            if $operation == 'list' {
                let projects = (data-field $data projects)
                if $projects == null { $data } else { field $projects items [] }
            } else { $data }
        }
        'content' => {
            match $operation {
                'list' => {
                    let inventory = (data-field $data inventory)
                    if $inventory == null { $data } else { field $inventory items [] }
                }
                'search' => {
                    let search = (data-field $data search)
                    if $search == null { $data } else { field $search results [] }
                }
                'versions' => {
                    let versions = (data-field $data versions)
                    if $versions == null { $data } else { field $versions items [] }
                }
                _ => {
                    let transaction = (data-field $data transaction)
                    if $transaction == null { $data } else { $transaction }
                }
            }
        }
        'category' => {
            let categories = (data-field $data categories)
            if $categories != null { field $categories items [] } else {
                let transaction = (data-field $data transaction)
                if $transaction == null { $data } else { $transaction }
            }
        }
        'doctor' => {
            let doctor = (data-field $data doctor)
            if $doctor == null { $data } else { $doctor }
        }
        'diff' => {
            let diff = (data-field $data diff)
            if $diff == null { $data } else { $diff }
        }
        'build' => {
            let build = (data-field $data build)
            if $build == null { $data } else { $build }
        }
        _ => {
            let transaction = (data-field $data transaction)
            if $transaction == null { $data } else { $transaction }
        }
    }
}

# Unmigrated commands use the PowerShell engine. The bridge always requests one JSON
# envelope internally. Normal Nu usage consumes that machine payload silently and
# leaves only R3CLI presentation visible; --no-human exposes the parsed Nu value.
export def --env --wrapped main [...args: string] {
    validate-nu-option-style $args
    let machine_output = (wants-machine-output $args)
    let request = ({
        arguments: $args
        human_stderr_terminal: (is-terminal --stderr)
    } | to json)
    let raw = if (owns-native-command $args) { invoke-native-command $args } else { invoke-bridge $request }
    let text = ($raw | str trim)

    if $text == '' {
        error make { msg: 'ModpackTools bridge returned no JSON data.' }
    }

    let envelope = try {
        $text | from json
    } catch {|err|
        error make { msg: $'ModpackTools returned invalid JSON: ($err.msg)' }
    }

    if not (field $envelope ok false) {
        let failure = (field $envelope error {})
        let message = (field $failure message 'ModpackTools failed.')
        let id = (field $failure id '')
        let category = (field $failure category '')
        let suffix = ([
            (if $id == '' { null } else { $id })
            (if $category == '' { null } else { $category })
        ] | compact | str join ' · ')
        error make { msg: (if $suffix == '' { $message } else { $'($message) [($suffix)]' }) }
    }

    if ((field $envelope command '') == 'project') and (($envelope.arguments | get --optional 0 | default '') == 'use') {
        let use_args = (field $envelope arguments [])
        if not ($use_args | any {|token| $token == '--help' }) {
            let active_project = (data-field (field $envelope data {}) active_project)
            if $active_project != null {
                $env.MODPACKTOOLS_PROJECT = ($active_project | into string)
            }
        }

        if $machine_output {
            return (unwrap-result $envelope)
        }
        return
    }

    if $machine_output {
        return (unwrap-result $envelope)
    }
}
