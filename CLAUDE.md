# JinGu Cheats — dev guide

User-facing docs live in `README.md`. This file is the layout + workflow reference
for the dev environment.

## Project layout

```
physickungfu_cheat/
├── README.md                    ← user-facing install + usage
├── CLAUDE.md                    ← this file
├── plugin/                      ← MelonLoader plugin (C# / .NET 4.7.2)
│   ├── JinGuCheats.csproj
│   ├── Plugin.cs                ← MelonMod entry + Harmony patches
│   ├── UpdateDriver.cs          ← MonoBehaviour that runs every frame
│   ├── PipeServer.cs            ← named pipe \\.\pipe\JinGuCheats.v1
│   ├── Dispatcher.cs            ← protocol handler (cmd: state/toggle/give_money/...)
│   ├── Cheats.cs                ← per-cheat methods + state snapshot + cache
│   ├── MainThreadDispatcher.cs  ← marshals work onto Unity main thread
│   ├── ErrorLog.cs              ← ring buffer of last 50 plugin errors
│   ├── UiLauncher.cs            ← spawns the Tauri UI process at plugin init
│   └── Json.cs                  ← tiny in-house JSON writer/reader
├── ui/                          ← Tauri UI (Rust + HTML/CSS/JS)
│   ├── src/                     ← frontend (HTML/CSS/JS)
│   ├── src-tauri/               ← Rust shell + pipe client
│   └── target/release/jingu-cheats-ui.exe
├── decomp/                      ← latest ILSpy decomp of Assembly-CSharp.dll
├── decomp_versions/<date>/      ← archived old decomps from prior game versions
├── tools/                       ← dev + optional-user scripts
│   ├── pre-launch.bat           ← Steam Launch Option wrapper (auto-heal corlibs)
│   ├── pre-launch.ps1           ← actual corlib validator/restorer
│   ├── patch-run-in-background.py ← (reference only — too heavy for this game's data.unity3d)
│   └── test-mod.ps1             ← dev smoke test (launch → bootstrap → pipe → state)
├── downloads/                   ← cached installer zips (BepInEx, MelonLoader, corlibs)
├── decrypt_save.py              ← standalone save-file decryptor (AES-CBC, "MyPassword")
└── .claude/skills/              ← project-specific skills
    └── game-update/SKILL.md     ← end-to-end flow when the game gets a Steam update
```

## Game facts

- **Game**: 今古群侠传 (JinGu), Steam appid `3313720`, by 金十四工作室
- **Engine**: Unity 2021.3.15f1c1, **Mono** (not IL2CPP), x64
- **Install path**: `C:\Program Files (x86)\Steam\steamapps\common\JinGu\JinGu`
- **Process name**: `JinGu` (we use this for `MelonGame` matching and process discovery)
- **Save path**: `C:\Users\<user>\AppData\LocalLow\金十四工作室\JinGu\SaveDatas\*.bytes`
- **Save format**: AES-256-CBC, password `"MyPassword"`, 8-byte fixed salt, 1000 PBKDF2 iterations, JSON payload via Unity JsonUtility — see `decrypt_save.py`

## Critical compat traps to remember

- **Stripped corlibs**: Unity build stripped `mscorlib.dll`, `System.dll`, `System.Core.dll`, `System.Runtime.dll` in `JinGu_Data/Managed/`. MelonLoader's bootstrap reflection (`DllImportAttribute.CharSet`, etc.) fails against the stripped versions. Fix: copy MelonLoader's bundled patches over them (`MelonLoader/Dependencies/MonoBleedingEdgePatches/*.dll`). Steam game updates sometimes revert one or more of these — `pre-launch.bat` is the auto-fix.
- **`Application.runInBackground` setter missing in the runtime UnityEngine.CoreModule** — read-only at runtime. We Harmony-patch `get_runInBackground` to always return `true` so anything that reads it sees true. Unclear if Unity's native focus-pause logic re-reads the C# property each frame.
- **`Input.GetKeyDown` is stripped** — game uses new Input System. We poll keys via Win32 `GetAsyncKeyState` in `Hotkey.cs`.
- **`MelonGame("金十四工作室", "JinGu")`** — the developer name has Chinese characters. If the assembly attribute mismatches, MelonLoader prints `incompatible` and never loads the plugin.
- **`SaveManager.SaveData` getter auto-creates a placeholder save** (`leaderFamily="大"`, `leaderName="虾米"`) if accessed before a real save loads. Always gate on `SceneManager.GetActiveScene().buildIndex > 1` before touching it (1 = login scene).
- **MelonLoader's `OnUpdate` doesn't fire** in this build for our `MelonMod` — workaround in `UpdateDriver.cs`: install a Unity `MonoBehaviour` from `OnSceneWasInitialized` and let Unity tick it directly.

## Build + deploy

```bash
cd plugin && dotnet build -c Release     # also auto-copies the UI exe if it's been built
cd ui && cargo tauri build --no-bundle    # rebuild UI (slower; only when frontend changes)
```

`plugin/JinGuCheats.csproj` has a post-build target that copies both `JinGuCheats.dll` and `jingu-cheats-ui.exe` into the game's `Mods\` folder.

## Test loop

For development, the test runner exercises the full bootstrap + pipe path:

```bash
pwsh tools/test-mod.ps1                  # launch → wait for READY → ping pipe → tear down
pwsh tools/test-mod.ps1 -AutoLoad        # also auto-load the most recent save (marker file)
pwsh tools/test-mod.ps1 -NoCleanup       # keep game running for hands-on inspection
```

Pass criteria: plugin reaches `=== READY ===` in `MelonLoader/Latest.log` within 30s, pipe handshake succeeds, optionally state.in_game becomes true after AutoLoad triggers.

## When the game updates

Use the `game-update` skill: `.claude/skills/game-update/SKILL.md`. Summary:

1. Hash-compare new vs old `Assembly-CSharp.dll`. If same → only check corlibs (Steam may still have reverted them).
2. Pre-launch.bat already handles corlib reverts automatically; otherwise run `tools/pre-launch.ps1` once.
3. If `Assembly-CSharp.dll` changed:
   - Archive current `decomp/` to `decomp_versions/<date>/`
   - Re-decompile with ILSpy → `decomp/`
   - Diff Harmony patch targets: `Role.Injured`, `SaveData.CostAction`, `SaveData.GetItemNum`, `SaveManager.AddTime`, `SaveManager.Recent`, `SaveData.AddItems`
   - Diff `DBLoad/EffectId.cs` (UI button IDs depend on these values)
   - Diff `SceneEnum.cs` (we check buildIndex 1 = login)
   - **Re-extract game-data tables**: `python tools/extract-game-data.py`. The UI's Items tab bundles 13 of these JSONs (`ui/src/data/`); re-copy them and rebuild the UI if any of those 13 changed. Game-update skill phase 6b has the full procedure.
   - Rebuild plugin

## Distribution (out of scope for daily dev, but for the record)

The project is set up for **Path 1** distribution per the design decision on 2026-05-17:

- Ship a zip with: `JinGuCheats.dll`, `jingu-cheats-ui.exe`, `pre-launch.bat`, `pre-launch.ps1`, `README.md`
- User installs MelonLoader manually first (link in README)
- User runs the corlib-copy snippet from README once
- User drops our files into `Mods\` and optionally adds the launch option
- ~95% of users will never hit a corlib revert; the rest follow the optional README section

We deliberately do NOT ship an installer that programmatically edits Steam config files — too invasive, too fragile across Steam versions.
