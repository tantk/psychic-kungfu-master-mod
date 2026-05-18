# JinGu Cheats / 今古群侠传 修改器

A MelonLoader-based cheat mod for **今古群侠传 (JinGu)** by 金十四工作室, with an external Tauri-based UI in a separate window — wuxia-themed, with five user-switchable color themes.

> **Compatibility:** tested against JinGu Steam build of 2026-05-18 with MelonLoader 0.7.3.

## Features

### 主公 / Self
- 无限银两 / Max Money toggle
- 无敌 / God Mode toggle (friendly Roles take 0 damage in combat)
- + 银两 buttons (10k / 100k / 1M / 10M) — leader card shows the live amount
- + N for the six pillars (根骨 / 力道 / 体魄 / 敏捷 / 技巧 / 悟性), each click adds 1, with live values displayed
- 直接经验 / Direct XP sliders for 实战 (combat) and 内功 (internal qi) — adds raw XP to the pool that drives the skill level
- 经验加成 / XP Rate Boost toggles for 实战经验 / 武学经验 / 内功经验 — set the rate via slider, flip the toggle on to apply
- + N for the six weapon masteries (刀法 / 剑法 / 拳掌 / 枪棍 / 暗器 / 琴功)
- 战斗 · 全员回满 / heal full team
- 武学 · 满级所有武学 / one-click max all learned martial arts
- 声望 / Reputation sliders for 善恶 (good ↔ evil), 名望 (fame), 情缘 (romance) — ± by chosen amount

### 探索 / Explore
- 无消耗 / Free Actions toggle (no 精力 / 体力 cost for crafting, alchemy, etc.)
- 时停 / Freeze Time toggle (world clock paused)
- 回满 精力 + 体力 / recover both stamina pools (current / max shown live)
- + 精力上限 / + 体力上限 — raise the caps permanently
- + N 时辰 / advance the world clock by 1 / 4 / 12 hours

### 活动 / Mini-games
- 钓鱼 / Fishing — random ×1 / ×10 / ×50 (weighted by the game's own loot table)
- 打猎 / Hunting — same, weighted by `m_itemValue`
- 驯兽 / Animal Taming — one-click max all bonds, or max one specific animal by npcId
- 生活技 直接经验 / Living Arts Direct XP sliders for 钓鱼 / 打猎 / 驯兽 / 炼丹 / 打造 / 挖掘 / 下棋 / 采集
- 生活技 经验加成 / Living Arts XP Rate Boost toggles for all 8 living arts

### 物品 / Items
- Full master item browser (948 items) with **two-level category filter mirroring the in-game inventory**:
  - **主类** (main): 全部 · 武器 · 防具 · 秘籍 · 消耗 · 杂物
  - **子类** (subcategory): 24 game-defined subcategories that narrow with the main filter
- Cross-field text search
- Click any row to expand the full record, add to inventory at any quantity, or — for 丹方 / 图纸 — skip the alchemy / forging mini-game to craft the resulting item directly

### 角色 / NPCs
- NPC editor — pick any NPC by ID, edit money, affection (好感度), friend level (which drives the in-game Character record swap and therefore the NPC's stats + skills), robed / show / deaded flags, camp
- Character editor — edit the six pillars per character, add or remove martial-arts (active) and inner skills (passive), reset to baseline snapshot
- Use the friend-level field to unlock companion progression that's normally gated behind personal quests

### 门派 / Sects
- Per-sect editor — set membership flag, mass-adjust favor across all NPCs in a sect (presets ±10 / ±50 / ±100 / ±999, or any custom amount)

### 关于 / About
- 5 swappable color themes: 墨黑 (Ink) · 宣纸 (Paper) · 青瓷 (Celadon) · 赤壁 (Crimson) · 竹简 (Bamboo)
- 4 font presets · 4 size scales (responsive zoom)
- Per-cheat key bindings — you pick which keys to use, no defaults baked in
- Persistent error log surfaced in the UI

## Requirements

- **OS**: Windows 10 / 11 x64
- **WebView2 Runtime**: required by the Tauri UI. Windows 11 has it pre-installed; on Windows 10 most systems already have it via cumulative updates. If the UI window fails to open, install it from <https://developer.microsoft.com/microsoft-edge/webview2/>.

## ⚠ About antivirus warnings

Windows Defender and most antivirus products **will flag the cheat DLL** because it hooks into another process and uses reflection to modify game state — those are the same techniques real malware uses. The full source for everything you're installing is in this repository for you to inspect.

If you'd rather not allow AV exceptions for unsigned binaries, don't install. If you do trust this build, the simplest fix is:

- **Allow the file** when AV prompts, or
- **Add an exclusion** for the game folder (`C:\Program Files (x86)\Steam\steamapps\common\JinGu\JinGu\`) in your AV settings.

## Install — easy mode (recommended)

The release zip is self-contained — **MelonLoader 0.7.3 is bundled inside it**, you don't need to download anything else.

1. Download the release zip from the Releases page and extract it anywhere.
2. **Right-click `install.bat` → Run as administrator** (it self-elevates if you forget). It:
   - Finds your JinGu folder automatically (or asks if it can't)
   - Extracts the bundled MelonLoader into the game folder
   - Patches the four corlibs MelonLoader needs (game ships stripped versions that break the loader)
   - Drops `JinGuCheats.dll`, `jingu-cheats-ui.exe`, and the `pre-launch.*` auto-heal scripts into `Mods\`
3. **Recommended:** add the Steam Launch Option the installer prints out — it tells Steam to run `pre-launch.bat` before the game, which auto-repairs the corlibs whenever a JinGu update reverts them (this happens every few updates).
4. Launch JinGu through Steam. The cheat UI window opens automatically within a couple seconds of the game starting.

To uninstall, **right-click `uninstall.bat` → Run as administrator**. It removes `version.dll`, `MelonLoader\`, `Mods\`, and `UserData\`. Your save files are untouched.

## Install — manual mode (no installer)

If you'd rather not run a script:

1. **Extract the bundled `MelonLoader.x64.zip`** from the release into your JinGu folder (`C:\Program Files (x86)\Steam\steamapps\common\JinGu\JinGu\`). After extracting, `version.dll` should sit next to `JinGu.exe`.

2. **Patch the stripped Mono runtime.** Open PowerShell, paste, hit Enter:

   ```powershell
   $src = "C:\Program Files (x86)\Steam\steamapps\common\JinGu\JinGu\MelonLoader\Dependencies\MonoBleedingEdgePatches"
   $dst = "C:\Program Files (x86)\Steam\steamapps\common\JinGu\JinGu\JinGu_Data\Managed"
   foreach ($f in "mscorlib.dll","System.dll","System.Core.dll","System.Runtime.dll") {
     attrib -R "$dst\$f" 2>$null
     Copy-Item "$src\$f" "$dst\$f" -Force
   }
   ```

3. **Copy these from the release zip into `<game folder>\Mods\`**:
   - `JinGuCheats.dll`
   - `jingu-cheats-ui.exe`
   - `pre-launch.bat`  (optional — for the auto-heal feature below)
   - `pre-launch.ps1`  (optional — used by the bat)

4. **Optional auto-heal Steam Launch Option** — Steam updates revert the corlibs every few patches. Pasting this once into `Steam → JinGu → Properties → General → Launch Options` makes Steam re-patch them on every launch:

   ```
   "C:\Program Files (x86)\Steam\steamapps\common\JinGu\JinGu\Mods\pre-launch.bat" %command%
   ```

5. Launch through Steam.

> The bundled MelonLoader is 0.7.3. Newer versions may work but are unverified — for predictable behavior, use the bundled copy.

## Using the cheats

### Through the UI

The UI window has tabs across the top: **主公** (Self) · **探索** (Explore) · **活动** (Mini-games) · **物品** (Items) · **角色** (NPCs) · **门派** (Sects) · **关于** (About).

- **Toggles** (无限银两, 无敌, 无消耗, 时停, XP rate boosts) — flip on/off in their tab
- **Buttons** (+ 十万 银两, + 根骨, 满级所有武学, ...) — click to apply
- **Sliders + Apply** — set the amount, click Apply (or flip the toggle for rate boosts)
- **Live values** — small badges next to each cheat show the current in-game value, updated a few times per second
- **Hotkeys** — in the About tab, click "未绑定" next to each cheat and press your chosen key

The leader card at the top shows your character's name, current money, in-game time, and difficulty.

### Through hotkeys

By default no hotkeys are bound — you pick them. In the UI's 关于 / About tab:

1. Find the cheat you want a hotkey for
2. Click the "未绑定" / "Not bound" button next to it
3. A small modal appears: "Press any key to bind"
4. Press the key you want (e.g. F2)
5. Done — the button now shows the key name

Press Escape during binding to cancel. Click the "×" next to a binding to clear it.

## Troubleshooting

### "Windows Defender / antivirus flags the DLL"

Game-cheat DLLs are routinely flagged by AV products even when they're benign — they hook into another process and reflection-modify game state, which looks like malware behavior. If you're not comfortable with that, don't install. If you trust the build, allow the file in your AV exclusions. The full source is in this repository for inspection.

### "UI didn't appear"

Check `MelonLoader\Latest.log` in the game folder. Look for:

```
[JinGu_Cheats] === JinGu Cheats X.Y.Z READY ===
```

If you see that, the plugin loaded. The UI should follow within a second.

If the UI process spawned (`pid N` in the log) but you don't see a window, alt-tab around — fullscreen games can hide it. If you see a `WebView2 runtime not found` error, install it from <https://developer.microsoft.com/microsoft-edge/webview2/>.

### "TypeLoadException" in the log

Steam reverted a corlib. Re-run step 2 above, or set up the auto-heal in step 5.

### "Game won't launch after install"

Most likely your `version.dll` from MelonLoader isn't being loaded. Make sure `version.dll` sits in the same folder as `JinGu.exe` (not inside any subfolder).

If Windows SmartScreen blocks the unsigned `version.dll`, right-click it → Properties → check "Unblock" at the bottom.

### "Cheats do nothing"

You probably loaded a save when the plugin wasn't running. The leader card in the UI should show your character's name and money — if it shows "尚未载入存档", the plugin can't see your save. Save the game, close it fully, relaunch through Steam.

### "Items I add via the UI disappear"

Some items are flagged as session-only or quest-only by the game and get cleared on save/load. Try a different item from the same category — most regular items persist normally.

### Save-game compatibility

The mod doesn't modify save format. You can uninstall any time by deleting `version.dll`, `MelonLoader\`, and `Mods\` from the game folder. Saves keep working with the vanilla game.

## Uninstall

**Easy:** right-click `installer\uninstall.bat` → Run as administrator. It deletes `version.dll`, `MelonLoader\`, `Mods\`, `UserData\` after a y/N confirmation.

**Manual:** delete these from the game folder:
- `version.dll`
- `MelonLoader\`
- `Mods\`
- `UserData\`  (optional — has MelonLoader preferences and our mod's settings)

Either way, save files are untouched (they live in `%LOCALAPPDATA%Low\金十四工作室\JinGu\SaveDatas\`).

If you set the Steam Launch Option, clear it from JinGu → Properties → Launch Options.

To restore the original stripped corlibs: Steam → JinGu → Properties → Installed Files → Verify integrity. Steam will redownload them.

## Credit

Built on top of:
- [MelonLoader](https://github.com/LavaGang/MelonLoader) for the runtime injection layer
- [Harmony](https://github.com/pardeike/Harmony) for method patching
- [Tauri](https://tauri.app) for the UI shell
- [BepInEx unstripped Unity corlibs](https://unity.bepinex.dev) for restoring the runtime types Unity stripped during the game build

## License

This mod (plugin source + UI source + installer scripts) is MIT — see [LICENSE](LICENSE).

The release zip also bundles **MelonLoader 0.7.3 (Apache 2.0)**, distributed unchanged. Its license is preserved as `MelonLoader-LICENSE.md` in the release. Source and notice: <https://github.com/LavaGang/MelonLoader>.
