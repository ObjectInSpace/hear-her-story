Screen-reader accessibility mod for Sam Barlow's **Her Story**.

**Two downloads this time:**

- **`HearHerStory-0.5.0.zip`**: for everyone. Self-contained, so MelonLoader and the NVDA client are bundled. Extract it into the game folder and play.
- **`HearHerStory-0.5.0-slim.zip`**: only if you already run MelonLoader for this game. It contains the mod and support files. Read the `HearHerStory-README.txt` inside it before installing.

## What changed in 0.5.0

### The end-game chat is now spoken

The chat window that opens near the end of the game had no speech at all. Now:

- Each message is read as a whole. The game draws one message as several wrapped lines, and the mod joins them back together.
- When you are asked something, the mod reads the answers the game accepts, grouped into yes answers and no answers. Type a whole answer and press Enter. The game matches a partial entry anywhere inside an answer, not only at the start, so "y" alone can match a no.
- Your typing is echoed letter by letter, the same way the search box works.
- The game silently refuses any key that does not continue an accepted answer. A refused key now says so, and names the letters that would be accepted, such as "q not accepted. Next letter can be e or o".
- **Ctrl+T** in the chat reads the whole conversation so far, plus the accepted answers when the game is waiting for one.

### A direct path between the search box and the results

- **Down arrow** in the search box moves to the results. You return to the result you were last on. The search box has always told you to press Down arrow, but until now the key did nothing there.
- **Up arrow** on a result goes back to the search box.
- **Tab** now goes in this order: search box, results, toolbar, then everything else. Favourites no longer come between the search box and the results.
- Arriving on the search box or a result no longer adds a group name and item count before the label.

## Install

1. Extract the full archive into your Her Story folder, next to `HerStory.exe`, and let it merge with what is there.
2. Run `HerStory.exe`.

That is the whole install. Do not install MelonLoader separately, because the full bundle includes a rebuilt file it needs (see below).

To upgrade from 0.4.x, extract over the top and let it overwrite. Only `Mods\HearHerStory.dll` has changed.

## What is bundled

| Component | Purpose | Full | Slim |
| --- | --- | --- | --- |
| MelonLoader v0.7.3 Open-Beta (net35) | Mod loader, with `version.dll` injector | yes | no |
| `MelonLoader\net35\Tomlet.dll` | Rebuilt, required (see below) | yes | yes |
| `Mods\HearHerStory.dll` | The mod | yes | yes |
| `UserLibs\UnityAccessibilityLib.dll` | Accessibility support library | yes | yes |
| `UniversalSpeech.dll` | Speech output | yes | yes |
| `nvdaControllerClient.dll` | NVDA support | yes | yes |

### Why a rebuilt Tomlet.dll

Stock Tomlet fails on the old version of Mono that Unity 5.0.1 uses: `TomlSerializationMethods`' static constructor cannot resolve a generic type and throws `TypeLoadException`. `MelonBase.RegisterCallbacks()` touches `MelonPreferences`, so this makes every mod fail to register, and nothing is written to the log. The bundled build removes the generic constraint that causes it.

The slim bundle ships this file too. If you install it over a stock MelonLoader, you must let it overwrite `Tomlet.dll`, or the mod will not load. If MelonLoader later auto-updates, it may restore the stock file; extract the archive again to fix that.

### Screen reader support

NVDA works out of the box. If you use JAWS, Dolphin or System Access, copy your screen reader's client file (`jfwapi.dll`, `dolapi32.dll` or `SAAPI32.dll`) next to `HerStory.exe`. Without one, speech falls back to SAPI without any warning, which is easy to mistake for working output.

## Controls

- Down arrow from the search box goes to the results. Up arrow from a result goes back.
- Arrow keys move within a group, Tab moves between groups, and Ctrl+Tab moves between windows.
- Backtick repeats the last announcement.
- Ctrl+T reads the transcript, or the conversation in the chat window. Ctrl+P reports progress. Ctrl+D adds the clip to favourites.
- Esc stops a clip. Ctrl+J turns spoken captions on and off. Ctrl+K repeats the current caption.
- F8 checks which focusable controls can be activated.

## Verifying the install

`MelonLoader\Latest.log` should contain:

```
1 Mod loaded.
[Hear_Her_Story] Speech ready.
```
