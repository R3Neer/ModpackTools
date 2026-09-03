@{
    RootModule = 'R3CLI.psm1'
    ModuleVersion = '0.4.1'
    GUID = '76d75e68-14c7-4de8-b47b-c5b31fa7f9c7'
    Author = 'R3Neer'
    Description = 'The R3CLI visual language for PowerShell consumers.'
    PowerShellVersion = '7.0'
    FunctionsToExport = @('New-R3Console', 'Write-R3Banner', 'Write-R3Heading', 'Write-R3Section', 'Write-R3Status', 'Write-R3Line', 'Write-R3KeyValue', 'Write-R3Table', 'Write-R3Help', 'Test-R3HelpCatalogue', 'Format-R3Diagnostic', 'Get-R3Symbol')
    CmdletsToExport = @()
    VariablesToExport = @()
    AliasesToExport = @()
    PrivateData = @{ ApiVersion = 1 }
}
