using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length == 4 && args[0] == "--tree")
{
    foreach (string source in Directory.EnumerateFiles(args[2], "*.dll"))
        Restore(Path.Combine(args[1], Path.GetFileName(source)), source, Path.Combine(args[3], Path.GetFileName(source)));
}
else if (args.Length == 3) Restore(args[0], args[1], args[2]);
else throw new ArgumentException("Usage: EngineCompat stock-module full-module output-module; or --tree stock-directory full-directory output-directory");

IEnumerable<TypeDefinition> Expand(IEnumerable<TypeDefinition> roots)
{
    foreach (var type in roots) { yield return type; foreach (var nested in Expand(type.NestedTypes)) yield return nested; }
}
string Signature(MethodDefinition method) => method.ReturnType.FullName + " " + method.Name + "(" + string.Join(",", method.Parameters.Select(p => p.ParameterType.FullName)) + ")";

void Restore(string stockPath, string sourcePath, string destination)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
    if (!File.Exists(stockPath)) { File.Copy(sourcePath, destination, true); return; }
    using var resolver = new DefaultAssemblyResolver();
    string sourceFolder = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
    resolver.AddSearchDirectory(sourceFolder);
    resolver.AddSearchDirectory(Path.Combine(sourceFolder, "Facades"));
    using var stock = ModuleDefinition.ReadModule(stockPath);
    using var full = ModuleDefinition.ReadModule(sourcePath, new ReaderParameters { AssemblyResolver = resolver });
    var stockTypes = Expand(stock.Types).ToDictionary(type => type.FullName);
    int changed = 0;
    // Unity's linker removes interface members and their implementations together.
    // Restoring an abstract member alone invalidates the game's existing classes.
    // A default implementation keeps the restored API callable for full engine types,
    // whose own implementations take precedence, without changing serialized game types.
    foreach (var type in Expand(full.Types))
    {
        if (!type.IsInterface) continue;
        stockTypes.TryGetValue(type.FullName, out var original);
        // A completely stripped parent interface can also be restored indirectly.
        var known = new HashSet<string>(original == null ? Array.Empty<string>() : original.Methods.Select(Signature));
        foreach (var method in type.Methods.Where(m => m.IsAbstract && !known.Contains(Signature(m))))
        {
            method.IsAbstract = false;
            method.Body = new MethodBody(method);
            var il = method.Body.GetILProcessor();
            var errorType = new TypeReference("System", "NotSupportedException", full, full.TypeSystem.CoreLibrary);
            var constructor = new MethodReference(".ctor", full.TypeSystem.Void, errorType) { HasThis = true };
            constructor.Parameters.Add(new ParameterDefinition(full.TypeSystem.String));
            il.Emit(OpCodes.Ldstr, "This game build stripped " + method.FullName + "; the original implementation is unavailable.");
            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Throw);
            changed++;
        }
    }
    if (changed == 0) File.Copy(sourcePath, destination, true);
    else
    {
        full.Write(destination);
        Console.WriteLine(Path.GetFileName(sourcePath) + ": made " + changed + " restored interface members optional; unavailable calls fail explicitly.");
    }
}
