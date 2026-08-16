# Release bundle

Builds the self-contained zip players download: extract into the game folder,
nothing else to install.

```powershell
.\release\build-release.ps1
```

Output lands in `release\out\HearHerStory-<version>.zip`.

## Layout

| Path | Tracked | What it is |
| --- | --- | --- |
| `build-release.ps1` | yes | The build script |
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
