## Problem

In `MelonBase.Register()`, `RegisterCallbacks()` is called without exception handling, immediately above an `OnEarlyInitializeMelon()` call that *is* guarded:

```csharp
Registered = true; // this has to be true before the melon can subscribe to any events
RegisterCallbacks();          // <-- unguarded

try
{
    OnEarlyInitializeMelon();
}
catch (Exception ex)
{
    MelonLogger.Error($"Failed to register {MelonTypeName} '{MelonAssembly.Location}': Melon failed to initialize!");
    MelonLogger.Error(ex.ToString());
    Registered = false;
    return false;
}
```

`MelonBase.RegisterCallbacks()` subscribes to `MelonPreferences.OnPreferencesLoaded` and `OnPreferencesSaved`. If anything in the preferences subsystem throws — notably a `TypeInitializationException`, raised the first time `MelonPreferences` is touched — the exception propagates out of `Register()` and registration dies.

Nothing is logged. The melon is constructed (static and instance constructors both run) but never initialized: no `OnInitializeMelon`, no `PrintLoadInfo()` banner, no error. From a mod author's perspective the loader prints `Melon Assembly loaded` and then simply stops.

## Observed behaviour

Log output ended here, with the game still running normally:

```
Loading Mods...
------------------------------
Melon Assembly loaded: '.\Mods\MlProbe.dll'
SHA256 Hash: 'E63D3A02...'
```

A file-writing trace inside the mod showed how far execution actually reached:

```
static ctor ran
instance ctor ran
(nothing further — OnInitializeMelon never invoked)
```

No error in `Latest.log`, the console, or the Unity player log.

## Change

Wrap `RegisterCallbacks()` in the same error handling already used for `OnEarlyInitializeMelon()` three lines below. 12 lines, one file.

An optional follow-up, not included here to keep the diff minimal: isolate the two `MelonPreferences` subscriptions inside `RegisterCallbacks()` so a broken preferences subsystem degrades to "no preference callbacks for this melon" rather than "melon does not load".

## Scope

To be explicit about what is and isn't platform-specific:

- **The trigger I hit** — `MelonPreferences`' type initializer failing — requires an old Mono. That's a Unity 5.0.1 game, well below your supported floor, and I'm not asking for support there.
- **The silent-swallow behaviour** is not platform-specific. Any exception from `RegisterCallbacks()`, from any cause, aborts registration with no log output on any platform.

The unguarded call also isn't a recent regression: the same pattern appears in 0.7.3 and in the older build shipped with a Unity 2017.4 title I checked.

I did not build a synthetic reproduction on a supported Unity version. `RegisterCallbacks()` is `private protected`, so an external melon can't override it, and on a supported runtime `MelonPreferences` initializes fine — any such demo would require patching MelonLoader or forcing the cctor to throw, which would only restate what the source already shows.

## Verification status

**Syntax-verified, not fully compiled.** Parsed with Roslyn: no syntax errors, and `Register()` now contains two `try`/`catch` blocks as intended — the new one wrapping `RegisterCallbacks()`, and the pre-existing one wrapping `OnEarlyInitializeMelon()` directly below.

I could not run a full compile: `MelonLoader.csproj` depends on `MelonLoader.Bootstrap`, which uses NativeAOT and needs the Visual Studio C++ workload for the platform linker, which isn't installed here. So type resolution hasn't been machine-checked. The change introduces no new types or members — it uses `Exception`, `MelonLogger.Error`, `MelonTypeName` and `MelonAssembly.Location`, all already used identically in the adjacent catch block — so I'd expect it to compile, but your `compile_windows.yml` will be the authoritative check.

For context on the underlying trigger, the accompanying fix in Tomlet (where the type-init failure originates) *is* fully verified: all targets build, 164/164 tests pass, and it's confirmed working on the affected runtime.

Investigated and drafted with AI assistance. Verification is stated exactly as it stands.
