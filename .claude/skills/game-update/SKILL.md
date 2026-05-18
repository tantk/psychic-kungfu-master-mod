---
name: game-update
description: Handle a JinGu game update end-to-end. Detects Assembly-CSharp.dll changes, archives old decompilation, restores corlib patches that Steam may have reverted, re-decompiles, diffs Harmony patch targets and shared constants (EffectId, item IDs, scene buildIndex), and reports plugin impact. Use when the user says "game updated", "new version", "Steam updated the game", "check for breakage", or after any Steam update on this game.
---

# JinGu Game Update Pipeline

Adapted from `C:/dev/longyin-cheats/.claude/skills/game-update/skill.md` for our **Mono + MelonLoader** stack. Most of longyin's IL2CPP-specific phases (Ghidra, RVA diff, name resolution) don't apply here because Mono + ILSpy gives us readable source directly.

**Total time: ~3 minutes**

## Paths

| Item | Path |
|---|---|
| Game install | `C:\Program Files (x86)\Steam\steamapps\common\JinGu\JinGu` |
| Game code | `JinGu_Data\Managed\Assembly-CSharp.dll` |
| Stripped corlibs | `JinGu_Data\Managed\{mscorlib,System,System.Core,System.Runtime}.dll` |
| MelonLoader's patched corlibs | `MelonLoader\Dependencies\MonoBleedingEdgePatches\*.dll` |
| Plugin DLL | `Mods\JinGuCheats.dll` |
| Decomp workspace | `C:\dev\physickungfu_cheat\decomp` |
| Decomp archive | `C:\dev\physickungfu_cheat\decomp_versions\<date>` |
| Plugin source | `C:\dev\physickungfu_cheat\plugin\` |

## Phase 1 — Detect Whether Anything Actually Changed

```bash
sha256sum "C:/Program Files (x86)/Steam/steamapps/common/JinGu/JinGu/JinGu_Data/Managed/Assembly-CSharp.dll"
```
Compare against the previously-stored hash. If unchanged → report "no game-code changes" and **stop after Phase 2** (still re-check corlibs since Steam sometimes reverts those even when game code doesn't change).

Also check the game exe and Unity player — these change much less often but worth a hash on:
- `JinGu.exe`
- `UnityPlayer.dll`

If those change, the **Unity version may have changed**. You'll need new unstripped corlibs from BepInEx for the new Unity version (`https://unity.bepinex.dev/corlibs/<unity_version>.zip`).

## Phase 2 — Restore Corlib Patches (Steam reverts these regularly)

Steam updates frequently revert the Managed-folder corlibs back to their stripped versions, killing MelonLoader's bootstrap.

### 2a — Long-term solution: Pre-launch validator (Steam Launch Options)

We have a permanent fix in place: a script that runs before the game starts on every launch and auto-restores any reverted corlibs. **Verify it's still wired up** — open the user's Steam library, right-click JinGu → Properties → General → Launch Options, and confirm it contains:

```
"C:\dev\physickungfu_cheat\tools\pre-launch.bat" %command%
```

If yes: corlibs get auto-fixed on every game launch. Just inspect the log to confirm a restore happened after an update:

```powershell
Get-Content "$env:LOCALAPPDATA\JinGuCheats\pre-launch.log" -Tail 20
```

You'll see lines like `mscorlib.dll reverted by update (2650112 → 4631552 bytes), restoring patch` whenever Steam reverted something — followed by the auto-restore. No manual action needed.

If the launch option is missing or someone cleared it: tell the user how to re-add it (paste the line into Launch Options). The pre-launch.bat + pre-launch.ps1 scripts live in `tools/`.

### 2b — Manual fallback (if pre-launch isn't wired up)

Run the same logic by hand:

```powershell
powershell -ExecutionPolicy Bypass -File C:\dev\physickungfu_cheat\tools\pre-launch.ps1
```

That single command restores any reverted corlib. Inspect the same log file to see what it did.

### 2c — Edge case: Unity version changed (rare)

If `JinGu.exe` or `UnityPlayer.dll` was also updated, the Unity version may have bumped. In that case MelonLoader's bundled patches may no longer match the new runtime. Pull fresh unstripped corlibs from `https://unity.bepinex.dev/corlibs/<unity_version>.zip` and place them in `MelonLoader/Dependencies/MonoBleedingEdgePatches/` (replacing the old ones). The pre-launch script will pick them up automatically.

## Phase 3 — Archive Old Decomp

```powershell
$date = (Get-Date).ToString("yyyy-MM-dd")
$archive = "C:\dev\physickungfu_cheat\decomp_versions\$date"
if (Test-Path $archive) { $i = 2; while (Test-Path "${archive}_${i}") { $i++ } ; $archive = "${archive}_${i}" }
New-Item -ItemType Directory -Path $archive | Out-Null
Move-Item "C:\dev\physickungfu_cheat\decomp" $archive
```

## Phase 4 — Re-decompile Assembly-CSharp

Use ILSpy with `--project --nested-directories` so we get per-class files like the previous archive:

```bash
mkdir -p /c/dev/physickungfu_cheat/decomp
/c/Users/tanti/.dotnet/tools/ilspycmd.exe \
  -o /c/dev/physickungfu_cheat/decomp \
  --project --nested-directories \
  "C:/Program Files (x86)/Steam/steamapps/common/JinGu/JinGu/JinGu_Data/Managed/Assembly-CSharp.dll"
```

## Phase 5 — Diff Harmony Patch Targets

The plugin patches these methods. Their signatures MUST stay identical or the patch won't bind (Harmony fails silently, cheats become no-ops).

```bash
OLD=/c/dev/physickungfu_cheat/decomp_versions/<old-date>
NEW=/c/dev/physickungfu_cheat/decomp
for tgt in \
  "Role.cs:public void Injured" \
  "SaveData.cs:public void CostAction" \
  "SaveData.cs:public int GetItemNum" \
  "SaveManager.cs:private void AddTime"; do
  f="${tgt%:*}"; sig="${tgt#*:}"
  diff <(grep -A 0 "$sig" "$OLD/$f") <(grep -A 0 "$sig" "$NEW/$f") && echo "  ✓ $f::$sig"
done
```

If any diff fires, look at the new signature and update the `[HarmonyPatch(...)]` attribute in `plugin/Plugin.cs` accordingly.

## Phase 6 — Diff Shared Constants

The plugin and UI depend on specific IDs that the game devs occasionally reassign:

| Constant | Where defined | Used by |
|---|---|---|
| Money item ID = **10001** | `NpcInfo.m_money` references in trade/buy code | `Cheats.GiveMoney`, plugin `Patches.GetItemNum_Postfix` |
| `EffectId` enum values 1-803 | `DBLoad/EffectId.cs` | UI button presets, `Cheats.AddEffect` |
| Scene buildIndex `1 = SceneEnum.登录` (login) | `SceneEnum.cs` | `Cheats.IsInGame`, auto-load logic |

```bash
diff -u "$OLD/DBLoad/EffectId.cs" "$NEW/DBLoad/EffectId.cs"
diff -u "$OLD/SceneEnum.cs"        "$NEW/SceneEnum.cs"
# Money ID
grep -n "10001" "$NEW/NpcInfo.cs" "$NEW/SaveData.cs" | head -10
```

If `EffectId` values shift (e.g. 内功经验 was 717 but is now 718), the UI button `data-args='{"id":717,...}'` becomes wrong — update `ui/src/index.html` accordingly.

If `SceneEnum.登录` index changes from 1 to something else, update `Cheats.IsInGame()` and the auto-load `idx == 1` check.

## Phase 6b — Re-extract Game Data Tables (Items / Equip / Gift / WuXue / …)

The UI's Items tab is powered by 13 JSON files bundled in `ui/src/data/`, extracted from the decompiled `DBLoad/*.cs` source by `tools/extract-game-data.py`. **Any game update that touches item, equipment, gift, or related tables means these JSONs are stale** — the UI will display the old item list, possibly missing new items, and may try to give items whose IDs no longer exist.

The 13 inventory-valid tables to re-check on every update:

```
item.json       equip.json      gift.json       wuxue.json
itemuse.json    information.json prop.json      animalfood.json
fish.json       fishrod.json    huntbow.json    danfang.json
dazao.json
```

Plus the 33 other tables in `data/` that we extract but don't bundle in the UI (they may still be useful for analysis even if not shown to the user).

### Steps

1. **Re-extract everything to `data/`** (the canonical source):
   ```bash
   cd /c/dev/physickungfu_cheat
   python tools/extract-game-data.py
   ```
   This regenerates all `data/*.json` from the decompilation. Takes ~10 seconds for most tables; `language.json` runs the slowest at ~30 seconds. PointLv hangs the parser — skip it if hit (low-priority stat threshold data).

2. **Diff against the archived version** to see what changed:
   ```bash
   OLD=/c/dev/physickungfu_cheat/decomp_versions/<old-date>
   # Old data isn't archived alongside decomps, so re-extract from the old decomp:
   for t in item equip gift wuxue itemuse information prop animalfood fish fishrod huntbow danfang dazao; do
     python tools/extract-game-data.py "$t" 2>/dev/null   # writes to data/
     diff <(jq -S . "data/$t.json") <(jq -S . "$ARCHIVED_DATA/$t.json") | head
   done
   ```
   Look for added entries (new items the user might want), removed entries (would cause give-item to silently fail in-game), and renamed `name` fields.

3. **Verify cross-references still hold.** The Items tab relies on equip/gift/wuxue/etc. IDs ALL existing in item.json (the "100% inventory-valid" property). If a game update breaks this, the UI's name-enrichment will show "?" for missing rows:
   ```bash
   python -c "
   import json
   items = {r['id'] for r in json.load(open('data/item.json',encoding='utf-8'))}
   for t in ('equip','gift','wuxue','itemuse','information','prop','animalfood','fish','fishrod','huntbow','danfang','dazao','animalfood'):
       rows = json.load(open(f'data/{t}.json',encoding='utf-8'))
       missing = [r['id'] for r in rows if r['id'] not in items]
       print(f'{t:<14}{len(rows):>5} rows, {len(missing):>3} orphan ids')
   "
   ```
   All should report `0 orphan ids`. If any have orphans, the orphan IDs are dead pointers — flag them, the UI won't crash but will show "?" names.

4. **If anything in the 13 bundled tables changed**, sync into the UI:
   ```powershell
   $keep = @("item","equip","gift","animalfood","danfang","dazao","fish","fishrod","huntbow","information","itemuse","prop","wuxue")
   foreach ($t in $keep) {
     Copy-Item "C:\dev\physickungfu_cheat\data\$t.json" "C:\dev\physickungfu_cheat\ui\src\data\$t.json" -Force
   }
   # Then rebuild the UI to embed the new JSON
   cd C:\dev\physickungfu_cheat\ui ; cargo tauri build --no-bundle
   ```

5. **If the 12-table inventory-valid list changes** (new table joins, or an existing one stops being 100% valid), update:
   - The `GIVE_TABLES` set in `ui/src/app.js`
   - The `DATA_TABLES` dropdown grouping in `ui/src/app.js`
   - The `keep` list in `ui/src/data/` (and trim/add files accordingly)

### When this matters most

- Game adds new items: users won't see them in the UI picker until you re-extract + rebuild
- Game removes items: clicking the Add button will silently no-op (the game-side `ChangeItem` rejects unknown IDs)
- Game restructures equipment system: equip.json may stop cross-referencing item.json cleanly → enrichment breaks

## Phase 7 — Rebuild

```bash
cd /c/dev/physickungfu_cheat/plugin && dotnet build -c Release
```

The build copies the new DLL to `Mods/` automatically.

If patches changed substantively, also rebuild the UI:
```bash
cd /c/dev/physickungfu_cheat/ui && cargo tauri build --no-bundle
```

## Phase 8 — Sanity Check

Launch the game from Steam, load a save, check `MelonLoader/Latest.log`:

- ✅ `STEP 1: OnInitializeMelon entered`
- ✅ `STEP 5: applying Harmony patches...` (no failures)
- ✅ `STEP 6 OK: pipe listening`
- ✅ Plugin reaches `=== READY ===`
- ✅ Test one cheat from the UI (e.g. give money) — check in-game inventory

If a Harmony patch failed silently, you'll see no error but the cheat will be a no-op. Hook a `[HarmonyDebug]` attribute to the patched class to log binding attempts.

## Common Failure Modes

| Symptom | Likely cause | Fix |
|---|---|---|
| MelonLoader log shows `TypeLoadException` during bootstrap | Steam reverted a corlib | Phase 2 |
| Plugin reaches READY but cheats are no-ops | Method signature changed | Phase 5 + edit `Patches` class |
| UI button "+10000 money" does nothing | Money item ID changed | Phase 6 + update `MONEY_ITEM_ID` |
| Wrong stat boosted by "+ 根骨" button | EffectId enum reassigned | Phase 6 + update UI `data-args` |
| Auto-load fires on splash screen instead of login | `SceneEnum.登录` index changed | Phase 6 + update `idx == 1` check |
| `Application.runInBackground setter not accessible` warning gone | Unity API surface changed in build | Re-check the reflection lookup; may need different approach |
| UI's Items tab shows "?" for some equipment/gift names | equip/gift row has an id not in item.json (cross-ref broken) | Phase 6b verify cross-references; report orphan ids |
| New game items not showing in UI's Items tab | `ui/src/data/*.json` is stale | Phase 6b re-extract + rebuild UI |
| "Add" button reports success but item never appears in inventory | Item ID was removed from `Item.Dic` in the new build | Phase 6b — find which id is dead, remove from UI or accept the loss |
