# /deploy -- no web deploy

**MediaButler has no web deploy.** Its README on GitHub -- https://github.com/mindattic/MediaButler -- is the project page. To update the project page, edit `README.md` and push to `main`.

MindAttic.Deploy does not publish MediaButler (`npm run deploy -- --only mediabutler` is rejected), and the repo-root `index.htm` is a static snapshot that nothing publishes. Do not run MindAttic.Deploy for this project.

When invoked, tell the user the above and stop.
