# Marketing assets

Source-of-truth text for places where the mod gets posted outside this repo. Keep these in sync with the README + release as the project evolves.

| File | Where to paste |
|---|---|
| `steam-guide.bbcode` | Steam Community Guide (bilingual zh + en) — <https://steamcommunity.com/app/3313720/guides/> |

## Posting a Steam Guide

1. Visit <https://steamcommunity.com/app/3313720/guides/>
2. Top right → **Create a Guide** (创建指南)
3. Category: **General** or **Reference**. Avoid Walkthrough/Strategy — those get scrutinized harder.
4. Title — keep it neutral; don't use the word "cheat" or "trainer" in the title. The guide title `今古群侠传 · 民间修改器 + 数据查询站` is fine ("民间修改器" = community modifier).
5. **Replace `REPLACE_WITH_YOUTUBE_VIDEO_ID` in the BBCode** with your actual YouTube video ID before pasting. The ID is the 11-character code in the URL:
   ```
   https://www.youtube.com/watch?v=dQw4w9WgXcQ
                                   ^^^^^^^^^^^
                                   this is the video ID
   ```
   Steam's BBCode embed format is `[previewyoutube=VIDEO_ID;full][/previewyoutube]`. If you don't have a video yet, delete the whole `[previewyoutube=...][/previewyoutube]` line.
6. Paste the rest of the BBCode body, preview, publish.
7. Set visibility: **Public** for discoverability. Start with **Friends only** if you want to verify it renders correctly before going public.

## Screenshots to add as cover images

Use the three screenshots in `../screenshot/` — drag-and-drop into the guide editor in this order (first image = thumbnail in the guide list):

1. `screenshot/01-self-stats.png` — 主公 tab (Player stats / theme showcase)
2. `screenshot/02-items-browser.png` — 物品 tab (Items browser depth)
3. `screenshot/03-npc-editor.png` — 角色 tab (NPC editor)

## Caveat — Steam Guide policy on cheats

Steam doesn't outright ban single-player cheat-mod guides, but **the game developer can request a guide be removed** and Steam usually complies without warning. Mitigations baked into this BBCode:

- Framed as "mod / tool", not "cheat" or "trainer"
- Described as "skip grind" / "accessibility" rather than "break the game"
- Hosts binaries on GitHub (a link, not an attachment Steam can remove)
- Credits the dev + links to Steam store ("buy the game to support developers")

If a guide gets removed, **don't re-post identically** — Steam recognizes it and removes again. Re-frame more conservatively and try once more. After two takedowns, accept the dev is strict and rely on GitHub + YouTube for discovery.

## Keeping it in sync

When the README, release version, or feature list changes substantively, update `steam-guide.bbcode`. The bilingual format keeps both languages in one file — short enough that posting one guide reaches both audiences.
