# Agent guidance — StS2-Launcher_Mod_Manager

## Task scope and engineering scale

Android/Godot launcher and Steam mod/save manager.

This is a local fork. Preserve upstream architecture, licenses, product contracts
and required checks. A local task does not authorize upstream sync, broad
refactoring, installation, a pull request, publication or deployment.

- **Scope:** Answer the named questions or finish the requested change, then stop.
  Reviews stay read-only; unrelated test backfill, audits and cleanup are not
  implied. Use the existing structure.
- **Testing:** Use the relevant existing test/build for the changed module; inspect affected install/save flow on fixtures.
  Prose-only edits need content/link checks. Broaden validation only for a changed
  contract, relevant failure or an existing required gate; reuse valid evidence.
- **Security:** Changes to Steam credentials, mod downloads/execution or save replacement need targeted checks and preserve recovery of existing saves.
  Security work follows a real changed boundary, not a blanket checklist.
- **Robustness:** Keep failures visible and distinguish missing data from valid
  results. Use existing bounded recovery at external I/O; do not add speculative
  fallback layers, services or fabricated successes. Preserve user-owned state.
