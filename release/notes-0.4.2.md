Screen-reader accessibility mod for Sam Barlow's **Her Story**.

**Self-contained — nothing else to install.** MelonLoader and the NVDA client are bundled. Extract into the game folder and play.

## What changed in 0.4.2

Fixes the settings window's subtitles and glare toggles, which were broken in two ways at once.

**They were not actually changing.** Both are Unity `Toggle`s, which carry no `Button`, so activating one fell through to the mod's synthetic-click path. That path fired both a pointer click and a submit event, on the assumption that a control implements one or the other. `Toggle` implements both, and each one flips `isOn` — so a single keypress turned the setting on and straight back off. The setting never changed and the glare effects never applied. This affected mouse-free play only; the toggles were fine with a mouse.

**They never announced their new state.** Flipping a toggle does not move focus, so nothing spoke again — the state was read correctly on arrival and then went silently stale. The only way to hear a setting was to leave the control and come back to it. Activating a stateful control now speaks its new value.

Version strings are also brought back into line: the mod reported 0.4.0 in 0.4.1, and the project file still said 0.1.0.

## Install

1. Extract the archive into your Her Story folder, next to `HerStory.exe`, letting it merge.
2. Run `HerStory.exe`.

That is the whole install. Do not install MelonLoader separately — this bundle includes a build it needs (see below).

Upgrading from 0.4.1: extract over the top and let it overwrite. Only `Mods\HearHerStory.dll` differs.

## What is bundled

| Component | Purpose |
| --- | --- |
| MelonLoader v0.7.3 Open-Beta (net35) | Mod loader, with `version.dll` injector |
| `MelonLoader\net35\Tomlet.dll` | Rebuilt — required, see below |
| `Mods\HearHerStory.dll` | The mod |
| `UserLibs\UnityAccessibilityLib.dll` | Accessibility support library |
| `UniversalSpeech.dll` | Speech output |
| `nvdaControllerClient.dll` | NVDA support |

Trimmed to the net35 runtime this game uses, so it is 7.6 MB rather than 43 MB.

### Why a rebuilt Tomlet.dll

Stock Tomlet fails at type-init on Unity 5.0.1's old Mono: `TomlSerializationMethods`' static constructor cannot resolve a generic typespec and throws `TypeLoadException`. Because `MelonBase.RegisterCallbacks()` touches `MelonPreferences`, this cascades into **every** mod registration failing — silently, with nothing logged. The bundled build removes the offending constraint.

Installing stock MelonLoader over this bundle will reintroduce the bug. `MelonLoader.dll` itself is unmodified; only `Tomlet.dll` differs. See `MelonLoader\MODIFICATIONS.txt`.

### Screen reader support

NVDA works out of the box. JAWS, Dolphin, or System Access users should copy their own client (`jfwapi.dll`, `dolapi32.dll`, `SAAPI32.dll`) next to `HerStory.exe` — without one, speech falls back silently to SAPI, which is easy to mistake for working output.

## Controls

- Arrows move within a group, Tab between groups, Ctrl+Tab between windows
- Backtick repeats the last announcement
- Ctrl+T transcript, Ctrl+P progress, Ctrl+D favourite
- Esc stop clip, Ctrl+J toggle spoken captions, Ctrl+K repeat caption
- F8 audits which focusable controls can be activated

## Verifying the install

`MelonLoader\Latest.log` should contain:

```
1 Mod loaded.
[Hear_Her_Story] Speech ready.
```
