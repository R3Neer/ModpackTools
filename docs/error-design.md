# Error message design

ModpackTools treats predictable user, project, configuration, content, and external-tool failures as structured CLI errors. Unexpected programming failures remain ordinary PowerShell exceptions so their diagnostic context is not hidden.

## Message contract

Every expected error follows this order:

```text
What failed and, when useful, which value caused it.
Details: Technical or upstream context.
Try: One concrete recovery action
```

- Start with the failed subject or value; do not start with `Error`, `Invalid syntax`, or `Failed to`.
- Use sentence case and end statements with punctuation.
- Quote user-provided values, paths, IDs, selectors, settings, and options with single quotes.
- State allowed values in the cause when the user must choose from a closed set.
- Put commands and recovery instructions only on the `Try:` line so they are easy to find and copy.
- Mention rollback or preservation before upstream details when an operation was transactional.
- Do not expose private script paths for expected errors. The public `modpack` command emits a stable `ModpackTools.<area>.<condition>` error ID.
- Do not catch or reformat unexpected exceptions unless enough context can be added without losing the original reason.

## Error inventory

The audited errors belong to these namespaces:

| Namespace | Covered conditions |
| --- | --- |
| `Command` | Unknown commands or wrong positional argument counts |
| `Option` | Unknown options, missing values, forbidden combinations, invalid values, or omitted `--` prefixes |
| `Configuration` | Missing or invalid global configuration and unavailable roots |
| `Project` | Missing, malformed, duplicate, unsupported, incomplete, or unresolved projects |
| `Inventory` | Invalid filters, categories, filter combinations, content types, and numbered reference caches |
| `Metadata` | Invalid metadata tables, unknown categories, missing or ambiguous mods |
| `ResourcePack` | Missing or ambiguous packs, invalid priority, and Default Options failures |
| `Search` | Modrinth request failures and missing, invalid, stale, incompatible, or out-of-range cached results |
| `Content` | Add/update selection failures, unsupported local content, normalization failures, and transactional rollback |
| `Build` | Missing Packwiz inputs or outputs, process startup/exit failures, and unsupported metadata paths |
| `Diff` | Missing or malformed builds and failed temporary comparison exports |
| `Theme` | Missing, incomplete, or malformed theme configuration |

The `ErrorId` passed at each `Throw-MpError` call is the source of truth for individual conditions. Tests enforce the shared format and prevent new ad-hoc `throw` statements in CLI implementation files.
