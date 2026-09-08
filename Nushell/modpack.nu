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

def unwrap-result [envelope: record] {
    let command = (field $envelope command '')
    let data = (field $envelope data {})

    match $command {
        '--version' => {
            let version = (data-field $data version)
            if $version == null { $data } else { $version }
        }
        'list' => {
            let projects = (data-field $data projects)
            if $projects == null { $data } else { field $projects items [] }
        }
        'inventory' => {
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
        'classify' => {
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

# PowerShell remains the canonical engine. The bridge reserves stdout for one JSON envelope;
# R3CLI human output continues on stderr and therefore never contaminates the Nu pipeline.
export def --env --wrapped main [...args: string] {
    let request = ({ arguments: $args } | to json)
    let raw = ($request | ^pwsh -NoLogo -NoProfile -File $BRIDGE)
    let exit_code = $env.LAST_EXIT_CODE
    let text = ($raw | into string | str trim)

    if $text == '' {
        error make { msg: $'ModpackTools bridge returned no JSON data; exit code ($exit_code).' }
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

    if $exit_code != 0 {
        error make { msg: $'ModpackTools exited with code ($exit_code) after returning a success envelope.' }
    }

    if (field $envelope command '') == 'use' {
        let use_args = (field $envelope arguments [])
        if ($use_args | is-empty) {
            return { active_project: ($env.MODPACKTOOLS_PROJECT? | default null) }
        }
        let project = ($use_args | first)
        $env.MODPACKTOOLS_PROJECT = $project
        return { active_project: $project }
    }

    unwrap-result $envelope
}
