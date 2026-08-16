## Problem

`TomlSerializationMethods` resolves private generic methods using reflection during type initialization. One of those methods is declared with this constraint:

```csharp
where TKey : unmanaged, IConvertible
```

The `unmanaged` constraint is emitted as `System.ValueType modreq(System.Runtime.InteropServices.UnmanagedType)`. Mono versions predating the fix for [mono/mono#6804](https://github.com/mono/mono/issues/6804) cannot resolve that metadata while enumerating the type's methods and throw:

```
System.TypeLoadException: Could not load type 'Typespec 0x1b000001'.
  at (wrapper managed-to-native) System.MonoType:GetMethodsByName (string,System.Reflection.BindingFlags,bool,System.Type)
  at System.MonoType.GetMethodImpl (System.String name, BindingFlags bindingAttr, ...)
  at System.Type.GetMethod (System.String name, BindingFlags bindingAttr)
  at Tomlet.TomlSerializationMethods..cctor ()
```

Because the lookup happens in the static constructor, this prevents any use of `TomlSerializationMethods`. The constraint is also present in the non-`MODERN_DOTNET` branch compiled for the declared `netframework3.5` target.

## Change

Remove the redundant `unmanaged, IConvertible` constraint from `PrimitiveKeyedDictionaryDeserializerFor`.

The method is private and is only constructed through reflection after `GetDeserializer` has restricted `TKey` to integer types, `bool`, or `char`. Its body does not rely on either constraint. Removing it therefore preserves the accepted inputs while avoiding the incompatible metadata.

## Verification

- All target frameworks build, including `netframework3.5`.
- Test suite passes: **164/164**.
- Verified this constraint-removal revision on Unity 5.0.1f1's embedded Mono, x86, net35 profile. Preferences loaded, the mod initialized fully, and the log contained no `TypeLoadException` or `TypeInitializationException`.

## Notes

Found while modding a Unity 5.0.1 game, where MelonLoader bundles Tomlet for its config system. I haven't added a regression test because CI does not execute the net35 output on an affected old Mono runtime.

Investigated and drafted with AI assistance. Everything above was verified as described.
