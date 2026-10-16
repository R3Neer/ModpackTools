# R3CLI visual-language adapter for Nushell 0.115+.
#
# Use as a module:
#   use ./r3cli
#   let ui = r3cli console
#   r3cli banner $ui 'MY TOOL 1.0'

const MODULE_DIR = path self .

# Load the generated package resources when present. Falling back to the
# canonical repository sources keeps the module usable from a source checkout.
def resources []: nothing -> record {
    let packaged = ($MODULE_DIR | path join 'resources.json')
    if ($packaged | path exists) {
        open $packaged
    } else {
        let root = ($MODULE_DIR | path join '../..')
        {
            colours: ((open ($root | path join 'src/r3_cli/default_theme.toml')).colours)
            symbols: (open ($root | path join 'src/r3_cli/symbols.json'))
        }
    }
}

def fail [message: string] {
    error make { msg: $message }
}

def nonempty [value: any]: nothing -> bool {
    if $value == null { false } else { (($value | into string | str trim) != '') }
}

def text-width [text: string]: nothing -> int {
    let clean = ($text | ansi strip)
    ($clean | str stats | get 'unicode-width')
}

def clean-text [value: any]: nothing -> string {
    ($value | into string | str replace --all "\u{1b}" '' | str replace --all "\r\n" "\n" | str replace --all "\t" '    ')
}

def field [value: any, name: string, default_value: any = null] {
    if (($value | describe) !~ '^record') { return $default_value }
    let result = ($value | get --optional $name)
    if $result == null { $default_value } else { $result }
}

def role-exists [console: record, role: string]: nothing -> bool {
    $role in ($console.theme | columns)
}

def styled [
    console: record
    use_colour: bool
    text: string
    role: any = null
    bold: bool = false
]: nothing -> string {
    let clean = (clean-text $text)
    if not $use_colour { return $clean }

    if ($role != null) and (not (role-exists $console ($role | into string))) {
        fail $"R3CLI.Theme.UnknownRole: '($role)'."
    }

    let prefix = if $role == null {
        if $bold { ansi bo } else { '' }
    } else {
        let role_name = ($role | into string)
        let colour = ($console.theme | get $role_name)
        if $bold { ansi { fg: $colour, attr: b } } else { ansi $colour }
    }

    if $prefix == '' { $clean } else { $"($prefix)($clean)(ansi reset)" }
}

def segment-text [segment: any]: nothing -> string {
    if (($segment | describe) == 'string') {
        clean-text $segment
    } else {
        clean-text (field $segment 'text' '')
    }
}

def wrap-plain [text: string, width: int]: nothing -> list<string> {
    let normalized = (clean-text $text)
    if (text-width $normalized) <= $width { return [$normalized] }

    mut output = []
    for paragraph in ($normalized | split row "\n") {
        if $paragraph == '' {
            $output = ($output | append '')
            continue
        }

        let indent_match = ($paragraph | parse --regex '^(?<indent>\s*)' | first)
        let indent = ($indent_match | get --optional indent | default '')
        let body = ($paragraph | str trim --left)
        # `split words` tokenizes punctuation away. R3CLI wrapping must preserve
        # every non-whitespace token verbatim, including periods and brackets.
        let words = ($body | parse --regex '(?<word>\S+)' | get word)
        mut current = $indent

        for word in $words {
            let candidate = if ($current | str trim) == '' { $"($current)($word)" } else { $"($current) ($word)" }
            if (text-width $candidate) <= $width {
                $current = $candidate
            } else {
                if ($current | str trim) != '' { $output = ($output | append $current) }
                $current = $"($indent)($word)"
            }
        }
        if ($current | str trim) != '' { $output = ($output | append $current) }
    }
    $output
}

# Construct a rendering context. `colour` follows R3CLI's auto/always/never
# contract. `is-terminal` and `sink` exist mainly for deterministic tests and
# embedding. A sink closure receives `(text, stream)` and keeps human rendering
# out of Nushell's structured pipeline just like direct terminal output does.
export def console [
    --colour: string = 'auto'
    --ascii
    --width: int
    --theme-extension: record = {}
    --is-terminal: any = null
    --sink: any = null
]: nothing -> record {
    if $colour not-in ['auto' 'always' 'never'] {
        fail $"R3CLI.Colour.Invalid: '($colour)'."
    }

    for item in ($theme_extension | transpose key value) {
        if (($item.value | describe) != 'string') or ($item.value !~ '^#[0-9A-Fa-f]{6}$') {
            fail $"R3CLI.Theme.InvalidColours: '($item.key)' must use #RRGGBB."
        }
    }

    let loaded = (resources)
    let actual_width = if $width == null {
        let columns = ((term size).columns | default 80)
        if $columns > 0 { $columns } else { 80 }
    } else {
        $width
    }
    if ($actual_width < 1) or ($actual_width > 10000) {
        fail 'R3CLI.Console.InvalidWidth: width must be between 1 and 10000.'
    }

    {
        theme: ($loaded.colours | merge $theme_extension)
        symbols: $loaded.symbols
        ascii: $ascii
        width: $actual_width
        colour: $colour
        is_terminal: $is_terminal
        sink: $sink
    }
}

export def symbol [console: record, kind: string]: nothing -> string {
    if $kind not-in ($console.symbols | columns) {
        fail $"R3CLI.Symbol.Unknown: '($kind)'."
    }
    let values = ($console.symbols | get $kind)
    if $console.ascii { $values.1 } else { $values.0 }
}

export def line [
    console: record
    segments: list<any> = []
    --no-newline
    --stderr
]: nothing -> nothing {
    for segment in $segments {
        if (($segment | describe) =~ '^record') {
            let role = (field $segment 'role')
            if ($role != null) and (not (role-exists $console ($role | into string))) {
                fail $"R3CLI.Theme.UnknownRole: '($role)'."
            }
        }
    }

    # Nushell 0.114+ makes `is-terminal` redirection-aware. Detect at the
    # emission boundary; calling it inside `styled`, whose result is collected
    # by `each`, incorrectly reports redirected output and disables auto colour.
    let use_colour = match $console.colour {
        'always' => true
        'never' => false
        _ => {
            if (($env | get --optional NO_COLOR) != null) {
                false
            } else if $console.is_terminal != null {
                $console.is_terminal
            } else if $stderr {
                if (is-terminal --stderr) { true } else { false }
            } else {
                if (is-terminal --stdout) { true } else { false }
            }
        }
    }

    let plain = ($segments | each {|segment| segment-text $segment } | str join '')
    let wrapped = (wrap-plain $plain $console.width)

    let rendered = if ($wrapped | length) == 1 and ($plain !~ "\n") {
        let rendered_line = ($segments | each {|segment|
            if (($segment | describe) == 'string') {
                styled $console $use_colour ($segment | into string)
            } else {
                styled $console $use_colour (field $segment 'text' '') (field $segment 'role') (field $segment 'bold' false)
            }
        } | str join '')
        [$rendered_line]
    } else {
        let records = ($segments | where {|it| ($it | describe) =~ '^record'})
        let first_record = if ($records | is-empty) { {} } else { $records | first }
        let role = (field $first_record 'role')
        let bold = (field $first_record 'bold' false)
        $wrapped | each {|item| styled $console $use_colour $item $role $bold }
    }

    let sink = (field $console 'sink')
    let stream = if $stderr { 'stderr' } else { 'stdout' }
    for item in $rendered {
        if $sink != null {
            do $sink $item $stream | ignore
        } else if $stderr {
            if $no_newline { print --stderr --no-newline $item } else { print --stderr $item }
        } else {
            if $no_newline { print --no-newline $item } else { print $item }
        }
    }
}

export def banner [console: record, text: string]: nothing -> nothing {
    let rule_width = ([68 $console.width] | math min)
    let rule = ('' | fill --width $rule_width --character (symbol $console banner))
    line $console
    line $console [{ text: $rule, role: secondary }]
    line $console [{ text: $" ($text)", role: heading, bold: true }]
    line $console [{ text: $rule, role: secondary }]
}

export def heading [console: record, text: string]: nothing -> nothing {
    line $console
    line $console [{ text: ($text | str uppercase), role: heading, bold: true }]
}

export def section [console: record, title: string, count: any = null]: nothing -> nothing {
    line $console
    mut segments = [{ text: $"  ($title)", role: heading }]
    if $count != null { $segments = ($segments | append { text: $"  ($count)", role: accent }) }
    line $console $segments
    let rule_width = ([64 ($console.width - 2)] | math min | into int)
    let safe_width = if $rule_width < 0 { 0 } else { $rule_width }
    let rule = ('' | fill --width $safe_width --character (symbol $console rule))
    line $console [{ text: $"  ($rule)", role: secondary }]
}

export def status [console: record, kind: string, text: string]: nothing -> nothing {
    let role = (match $kind {
        step => 'process'
        success => 'success'
        info => 'heading'
        warning => 'process'
        error => 'error'
        _ => { fail $"R3CLI.Status.Unknown: '($kind)'." }
    })
    let segments = [
        { text: (symbol $console $kind), role: $role }
        { text: $" ($text)", role: value }
    ]
    if $kind == 'warning' { line $console $segments --stderr } else { line $console $segments }
}

export def key-value [console: record, key: string, value: any, --width: int = 16]: nothing -> nothing {
    let value_text = ($value | into string)
    let inline_width = ($width + 1 + (text-width $value_text))
    if ($console.width < 40) or (($width + 4) >= $console.width) or ($inline_width > $console.width) {
        line $console [{ text: $key, role: secondary }]
        line $console [{ text: $"  ($value_text)", role: value }]
    } else {
        let padded = ($key | fill --width $width --alignment left)
        line $console [{ text: $"($padded) ", role: secondary } { text: $value_text, role: value }]
    }
}

export def table [console: record, headers: list<string>, rows: list<any>]: nothing -> nothing {
    let count = ($headers | length)
    for row in $rows {
        if (($row | length) != $count) { fail 'R3CLI.Table.InvalidRow: column count differs.' }
    }
    if $count == 0 { return }

    let raw_width = (($console.width - (2 * ($count - 1))) / $count)
    let width = ([1 ($raw_width | math floor | into int)] | math max)

    if $width < 12 {
        for row in $rows {
            for index in 0..<($count) {
                key-value $console ($headers | get $index) ($row | get $index)
            }
            line $console
        }
        return
    }

    let header_line = ($headers | each {|item| $item | fill --width $width --alignment left } | str join '  ')
    line $console [{ text: $header_line, role: heading }]

    for row in $rows {
        let has_long = ($row | any {|item| (text-width ($item | into string)) > $width })
        if $has_long {
            for index in 0..<($count) {
                key-value $console ($headers | get $index) ($row | get $index)
            }
        } else {
            let row_line = ($row | each {|item| ($item | into string) | fill --width $width --alignment left } | str join '  ')
            line $console [{ text: $row_line, role: value }]
        }
    }
}

def normalize-catalogue [catalogue: record]: nothing -> record {
    let nested = ($catalogue | get --optional help)
    if $nested == null {
        $catalogue
    } else {
        $nested | merge { commands: ($catalogue | get --optional commands | default []) }
    }
}

def catalogue-groups [catalogue: record]: nothing -> list<any> {
    let explicit = ($catalogue | get --optional 'group-order')
    if $explicit == null { $catalogue | get --optional groups | default [] } else { $explicit }
}

export def test-help-catalogue [catalogue: record, --executable-commands: list<string>]: nothing -> bool {
    let catalogue = (normalize-catalogue $catalogue)
    for name in [product description invocation] {
        if not (nonempty ($catalogue | get --optional $name)) {
            fail $"R3CLI.Help.Invalid: ($name) is empty."
        }
    }

    let help_options = ($catalogue | get --optional 'help-options' | default ['-h' '--help'])
    if ('--help' not-in $help_options) or ($help_options | any {|it| not (nonempty $it) }) or (($help_options | uniq | length) != ($help_options | length)) {
        fail 'R3CLI.Help.Invalid: help options must be unique, nonempty and include --help.'
    }

    let groups = (catalogue-groups $catalogue)
    if (($groups | uniq | length) != ($groups | length)) { fail 'R3CLI.Help.Invalid: duplicate groups.' }

    let commands = ($catalogue | get --optional commands | default [])
    mut names = []
    for command in $commands {
        let name = ($command | get --optional name | default '' | into string)
        if (not (nonempty $name)) or ($name == 'help') or ($name in $names) {
            fail $"R3CLI.Help.Invalid: duplicate or invalid command '($name)'."
        }
        $names = ($names | append $name)
        let group = ($command | get --optional group)
        if $group not-in $groups { fail $"R3CLI.Help.Invalid: unknown group for '($name)'." }
        for required in [summary description usage] {
            let values = ($command | get --optional $required | default [])
            let values = if (($values | describe) =~ '^list') { $values } else { [$values] }
            if (($values | length) == 0) or ($values | any {|it| not (nonempty $it) }) {
                fail $"R3CLI.Help.Invalid: missing ($required) for '($name)'."
            }
        }
        for item in ($command | get --optional items | default []) {
            if (not (nonempty ($item | get --optional label))) or (not (nonempty ($item | get --optional description))) {
                fail $"R3CLI.Help.Invalid: incomplete item for '($name)'."
            }
        }
    }

    for item in ($catalogue | get --optional 'global-items' | default []) {
        if (not (nonempty ($item | get --optional label))) or (not (nonempty ($item | get --optional description))) {
            fail 'R3CLI.Help.Invalid: incomplete global item.'
        }
    }

    if $executable_commands != null {
        let missing = ($names | where {|it| $it not-in $executable_commands })
        let extra = ($executable_commands | where {|it| $it not-in $names })
        if (($missing | length) > 0) or (($extra | length) > 0) {
            fail 'R3CLI.Help.DispatchMismatch: catalogue differs from executable commands.'
        }
    }
    true
}

def help-row [console: record, label: string, description: string, width: int]: nothing -> nothing {
    if ($console.width < 40) or (($width + 4) >= $console.width) {
        line $console [{ text: $"  ($label)", role: accent }]
        line $console [{ text: $"    ($description)", role: secondary }]
    } else {
        let padded = ($label | fill --width $width --alignment left)
        line $console [{ text: $"  ($padded)", role: accent } { text: $description, role: secondary }]
    }
}

export def help [console: record, catalogue: record, command: string = '']: nothing -> nothing {
    let catalogue = (normalize-catalogue $catalogue)
    test-help-catalogue $catalogue | ignore
    let commands = ($catalogue | get --optional commands | default [])

    let entry = if $command != '' {
        let matches = ($commands | where name == $command)
        if (($matches | length) != 1) { fail $"R3CLI.Help.UnknownCommand: '($command)'." }
        banner $console ($command | str uppercase)
        $matches | first
    } else {
        let version = ($catalogue | get --optional version | default '')
        banner $console ($"($catalogue.product) ($version)" | str trim)
        $catalogue
    }

    line $console [{ text: ($entry | get description), role: value }]
    heading $console USAGE
    for usage in ($entry | get usage) { line $console [{ text: $"  ($usage)", role: accent }] }

    let items = if $command != '' {
        $entry | get --optional items | default []
    } else {
        $entry | get --optional 'global-items' | default []
    }
    if ($items | length) > 0 {
        heading $console (if $command != '' { 'ARGUMENTS AND OPTIONS' } else { 'GLOBAL OPTIONS' })
        let max_label = ($items | get label | each {|it| text-width ($it | into string) } | math max)
        let width = ([28 ($max_label + 2)] | math min)
        for item in $items { help-row $console $item.label $item.description $width }
    }

    if $command == '' {
        let names = ($commands | get name)
        let width = if (($names | length) == 0) { 2 } else {
            [24 (($names | each {|it| text-width $it } | math max) + 2)] | math min
        }
        for group in (catalogue-groups $catalogue) {
            let members = ($commands | where group == $group)
            if ($members | length) == 0 { continue }
            heading $console $group
            for member in $members { help-row $console $member.name $member.summary $width }
        }
    }

    let notes = ($entry | get --optional notes | default [])
    if ($notes | length) > 0 {
        if $command != '' { heading $console NOTES } else { line $console }
        for note in $notes { status $console info $note }
    }

    let examples = ($entry | get --optional examples | default [])
    if ($examples | length) > 0 {
        heading $console EXAMPLES
        for example in $examples { line $console [{ text: $"  ($example)", role: accent }] }
    }
    line $console
}

export def format-diagnostic [
    message: string
    --details: string = ''
    --hint: string = ''
    --code: string = ''
]: nothing -> string {
    def sentence [value: string]: nothing -> string {
        let text = ($value | ansi strip | str trim)
        if $text =~ '[.!?]$' { $text } else { $"($text)." }
    }

    mut lines = [(sentence $message)]
    if $code != '' {
        let clean_code = ($code | ansi strip | str trim)
        $lines = ($lines | append $"  [($clean_code)]")
    }
    if $details != '' { $lines = ($lines | append $"Details: (sentence $details)") }
    if $hint != '' {
        let clean_hint = ($hint | ansi strip | str trim)
        $lines = ($lines | append $"Try: ($clean_hint)")
    }
    $lines | str join (char newline)
}