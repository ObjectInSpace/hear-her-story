# Third-Party Notices

Hear Her Story is licensed under the GNU General Public License v3.0 or later
(see `LICENSE`). It incorporates and/or distributes the following third-party
components, each under its own terms.

---

## UnityAccessibilityLib

- **Author:** LordLuceus
- **Source:** https://github.com/LordLuceus/UnityAccessibilityLib
- **Licence:** MIT

Linked by the mod and redistributed in release bundles as
`UserLibs/UnityAccessibilityLib.dll`. MIT is GPL-compatible; its copyright
notice and permission notice must be preserved in redistributions.

---

## MelonLoader

- **Author:** Lava Gang
- **Source:** https://github.com/LavaGang/MelonLoader
- **Licence:** Apache License 2.0

Redistributed in release bundles (v0.7.3 Open-Beta, net35 runtime only).
Apache-2.0 is compatible with GPLv3 (but *not* GPLv2 — this is part of why
this project is GPL-3.0 rather than GPL-2.0).

The bundled `MelonLoader/net35/Tomlet.dll` is a modified rebuild; the
modification is documented in `MelonLoader/MODIFICATIONS.txt` inside the release
archive, as required by Apache-2.0 section 4(b). `MelonLoader.dll` itself is
unmodified.

Apache-2.0 licence text and NOTICE ship in `MelonLoader/Documentation/` within
the release archive.

---

## Tomlet

- **Author:** SamboyCoding
- **Source:** https://github.com/SamboyCoding/Tomlet
- **Licence:** MIT

Bundled via MelonLoader as `MelonLoader/net35/Tomlet.dll`, rebuilt from source
with one generic constraint removed so it initializes on Unity 5.0.1's Mono.
See `upstream-reports/` for the defect analysis reported upstream.

---

## UniversalSpeech

- **Source:** https://github.com/qtnc/UniversalSpeech
- **Licence:** LGPL

Redistributed as `UniversalSpeech.dll` (32-bit). Used unmodified, via dynamic
linking, which is what LGPL permits without extending its terms to this project.

---

## nvdaControllerClient.dll

- **Author:** NV Access
- **Source:** https://github.com/nvaccess/nvda
- **Licence:** GNU General Public License v2.0 or later

Redistributed in release bundles so speech reaches NVDA directly rather than
falling back silently to SAPI. NVDA is licensed GPLv2-or-later; the
"or later" clause is what permits its inclusion alongside this project's
GPL-3.0 terms and the Apache-2.0 components.

---

## Her Story

Her Story is copyright Sam Barlow. This project is an unofficial,
non-commercial accessibility modification and is not endorsed by or affiliated
with the author or publisher.

**No game assets, engine assemblies, or game code are redistributed here.**
The build references `Assembly-CSharp.dll`, `Assembly-UnityScript.dll`, and the
Unity engine assemblies from a local installation only; these are excluded from
version control and from all release archives.
