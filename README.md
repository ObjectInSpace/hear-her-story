# Hear Her Story

A screen-reader accessibility mod for Sam Barlow's **Her Story**.

Her Story is a game about searching a police database of video interviews. Its
interface is a simulated desktop — a search box, a results list of clip
thumbnails, a video player — none of which is exposed to assistive technology.
This mod makes that interface navigable and audible.

## Features

- Focus tracking and spoken announcements across the desktop UI
- Clip library navigation and search box handling
- Transcript, progress, and favourite announcements
- Configurable key bindings

### Controls

| Key | Action |
| --- | --- |
| Arrows | Move within a group |
| Tab / Ctrl+Tab | Move between groups / windows |
| Backtick | Repeat last announcement |
| Ctrl+T | Transcript |
| Ctrl+P | Progress |
| Ctrl+D | Favourite |
| Ctrl+J | Toggle spoken captions |
| Ctrl+K | Repeat caption |
| Esc | Stop clip |
| F8 | Audit which focusable controls can be activated |

## Installing

Download the latest release and extract it into your Her Story folder, next to
`HerStory.exe`. That is the whole install — MelonLoader and NVDA support are
bundled.

Do **not** install stock MelonLoader over the top; it ships a `Tomlet.dll` that
fails on this game's Unity version, and the failure is silent. See the release
notes for details.

NVDA works out of the box. JAWS, Dolphin, and System Access users should copy
their own client library (`jfwapi.dll`, `dolapi32.dll`, `SAAPI32.dll`) next to
`HerStory.exe` — without one, speech falls back silently to SAPI.

## Building

Requires the .NET SDK. The build targets `net35` to match Unity 5.0.1's Mono.

```
dotnet build src/HearHerStory/HearHerStory.csproj -c Release
```

`lib/` must contain the game and engine assemblies, copied from a local Her
Story installation:

```
Assembly-CSharp.dll   Assembly-UnityScript.dll   UnityEngine.dll
UnityEngine.UI.dll    MelonLoader.dll            0Harmony.dll
```

These are not redistributed here and are excluded from version control.

## Licence

GNU General Public License v3.0 or later — see [LICENSE](LICENSE).

GPL-3.0 specifically (rather than v2) because the release bundle includes
Apache-2.0 licensed MelonLoader, which is compatible with GPLv3 but not GPLv2.

Third-party components and their licences are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Her Story is copyright Sam Barlow. This is an unofficial, non-commercial
accessibility modification, not endorsed by or affiliated with the author or
publisher. No game assets or code are redistributed.
