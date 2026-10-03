---
name: deploy
description: MediaButler has no web deploy -- its GitHub README (https://github.com/mindattic/MediaButler) is the project page. The mindattic.com/mediabutler.htm catalog landing page was retired (MindAttic.Deploy DEP-A6); do not run MindAttic.Deploy.
---

# /deploy -- no web deploy

**MediaButler has no web deploy.** Its README on GitHub -- https://github.com/mindattic/MediaButler -- is the project page. To update the project page, edit `README.md` and push to `main`.

The README-driven landing page `mindattic.com/mediabutler.htm` was retired together with MindAttic.Deploy's catalog mode (amendment DEP-A6 in `MindAttic.Deploy/docs/AMENDMENTS.md`, 2026-10-03). `npm run deploy -- --only mediabutler` is now rejected, so do not run MindAttic.Deploy for this project.

When invoked, tell the user the above and stop.
