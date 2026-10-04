# Release bundle

Two zips ship per release.

```powershell
.\release\build-release.ps1 -Both
```

| Output | For | Size |
| --- | --- | --- |
| `HearHerStory-<version>.zip` | Everyone. Self-contained — extract into the game folder, nothing else to install. | 7.6 MB |
| `HearHerStory-<version>-slim.zip` | Players who already run MelonLoader. Mod and support files only. | 0.2 MB |

`-Both` builds the pair; bare builds only the full one, `-Slim` only the slim
one.

## What the slim bundle drops — and what it must not

It omits stock MelonLoader and the `version.dll` injector, which the player
already has. It still ships `MelonLoader\net35\Tomlet.dll`, and that file is
not negotiable: a stock MelonLoader install carries the broken Tomlet, so a
slim bundle without it installs a mod that never loads and logs nothing.

For that reason the file list in the script is written as an explicit include
set rather than an "exclude `MelonLoader/`" rule — the obvious exclude would
quietly drop the one loader file that is ours. `MelonLoader\Documentation\`
and `MODIFICATIONS.txt` travel with Tomlet to satisfy Apache-2.0 s4(b).

The slim zip also carries `slim-README.txt` as `HearHerStory-README.txt`,
which warns about the overwrite and about MelonLoader auto-update restoring
the stock file. The full bundle has no such README — it is extract-and-play.

## Layout

| Path | Tracked | What it is |
| --- | --- | --- |
| `build-release.ps1` | yes | The build script |
| `slim-README.txt` | yes | Install notes packaged into the slim zip |
| `notes-<version>.md` | yes | GitHub release notes |
| `payload/` | **no** | Third-party binaries the bundle ships around the mod |
| `out/` | **no** | Built zips |

`payload/` is untracked because it is ~19 MB of MelonLoader, the speech DLLs and
the accessibility library — none of it ours to redistribute through this repo,
and all of it reconstructible (see below). It holds everything that does *not*
change between releases; only `Mods/HearHerStory.dll` differs, and the script
compiles that fresh each time.

## Bumping the version

Three files carry it, and they drift — 0.4.1 shipped a mod that reported itself
as 0.4.0 because only the tag moved. The script refuses to build unless all
three agree:

1. `src/HearHerStory/Plugin.cs` — the `MelonInfo` attribute (the authority)
2. `src/HearHerStory/Plugin.cs` — the `OnInitializeMelon` startup log line
3. `src/HearHerStory/HearHerStory.csproj` — `<Version>`

## Rebuilding payload/ if it is lost

Take it from the last published release, which is the same trimmed tree:

```powershell
gh release download v0.4.2 -R ObjectInSpace/hear-her-story -D .
Expand-Archive HearHerStory-0.4.2.zip -DestinationPath release\payload
Remove-Item release\payload\Mods\HearHerStory.dll
```

Do **not** rebuild it from the game install at `D:\root\her story`. That install
carries the full 43 MB MelonLoader rather than the trimmed net35 set, and
packaging from it bloats the bundle from 7.6 MB to 43 MB.

## What is in payload/

- MelonLoader v0.7.3 Open-Beta, net35 only, plus the `version.dll` injector
  (9.5 MB — over half the bundle)
- `MelonLoader/net35/Tomlet.dll` — **rebuilt**. Stock Tomlet throws
  `TypeLoadException` at type-init on Unity 5.0.1's Mono, which cascades into
  every mod failing to register, silently. `MelonLoader/MODIFICATIONS.txt`
  documents the change per Apache-2.0 s4(b).
- `UserLibs/UnityAccessibilityLib.dll`
- `UniversalSpeech.dll`, `nvdaControllerClient.dll`

`MelonLoader.dll` itself is unmodified — only Tomlet is patched.

## Verifying a build

A clean launch is not a pass; the failure mode is silent. Copy the game folder,
strip `MelonLoader/ Mods/ UserLibs/ Plugins/ UserData/ version.dll` and the two
speech DLLs, extract the zip over it, launch, and check `MelonLoader/Latest.log`
for `1 Mod loaded`, `Speech ready`, and
`screen reader clients present: nvdaControllerClient.dll`.

The slim bundle needs its own test against the case it is built for: install
**stock** MelonLoader 0.7.3 into a clean copy of the game, launch once to
confirm the loader itself runs, then extract the slim zip over it and check the
same three log lines. Testing it over the full bundle proves nothing, because
the patched Tomlet would already be in place.
