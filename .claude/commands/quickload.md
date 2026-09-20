---
description: Restore the last /quicksave transcript and resume exactly where it left off.
allowed-tools: Read, PowerShell, Bash
---

# Quickload — restore the paper transcript

Read **`.claude\quicksave.md` in the current project root** (`<cwd>\.claude\quicksave.md`).

- If it does not exist: tell the user there is no quicksave to restore, and stop.
- If it exists: treat its contents as your authoritative working memory for this session.
  1. **Archive it — never delete it.** Find the highest existing `.claude\quicksave.md.NNN`
     (zero-padded `.001`, `.002`, ...), shift every existing archive up by one (highest first, so
     nothing collides), then rename the just-read `quicksave.md` to `quicksave.md.001`. The path
     is now clear for a future `/quicksave`, but this save is preserved forever under its new
     numbered name — it is consumed for resume purposes only, never lost.
  2. Briefly confirm to the user what you're resuming (one line).
  3. Pick up the **Current task**, honor every **Decision locked**, and continue from
     **Next concrete steps** without re-asking anything already settled.
