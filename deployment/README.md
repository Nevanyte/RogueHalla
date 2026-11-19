# deployment

The legacy TypeScript game's Cloudflare deployment. It is not the Rust server's hosting (that is `hosting/`).

`cloudflare/` holds the Worker that fronts the legacy game: `wrangler.jsonc` (the Worker, its assets from
`packages/client/dist`, and the game container), `src/` (container lifecycle, lobby and profile snapshots, network probe,
the telemetry store and the dashboard), `test/` (its tests) and `scripts/` (dashboard dev helpers). The deploy workflow
`.github/workflows/deploy.yml` runs `npx wrangler deploy --config deployment/cloudflare/wrangler.jsonc`, and the root
`.gitignore` names `deployment/cloudflare/build-meta.json`, so do not move this folder.

Where to start: `cloudflare/src/index.ts`. Deploy through the workflow; nothing here is part of the Rust build. The legacy
game and its hosting are described in [docs/legacy/LEGACY_GAME_README.md](../docs/legacy/LEGACY_GAME_README.md).
