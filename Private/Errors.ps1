function Add-MpSentenceTerminator {
    param([Parameter(Mandatory)][string]$Text)

    $trimmed = $Text.Trim()
    if ($trimmed -notmatch '[.!?]$') { return "$trimmed." }
    return $trimmed
}

function Throw-MpError {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Message,
        [Parameter(Mandatory)][string]$ErrorId,
        [System.Management.Automation.ErrorCategory]$Category = [System.Management.Automation.ErrorCategory]::InvalidOperation,
        [AllowNull()][object]$TargetObject,
        [AllowNull()][AllowEmptyString()][string]$Details,
        [AllowNull()][AllowEmptyString()][string]$Hint
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add((Add-MpSentenceTerminator -Text $Message))
    if (-not [string]::IsNullOrWhiteSpace($Details)) {
        $lines.Add("Details: $(Add-MpSentenceTerminator -Text $Details)")
    }
    if (-not [string]::IsNullOrWhiteSpace($Hint)) {
        $lines.Add("Try: $($Hint.Trim())")
    }

    $formatted = $lines -join [Environment]::NewLine
    $renderer = Get-Variable -Name R3Module -Scope Script -ValueOnly -ErrorAction SilentlyContinue
    if ($renderer) { $formatted = Format-R3Diagnostic -Message $Message -Details $Details -Hint $Hint }
    $exception = [System.InvalidOperationException]::new($formatted)
    $exception.Data['ModpackTools.ExpectedError'] = $true
    $exception.Data['ModpackTools.ErrorId'] = "ModpackTools.$ErrorId"
    $exception.Data['ModpackTools.ErrorCategory'] = [int]$Category
    $exception.Data['ModpackTools.TargetObject'] = $TargetObject
    throw $exception
}

function Test-MpExpectedError {
    param([Parameter(Mandatory)][System.Exception]$Exception)
    return $Exception.Data.Contains('ModpackTools.ExpectedError') -and $Exception.Data['ModpackTools.ExpectedError'] -eq $true
}
