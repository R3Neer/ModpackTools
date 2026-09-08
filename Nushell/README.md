# Nushell adapter

This directory contains the ModpackTools Nushell adapter and its PowerShell bridge. The adapter keeps the PowerShell module as the canonical engine, always requests structured JSON output, forwards the normal R3CLI presentation to the terminal, and returns parsed Nushell values to the pipeline.
