# P-WEB-OPT-1: a smaller, faster-starting Unity Web player (release Player settings and Brotli sidecars)

- **Serves:** owner request 2026-11-14 ("a complete optimization sweep of the unity front end", on the live branch). Not
  in ORDER_OF_OPERATIONS: owner approval needed to land. Not a game rule.
- **Base:** `2f33045d6` (`coord/int-47`), the commit `play.rougehalla.com` runs. Its `client-unity/UnityProject` equals
  `feature/unity-rework` @ `b71590c8f` apart from three test files, so the change applies to either.
- **D-rules touched:** none (Player settings and build tooling; no wire, sim or rule change).

## 1. ID and outcome
Measured on this machine (Unity 6000.0.85f2, Runtime Speed, WebAssembly 2023, same commit and content hash
`7a542a13187f034c`), sizes in bytes:

| file | baseline raw | final raw | live host today (Caddy zstd, measured from play.rougehalla.com) | final, Brotli sidecar (q11) |
|---|---|---|---|---|
| Web.wasm | 57,988,360 | 50,484,532 | 17,004,474 | 9,233,056 |
| Web.data | 44,962,152 | 41,992,079 | 34,456,755 | 28,375,593 |
| Web.framework.js | 360,932 | 360,786 | 94,126 | 75,393 |
| **first load** | **103.3 MB** | **92.8 MB (-10%)** | **51.6 MB** | **37.7 MB (-27% against live)** |

Without the hosting change below, the live host's on-the-fly zstd of the final player is about 5% smaller than today's
(zstd -3 here: wasm 15.71 -> 13.82 MB, data 31.62 -> 30.88 MB).

Player settings changed (WebGL target, `ProjectSettings/ProjectSettings.asset`):

| setting | old | new | why |
|---|---|---|---|
| Managed Stripping Level | Minimal | **High** | wasm -7.5 MB raw (-1.2 MB brotli). Reflection in the client: Newtonsoft is DOM-only (`AccountFullJson`) and ships its own `link.xml`; `SpriteOutline`/`PerfRecorder` reflect on serialized URP fields (kept) and are measurement-only. |
| Splash Screen (Show) | on | **off** | the "Made with Unity" logo texture is 2.7 MB of Web.data and a few seconds of black screen before the hub. Unity 6 allows this on a Personal licence (the setting saved). |
| Initial Memory Size | 32 MB | **256 MB** | the heap reached 286 MB at match join; from 32 MB with the 0.2 geometric step that is about a dozen grow-and-copy steps during load. Now one (256 -> 307 MB in a match). |
| IL2CPP compiler configuration | Release (default) | Release (explicit) | Master was tried: byte-identical code on Web, so not kept. |

Kept on purpose (measured earlier in `docs/rework/PERF_REPORT.md` or already right): Compression Format Disabled (the host
compresses; local proof tools need plain files), Runtime Speed and IL2CPP Optimize Speed (main-thread time), WebAssembly
2023, Explicitly Thrown exceptions (the client catches), Strip Engine Code on, .NET Standard, Data Caching on, Debug Symbols
off, Maximum 2048 MB, Geometric growth; `Bootstrap.cs` already sets `vSyncCount=0` / `targetFrameRate=-1` and the template
caps `devicePixelRatio`. Graphics stripping is already Automatic/Strip Unused; "strip unused post-processing variants"
stays off because `ArenaView` builds its Volume profile at run time. Strip Unused Mesh Components: no mesh data in the build.

New tool: `Tools/precompress-web.cjs` writes `<file>.br` (quality 11, round-trip checked) next to Web.wasm, Web.data and
the JS. The player is unchanged, so plain servers and every proof tool keep working; `Tools/build-size.cjs` skips a sidecar
whose original is present.

## 2. Hosting change this needs (owner approval; `hosting/` not edited)
`hosting/technical-demo/bootstrap.py` `caddy_config()`, inside `handle @read`: `file_server` -> `file_server { precompressed br }`,
and run `node client-unity/UnityProject/Tools/precompress-web.cjs` on `Build/Web/Build` before bundling (about 7 minutes).
Caddy then serves `Web.wasm.br` with `Content-Encoding: br` to browsers that accept it (the existing `encode` block skips a
response that already has an encoding) and stops compressing ~100 MB per first download on the t3.small. Not tried
against a real Caddy here (none installed); the browser side was proven with a Node server that does the same.

## 3. Acceptance evidence (this machine, 2026-11-14)
- Settings read back from disk (`git diff ProjectSettings/ProjectSettings.asset`): `managedStrippingLevel: WebGL: 3`,
  `m_ShowUnitySplashScreen: 0`, `webGLInitialMemorySize: 256`, `il2cppCompilerConfiguration: WebGL: 1`.
- Final build served with the Brotli sidecars and `Content-Encoding: br` to the in-app Chromium, against a local
  `rh-server` built from `2f33045d6` (`--fill-bots 4 --min-players 1`): hub loads with no splash, heap 256 MB at start;
  `auto=1` join to `InMatch`, Fire HUD, a bot fight on screen, heap 307 MB; console: no exception; only errors are
  `/api/lobby` 404 (static server is not the game server) and the CoreCopy/HDRDebugView "shader not supported" lines, which
  the baseline prints identically. Wire sizes in the browser: wasm 9,233,356, data 28,375,893.
- `node --test Tools/build-size.test.cjs`: 4 pass.

## 4. Not established
Runtime frame time (no perf-matrix run); Safari and Firefox; a full match to its result screen on the final build; the
account/practice flows (Newtonsoft path) under High stripping; Caddy's `precompressed` behaviour on the host.

## 5. Follow-ups worth a packet (not done here)
- Textures are 86% of the Build Report (67 MB uncompressed): the character atlases (`Assets/Art/Derived/ShowcaseLive/Atlases`,
  up to 2048x4116 DXT5) dominate. Crunch or a lower max size would cut Web.data most, but it is an art decision
  (`zone-art-contrast` direction), to judge on real renders.
- `dungeon_theme.ogg` (4.0 MB) ships in Web.data through the catalog while the region music already streams from
  StreamingAssets; streaming it too takes it out of the first load.
- URP film-grain textures (10 x 256 KB) and the render-pipeline debug prefabs ride in through URP's default resources.
