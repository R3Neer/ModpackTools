# Nushell adapter

This directory contains the ModpackTools Nushell adapter and its PowerShell bridge. The adapter keeps the PowerShell module as the canonical engine, always requests structured JSON output, forwards normal R3CLI presentation to the terminal, and returns parsed Nushell values to the pipeline.

`modpack project use <id>` stores the validated active project in the Nu session as `MODPACKTOOLS_PROJECT`. Child PowerShell bridge processes inherit that variable when the module is imported, so the adapter does not rewrite later commands or inject `--project` arguments.

Install from Nushell with the repository-root `install-modpack-tools.nu` script. See [`docs/nushell.md`](../docs/nushell.md) for the full contract.
