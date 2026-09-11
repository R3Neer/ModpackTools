# Human-facing message style

ModpackTools output must tell the user **what is happening, what would happen, or what already happened** without making those states look interchangeable.

This policy applies to first-party human-facing output from commands, transactions, doctor checks, installers, self-update, help, and next-step guidance. Structured machine output has its own contract and keeps stable field/action values.

## 1. Choose the semantic state first

Every status line should fit one of these states before wording is chosen.

| State | Purpose | Preferred wording | Examples |
| --- | --- | --- | --- |
| In progress | Work is currently running | Progressive verb | `Searching Modrinth…`, `Building Vanilla Plus…` |
| Planned / preview | Work has not been applied | Explicit conditional/future wording | `Would change pack.toml`, `3 file changes planned; nothing has been changed.` |
| Completed | Work finished successfully | Past/completed wording | `Changed pack.toml`, `Installed ModpackTools 1.4.0.` |
| State / diagnosis | Report a fact, not an action | Declarative wording | `Pinned`, `Verification is incomplete.`, `No differences from the latest build.` |
| User instruction | Tell the user what to do next | Imperative, clearly framed as guidance | `Run modpack doctor.`, `Use modpack add <number>.` |

## 2. Never use an imperative-looking action for completed work

Bare action verbs such as `Add`, `Change`, `Remove`, `Update`, `Create`, or `Install` read as instructions to the user. They must not describe work the tool has already applied.

Bad:

```text
Change pack.toml
Add mods/example.pw.toml
✓ 2 file change(s) applied.
```

Good:

```text
Changed pack.toml
Added mods/example.pw.toml
✓ 2 file change(s) applied.
```

For previews, make the hypothetical state equally explicit:

```text
Would change pack.toml
Would add mods/example.pw.toml
• 2 file change(s) planned; nothing has been changed.
```

## 3. Transaction output must agree with itself

File-level entries and the transaction footer must describe the same state.

- Applied transaction: `Added`, `Changed`, `Removed` + `… applied.`
- Preview or dry run: `Would add`, `Would change`, `Would remove` + wording that explicitly says nothing was changed.
- No-op: report that no changes were needed. Do not claim that zero changes were applied.
- Failed/rolled-back transaction: report preservation or rollback as a fact before upstream details. Never print completed-action wording for changes that did not survive.

## 4. Plans should describe state transitions

A plan is not a list of commands for the user. Prefer transitions or explicit planned wording.

Prefer:

```text
Instrumental: not installed -> ZJR3Ob1z (requested)
Library X: abc123 -> removed (unused dependency)
```

Avoid:

```text
Remove Library X
Library X: remove
```

## 5. Imperatives are reserved for actual guidance

Imperatives are appropriate when the user really must take an action, but the line should make that role obvious.

Good:

```text
Run modpack doctor after opening a new PowerShell session.
Use modpack add <number> to install a search result.
Select a project before using the global search number.
```

Do not mix a user instruction into a change list where it could be mistaken for an operation performed by ModpackTools.

## 6. Diagnostics report facts

Warnings and health checks should say what is true and, when useful, what the consequence is.

Good:

```text
127 requirement(s) could not be verified from the available metadata.
Verification is incomplete; Fabric Loader may still reject this pack during startup.
```

A remediation hint may follow as a separate instruction.

## 7. Machine output remains semantic, not decorative

JSON fields are not prose. Stable values such as `action: "add"`, `action: "change"`, or `action: "remove"` describe machine semantics and do not need grammatical tense.

Do not change structured values merely to mimic human wording. Human renderers are responsible for presenting the correct temporal state.

## 8. Review checklist

When adding or changing output, check:

1. Is this line progress, a plan, a completed action, a state, or an instruction?
2. Could a user mistake it for something they still need to do?
3. If it belongs to a transaction, does its tense agree with the final transaction summary?
4. Does a dry run clearly say that nothing changed?
5. Is an imperative used only for an actual next step?
6. Did the machine-readable contract remain stable unless a schema change was intentional?
