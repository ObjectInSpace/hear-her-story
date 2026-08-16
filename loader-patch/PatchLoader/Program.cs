using Mono.Cecil;
using Mono.Cecil.Cil;

namespace PatchLoader;

/// <summary>
/// Patches Tomlet.dll so MelonLoader can run on Unity 5.0.1's old Mono.
///
/// The defect: TomlSerializationMethods' static constructor eagerly resolves four
/// private static generic methods by reflection:
///
///     _stringKeyedDictionaryMethod    = ...GetMethod("StringKeyedDictionaryDeserializerFor", ...)
///     _primitiveKeyedDictionaryMethod = ...GetMethod("PrimitiveKeyedDictionaryDeserializerFor", ...)
///     _genericDictionarySerializerMethod = ...GetMethod("GenericDictionarySerializer", ...)
///     _genericNullableSerializerMethod   = ...GetMethod("GenericNullableSerializer", ...)
///
/// Unity 5.0.1's Mono throws TypeLoadException ("Could not load type 'Typespec
/// 0x1b000001'") while enumerating those methods, because their signatures
/// reference generic typespecs it cannot resolve. Because this happens in a type
/// initializer, it cascades: TomlSerializationMethods -> TomlMapper ->
/// MelonPreferences -> anything that touches MelonPreferences at all.
///
/// That last part is what makes a narrower fix insufficient. MelonBase's
/// RegisterCallbacks() subscribes to MelonPreferences.OnPreferencesLoaded/Saved,
/// so *every melon registration* touches MelonPreferences even if the loader
/// never loads a config file. RegisterCallbacks() has no try/catch, so the
/// exception escapes Register() silently — the melon is constructed but never
/// initialized, with nothing logged.
///
/// The fix: replace those four GetMethod calls with `ldnull`. The fields are only
/// dereferenced by dictionary/nullable serialization paths (Tomlet.dll lines
/// 1674-1710), which MelonLoader's registration path never reaches. Anything that
/// genuinely needs them would now NRE instead of failing at type-init — an
/// acceptable trade here, since this mod stores no config through MelonPreferences.
/// </summary>
internal static class Program
{
    private const string TargetType = "Tomlet.TomlSerializationMethods";

    /// <summary>Reflection lookups to defuse, by the method name string they pass.</summary>
    private static readonly string[] LookupNames =
    {
        "StringKeyedDictionaryDeserializerFor",
        "PrimitiveKeyedDictionaryDeserializerFor",
        "GenericDictionarySerializer",
        "GenericNullableSerializer",
    };

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: PatchLoader <Tomlet.dll> <output.dll>");
            return 2;
        }

        string input = args[0];
        string output = args[1];

        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"input not found: {input}");
            return 2;
        }

        var resolver = new DefaultAssemblyResolver();
        var dir = Path.GetDirectoryName(Path.GetFullPath(input));
        if (!string.IsNullOrEmpty(dir))
        {
            resolver.AddSearchDirectory(dir);
        }

        using var assembly = AssemblyDefinition.ReadAssembly(
            input,
            new ReaderParameters { AssemblyResolver = resolver, ReadWrite = false });

        var type = assembly.MainModule.GetType(TargetType);
        if (type is null)
        {
            Console.Error.WriteLine($"type not found: {TargetType}");
            return 1;
        }

        var cctor = type.Methods.FirstOrDefault(m => m.Name == ".cctor");
        if (cctor is null || !cctor.HasBody)
        {
            Console.Error.WriteLine($"no static constructor on {TargetType}");
            return 1;
        }

        var il = cctor.Body.GetILProcessor();
        int patched = 0;

        // Each lookup compiles to roughly:
        //     ldtoken TomlSerializationMethods
        //     call    Type::GetTypeFromHandle
        //     ldstr   "<name>"
        //     ldc.i4  <BindingFlags>
        //     callvirt Type::GetMethod(string, BindingFlags)
        //     stsfld  <field>
        //
        // Rewriting just the callvirt to `ldnull` would leave the three pushed
        // arguments on the stack, so instead we walk back from the ldstr and turn
        // the whole sequence into nops, then push null for the stsfld.
        foreach (var name in LookupNames)
        {
            var ldstr = cctor.Body.Instructions.FirstOrDefault(
                i => i.OpCode == OpCodes.Ldstr && (i.Operand as string) == name);

            if (ldstr is null)
            {
                Console.Error.WriteLine($"lookup not found: {name} — Tomlet layout may have changed.");
                return 1;
            }

            // Forward to the stsfld that consumes this lookup's result.
            var store = ldstr;
            while (store is not null && store.OpCode != OpCodes.Stsfld)
            {
                store = store.Next;
            }

            if (store is null)
            {
                Console.Error.WriteLine($"no stsfld after lookup: {name}");
                return 1;
            }

            // Back to the ldtoken that starts the sequence.
            var start = ldstr;
            while (start.Previous is not null && start.Previous.OpCode != OpCodes.Stsfld)
            {
                start = start.Previous;
            }

            // Blank everything from the start of the sequence up to (not
            // including) the stsfld, then supply a null for it to store.
            var cursor = start;
            while (cursor != store)
            {
                var next = cursor.Next;
                il.Replace(cursor, il.Create(OpCodes.Nop));
                cursor = next;
            }

            il.InsertBefore(store, il.Create(OpCodes.Ldnull));

            Console.WriteLine($"defused: {name}");
            patched++;
        }

        assembly.Write(output);

        Console.WriteLine($"patched {patched} reflection lookup(s)");
        Console.WriteLine($"wrote: {output}");
        return patched == LookupNames.Length ? 0 : 1;
    }
}
