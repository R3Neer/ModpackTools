Set-StrictMode -Version Latest

function Get-R3Field {
    [CmdletBinding()]
    param($Object, [string]$Name, $Default = $null)
    if ($null -eq $Object) { return $Default }
    if ($Object -is [Collections.IDictionary]) {
        if ($Object.Contains($Name)) { return $Object[$Name] }
    } elseif ($Object.PSObject.Properties[$Name]) { return $Object.$Name }
    return $Default
}

function New-R3Console {
    [CmdletBinding()]
    param([ValidateSet('auto','always','never')][string]$Colour = 'auto', [switch]$Ascii,
        [ValidateRange(1,10000)][int]$Width = 80, [hashtable]$ThemeExtension = @{},
        [bool]$IsTerminal = (-not [Console]::IsOutputRedirected), [scriptblock]$Sink, $Invocation)
    if (-not $PSBoundParameters.ContainsKey('Width')) {
        try { if ($Host.UI.RawUI.WindowSize.Width -gt 0) { $Width = $Host.UI.RawUI.WindowSize.Width } } catch { }
    }
    if ($Invocation) {
        if ($Invocation.PipelinePosition -lt $Invocation.PipelineLength) { $IsTerminal = $false }
        if ($Invocation.Statement) {
            $tokens = $null; $parseErrors = $null
            $ast = [Management.Automation.Language.Parser]::ParseInput($Invocation.Statement, [ref]$tokens, [ref]$parseErrors)
            if ($ast.Find({ param($node) $node -is [Management.Automation.Language.RedirectionAst] }, $true)) { $IsTerminal = $false }
        }
    }
    $resources = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'resources.json') -Raw | ConvertFrom-Json -AsHashtable
    $theme = $resources.colours.Clone()
    foreach ($key in $ThemeExtension.Keys) {
        if ($ThemeExtension[$key] -isnot [string] -or $ThemeExtension[$key] -notmatch '^#[0-9A-Fa-f]{6}$') {
            throw "R3CLI.Theme.InvalidColours: '$key' must use #RRGGBB."
        }
        $theme[$key] = $ThemeExtension[$key].ToUpperInvariant()
    }
    [pscustomobject]@{
        Theme = $theme; Symbols = $resources.symbols; Ascii = [bool]$Ascii; Width = $Width
        UseColour = ($Colour -eq 'always' -or ($Colour -eq 'auto' -and $IsTerminal -and $null -eq [Environment]::GetEnvironmentVariable('NO_COLOR')))
        Sink = $Sink; Pending = [Collections.Generic.List[object]]::new()
    }
}

function Get-R3Symbol {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Console, [Parameter(Mandatory)][string]$Kind)
    if (-not $Console.Symbols.Contains($Kind)) { throw "R3CLI.Symbol.Unknown: '$Kind'." }
    return $Console.Symbols[$Kind][[int]$Console.Ascii]
}

function Send-R3Text {
    [CmdletBinding()]
    param($Console, [string]$Text, [ValidateSet('information','warning')][string]$Stream = 'information')
    if ($Console.Sink) { & $Console.Sink $Text $Stream | Out-Null }
    elseif ($Stream -eq 'warning') { Write-Warning $Text }
    else { Write-Information -MessageData $Text -InformationAction Continue -Tags R3CLI }
}

function Get-R3CellWidth {
    [CmdletBinding()]
    param([string]$Element)
    $code = [char]::ConvertToUtf32($Element, 0)
    if ([Globalization.CharUnicodeInfo]::GetUnicodeCategory($Element, 0) -in @('NonSpacingMark','EnclosingMark','Format')) { return 0 }
    if ($code -ge 0x1100 -and ($code -le 0x115f -or $code -in @(0x2329,0x232a) -or
        ($code -ge 0x2e80 -and $code -le 0xa4cf) -or ($code -ge 0xac00 -and $code -le 0xd7a3) -or
        ($code -ge 0xf900 -and $code -le 0xfaff) -or ($code -ge 0xfe10 -and $code -le 0xfe6f) -or
        ($code -ge 0xff01 -and $code -le 0xff60) -or ($code -ge 0xffe0 -and $code -le 0xffe6) -or
        ($code -ge 0x1f300 -and $code -le 0x1faff) -or $code -ge 0x20000)) { return 2 }
    return 1
}

function Write-R3Line {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Console, [AllowEmptyCollection()][object[]]$Segments = @(),
        [switch]$NoNewline, [ValidateSet('information','warning')][string]$Stream = 'information')
    # Validate before buffering so a failed segment cannot corrupt a later response.
    foreach ($segment in $Segments) {
        $role = Get-R3Field $segment 'Role'
        if ($role -and -not $Console.Theme.Contains($role)) { throw "R3CLI.Theme.UnknownRole: '$role'." }
    }
    foreach ($segment in $Segments) { $Console.Pending.Add($segment) }
    if ($NoNewline) { return }
    $line = [Text.StringBuilder]::new(); $column = 0; $escape = [string][char]27
    try {
        foreach ($segment in $Console.Pending) {
            $text = if ($segment -is [string]) { $segment } else { [string](Get-R3Field $segment 'Text' '') }
            $text = $text.Replace("`r`n", "`n").Replace("`t", '    ').Replace($escape, '')
            $role = Get-R3Field $segment 'Role'; $bold = Get-R3Field $segment 'Bold' $false
            $style = ''
            if ($Console.UseColour) {
                if ($bold) { $style += "$escape[1m" }
                if ($role) {
                    $hex = $Console.Theme[$role]
                    $rgb = @(1,3,5 | ForEach-Object { [Convert]::ToInt32($hex.Substring($_,2),16) }) -join ';'
                    $style += "$escape[38;2;$($rgb)m"
                }
            }
            foreach ($token in [regex]::Matches($text, '\s+|\S+')) {
                $tokenWidth = 0
                $measure = [Globalization.StringInfo]::GetTextElementEnumerator($token.Value)
                while ($measure.MoveNext()) { $tokenWidth += Get-R3CellWidth $measure.GetTextElement() }
                if ($token.Value -match '^\S' -and $tokenWidth -le $Console.Width -and $column -gt 0 -and $column + $tokenWidth -gt $Console.Width) {
                    Send-R3Text $Console $line.ToString() $Stream
                    [void]$line.Clear(); $column = 0
                }
                $elements = [Globalization.StringInfo]::GetTextElementEnumerator($token.Value)
                while ($elements.MoveNext()) {
                    $element = $elements.GetTextElement()
                    $cells = Get-R3CellWidth $element
                    if ($element -eq "`n" -or ($column -gt 0 -and $column + $cells -gt $Console.Width)) {
                        Send-R3Text $Console $line.ToString() $Stream
                        [void]$line.Clear(); $column = 0
                    }
                    if ($element -eq "`n") { continue }
                    if ($style) { [void]$line.Append($style) }
                    [void]$line.Append($element)
                    if ($style) { [void]$line.Append("$escape[0m") }
                    $column += $cells
                }
            }
        }
        Send-R3Text $Console $line.ToString() $Stream
    } finally { $Console.Pending.Clear() }
}

function Write-R3Banner {
    [CmdletBinding()]
    param($Console, [string]$Text)
    $rule = (Get-R3Symbol $Console banner) * [Math]::Min(68,$Console.Width)
    Write-R3Line $Console
    Write-R3Line $Console @(@{ Text=$rule; Role='secondary' })
    Write-R3Line $Console @(@{ Text=" $Text"; Role='heading'; Bold=$true })
    Write-R3Line $Console @(@{ Text=$rule; Role='secondary' })
}

function Write-R3Heading {
    [CmdletBinding()]
    param($Console, [string]$Text)
    Write-R3Line $Console
    Write-R3Line $Console @(@{ Text=$Text.ToUpperInvariant(); Role='heading'; Bold=$true })
}

function Write-R3Section {
    [CmdletBinding()]
    param($Console, [string]$Title, [Nullable[int]]$Count)
    Write-R3Line $Console
    $segments = @(@{ Text="  $Title"; Role='heading' })
    if ($null -ne $Count) { $segments += @{ Text="  $Count"; Role='accent' } }
    Write-R3Line $Console $segments
    Write-R3Line $Console @(@{ Text=('  ' + ((Get-R3Symbol $Console rule) * [Math]::Max(0,[Math]::Min(64,$Console.Width - 2)))); Role='secondary' })
}

function Write-R3Status {
    [CmdletBinding()]
    param($Console, [ValidateSet('step','success','info','warning','error')][string]$Kind, [string]$Text)
    $role = @{ step='process'; success='success'; info='heading'; warning='process'; error='error' }[$Kind]
    $stream = if ($Kind -eq 'warning') { 'warning' } else { 'information' }
    Write-R3Line $Console @(@{ Text=(Get-R3Symbol $Console $Kind); Role=$role }, @{ Text=" $Text"; Role='value' }) -Stream $stream
}

function Write-R3KeyValue {
    [CmdletBinding()]
    param($Console, [string]$Key, [AllowNull()]$Value, [int]$Width = 16)
    if ($Console.Width -lt 40 -or $Width + 4 -ge $Console.Width) {
        Write-R3Line $Console @(@{Text=$Key;Role='secondary'})
        Write-R3Line $Console @(@{Text="  $Value";Role='value'})
    } else {
        Write-R3Line $Console @(@{Text=($Key.PadRight($Width) + ' ');Role='secondary'}, @{Text=[string]$Value;Role='value'})
    }
}

function Write-R3Table {
    [CmdletBinding()]
    param($Console, [string[]]$Headers, [AllowEmptyCollection()][object[]]$Rows)
    foreach ($row in $Rows) { if ($row.Count -ne $Headers.Count) { throw 'R3CLI.Table.InvalidRow: column count differs.' } }
    if (-not $Headers.Count) { return }
    $width = [Math]::Max(1, [int][Math]::Floor(($Console.Width - 2 * ($Headers.Count - 1)) / $Headers.Count))
    if ($width -lt 12) {
        foreach ($row in $Rows) {
            for ($i=0; $i -lt $Headers.Count; $i++) { Write-R3KeyValue $Console $Headers[$i] $row[$i] }
            Write-R3Line $Console
        }
        return
    }
    Write-R3Line $Console @(@{Text=(($Headers | ForEach-Object { $_.PadRight($width) }) -join '  ');Role='heading'})
    foreach ($row in $Rows) {
        # Long values use labelled rows, retaining all content rather than truncating it.
        if (@($row | Where-Object { ([string]$_).Length -gt $width }).Count) {
            for ($i=0; $i -lt $Headers.Count; $i++) { Write-R3KeyValue $Console $Headers[$i] $row[$i] }
        } else { Write-R3Line $Console @(@{Text=(($row | ForEach-Object { ([string]$_).PadRight($width) }) -join '  ');Role='value'}) }
    }
}

function Test-R3HelpCatalogue {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Catalogue, [string[]]$ExecutableCommands)
    foreach ($field in @('Product','Description','Invocation')) {
        if ([string]::IsNullOrWhiteSpace([string](Get-R3Field $Catalogue $field))) { throw "R3CLI.Help.Invalid: $field is empty." }
    }
    $helpOptions = @(Get-R3Field $Catalogue 'HelpOptions' @('-h','--help'))
    if ('--help' -notin $helpOptions -or @($helpOptions | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -or @($helpOptions | Sort-Object -Unique).Count -ne $helpOptions.Count) {
        throw 'R3CLI.Help.Invalid: help options must be unique, nonempty and include --help.'
    }
    $groups = @(Get-R3Field $Catalogue 'Groups' @()); $commands = @(Get-R3Field $Catalogue 'Commands' @())
    if (@($groups | Sort-Object -Unique).Count -ne $groups.Count) { throw 'R3CLI.Help.Invalid: duplicate groups.' }
    $names = @()
    foreach ($command in $commands) {
        $name = [string](Get-R3Field $command 'Name')
        if ([string]::IsNullOrWhiteSpace($name) -or $name -eq 'help' -or $name -in $names) { throw "R3CLI.Help.Invalid: duplicate or invalid command '$name'." }
        $names += $name
        if ((Get-R3Field $command 'Group') -notin $groups) { throw "R3CLI.Help.Invalid: unknown group for '$name'." }
        foreach ($field in @('Summary','Description','Usage')) {
            $values = @(Get-R3Field $command $field @())
            if (-not $values.Count -or @($values | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count) { throw "R3CLI.Help.Invalid: missing $field for '$name'." }
        }
        foreach ($item in @(Get-R3Field $command 'Items' @())) {
            if ([string]::IsNullOrWhiteSpace([string](Get-R3Field $item 'Label')) -or [string]::IsNullOrWhiteSpace([string](Get-R3Field $item 'Description'))) { throw "R3CLI.Help.Invalid: incomplete item for '$name'." }
        }
    }
    foreach ($item in @(Get-R3Field $Catalogue 'GlobalItems' @())) {
        if ([string]::IsNullOrWhiteSpace([string](Get-R3Field $item 'Label')) -or [string]::IsNullOrWhiteSpace([string](Get-R3Field $item 'Description'))) { throw 'R3CLI.Help.Invalid: incomplete global item.' }
    }
    if ($PSBoundParameters.ContainsKey('ExecutableCommands') -and (@($names | Where-Object { $_ -notin $ExecutableCommands }).Count -or @($ExecutableCommands | Where-Object { $_ -notin $names }).Count)) {
        throw 'R3CLI.Help.DispatchMismatch: catalogue differs from executable commands.'
    }
    return $true
}

function Write-R3HelpRow {
    [CmdletBinding()]
    param($Console, [string]$Label, [string]$Description, [int]$Width)
    if ($Console.Width -lt 40 -or $Width + 4 -ge $Console.Width) {
        Write-R3Line $Console @(@{Text="  $Label";Role='accent'})
        Write-R3Line $Console @(@{Text="    $Description";Role='secondary'})
    } else {
        Write-R3Line $Console @(@{Text=('  ' + $Label.PadRight($Width));Role='accent'}, @{Text=$Description;Role='secondary'})
    }
}

function Write-R3Help {
    [CmdletBinding()]
    param($Console, $Catalogue, [string]$Command)
    [void](Test-R3HelpCatalogue $Catalogue)
    $commands = @(Get-R3Field $Catalogue 'Commands' @())
    if ($Command) {
        $entry = @($commands | Where-Object { $_.Name -eq $Command })
        if ($entry.Count -ne 1) { throw "R3CLI.Help.UnknownCommand: '$Command'." }
        $entry = $entry[0]
        Write-R3Banner $Console $Command.ToUpperInvariant()
    } else {
        $entry = $Catalogue
        Write-R3Banner $Console ("$($Catalogue.Product) $(Get-R3Field $Catalogue 'Version' '')".Trim())
    }
    Write-R3Line $Console @(@{Text=$entry.Description;Role='value'})
    Write-R3Heading $Console USAGE
    foreach ($usage in $entry.Usage) { Write-R3Line $Console @(@{Text="  $usage";Role='accent'}) }
    $items = @(if ($Command) { Get-R3Field $entry 'Items' @() } else { Get-R3Field $entry 'GlobalItems' @() })
    if ($items.Count) {
        Write-R3Heading $Console $(if ($Command) { 'ARGUMENTS AND OPTIONS' } else { 'GLOBAL OPTIONS' })
        $width = [Math]::Min(28, ($items | ForEach-Object { $_.Label.Length } | Measure-Object -Maximum).Maximum + 2)
        foreach ($item in $items) { Write-R3HelpRow $Console $item.Label $item.Description $width }
    }
    if (-not $Command) {
        $width = [Math]::Min(24, ($commands | ForEach-Object { $_.Name.Length } | Measure-Object -Maximum).Maximum + 2)
        foreach ($group in $Catalogue.Groups) {
            $members = @($commands | Where-Object Group -eq $group)
            if (-not $members.Count) { continue }
            Write-R3Heading $Console $group
            foreach ($member in $members) { Write-R3HelpRow $Console $member.Name $member.Summary $width }
        }
    }
    $notes = @(Get-R3Field $entry 'Notes' @())
    if ($notes.Count) {
        if ($Command) { Write-R3Heading $Console NOTES } else { Write-R3Line $Console }
        foreach ($note in $notes) { Write-R3Status $Console info $note }
    }
    $examples = @(Get-R3Field $entry 'Examples' @())
    if ($examples.Count) {
        Write-R3Heading $Console EXAMPLES
        foreach ($example in $examples) { Write-R3Line $Console @(@{Text="  $example";Role='accent'}) }
    }
    Write-R3Line $Console
}

function Format-R3Diagnostic {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Message, [string]$Details, [string]$Hint, [string]$Code)
    $lines = [Collections.Generic.List[string]]::new()
    $clean = { param($value) ([regex]::Replace($value, '\x1b\[[0-?]*[ -/]*[@-~]', '')).Trim() }
    $messageText = & $clean $Message
    if ($messageText -notmatch '[.!?]$') { $messageText += '.' }
    $lines.Add($messageText)
    if ($Code) { $lines.Add('  [' + (& $clean $Code) + ']') }
    if ($Details) {
        $detailText = & $clean $Details
        if ($detailText -notmatch '[.!?]$') { $detailText += '.' }
        $lines.Add("Details: $detailText")
    }
    if ($Hint) { $lines.Add('Try: ' + (& $clean $Hint)) }
    return $lines -join [Environment]::NewLine
}

Export-ModuleMember -Function New-R3Console, Write-R3Banner, Write-R3Heading, Write-R3Section, Write-R3Status, Write-R3Line, Write-R3KeyValue, Write-R3Table, Write-R3Help, Test-R3HelpCatalogue, Format-R3Diagnostic, Get-R3Symbol
