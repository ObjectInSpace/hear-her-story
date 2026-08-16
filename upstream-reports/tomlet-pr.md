# Tomlet — PR

**Repo:** https://github.com/SamboyCoding/Tomlet
**Target branch:** `master`
**PR title:** Resolve reflection lookups lazily so a failure can't poison the whole type
**PR:** [SamboyCoding/Tomlet#63](https://github.com/SamboyCoding/Tomlet/pull/63) — opened 2026-08-16
**Branch:** `lazy-reflection-lookups` on `ObjectInSpace/Tomlet` (pushed; nothing local-only)

> The local clone was deleted on 2026-08-16 to reclaim 11 MB. To resume work if a
> maintainer responds:
> `gh repo clone ObjectInSpace/Tomlet -- -b lazy-reflection-lookups`

> Commit message must NOT contain `[publish]` — their CI treats that as a NuGet release trigger.

---

## Problem

`TomlSerializationMethods` resolves four `MethodInfo`s in field initializers:

```csharp
private static MethodInfo _stringKeyedDictionaryMethod = typeof(TomlSerializationMethods).GetMethod(nameof(StringKeyedDictionaryDeserializerFor), BindingFlags.Static | BindingFlags.NonPublic)!;
private static MethodInfo _primitiveKeyedDictionaryMethod = typeof(TomlSerializationMethods).GetMethod(nameof(PrimitiveKeyedDictionaryDeserializerFor), BindingFlags.Static | BindingFlags.NonPublic)!;
private static MethodInfo _genericDictionarySerializerMethod = typeof(TomlSerializationMethods).GetMethod(nameof(GenericDictionarySerializer), BindingFlags.Static | BindingFlags.NonPublic)!;
private static MethodInfo _genericNullableSerializerMethod = typeof(TomlSerializationMethods).GetMethod(nameof(GenericNullableSerializer), BindingFlags.Static | BindingFlags.NonPublic)!;
```

Field initializers run inside the static constructor, so if any of them throws, the result is a `TypeInitializationException` that permanently poisons `TomlSerializationMethods` — for every caller, including ones that never touch dictionary or nullable serialization. A `try`/`catch` at the call site can't help, because the failure happens the first time the type is touched.

This is reachable on the `netframework3.5` target. `Type.GetMethod` enumerates every method on the type, and `PrimitiveKeyedDictionaryDeserializerFor` is declared:

```csharp
private static Deserialize<Dictionary<TKey, TValue>> PrimitiveKeyedDictionaryDeserializerFor<TKey, TValue>(TomlSerializerOptions options)
    where TKey : unmanaged, IConvertible
```

The `unmanaged` constraint is emitted as `System.ValueType modreq(System.Runtime.InteropServices.UnmanagedType)`. Mono versions predating the fix for [mono/mono#6804](https://github.com/mono/mono/issues/6804) can't resolve that through reflection, and throw:

```
System.TypeLoadException: Could not load type 'Typespec 0x1b000001'.
  at (wrapper managed-to-native) System.MonoType:GetMethodsByName (string,System.Reflection.BindingFlags,bool,System.Type)
  at System.MonoType.GetMethodImpl (System.String name, BindingFlags bindingAttr, ...)
  at System.Type.GetMethod (System.String name, BindingFlags bindingAttr)
  at Tomlet.TomlSerializationMethods..cctor ()
```

The constraint is present in the non-`MODERN_DOTNET` branch as well — the one `netframework3.5` compiles — so the build produced for that target is affected.

Checked with Cecil: exactly two constructs in the assembly carry `unmanaged` constraints, both on this type.

```
METHOD Tomlet.TomlSerializationMethods::PrimitiveKeyedDictionaryDeserializerFor
TYPE   Tomlet.TomlSerializationMethods/<>c__DisplayClass30_0`2
```

## Change

Move the four lookups out of field initializers into lazily-resolved properties. No behavioural change where reflection succeeds; where it doesn't, the failure is confined to the serialization path that actually needs the method rather than destroying the type for all callers.

One file, +33/−9.

## Verification

- All six target frameworks build clean, including `netframework3.5`.
- Test suite passes: **164/164**. (Run locally against net8 rather than net9, purely because net9 isn't installed on this machine — no source changes to the test project.)
- Verified on a genuinely affected runtime: Unity 5.0.1f1's embedded Mono, x86, net35 profile. Stock Tomlet 6.2.0 fails there at type-init; this build initializes and runs correctly.

Worth highlighting for review: the fix leaves `PrimitiveKeyedDictionaryDeserializerFor` and its `unmanaged` constraint **completely intact** — only the timing of the `GetMethod` call changes. I confirmed via Cecil that the constrained method is still present in the patched assembly, so the runtime test shows that deferring the lookup is sufficient on its own; nothing about the constrained code needed altering.

## Notes

Found while modding a Unity 5.0.1 game, where MelonLoader bundles Tomlet for its config system. Since `netframework3.5` is a declared target framework, I've written this up as a bug in a supported configuration rather than a request for extended support — but happy to reframe if you see it differently.

I haven't added a regression test. CI runs on `ubuntu-latest` with .NET 9 only, so while it builds the net35 target it never executes that output on an old Mono — meaning no test I can write here would actually exercise the failure. Open to suggestions if you'd like something.

Investigated and drafted with AI assistance. Everything above was verified as described.
