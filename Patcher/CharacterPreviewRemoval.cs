using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class CharacterPreviewRemoval
{
    private static readonly HashSet<string> PreviewTypes = new(StringComparer.Ordinal)
    {
        "CharacterTabView", "CharacterTabViewModel", "CharacterGalleryDialog", "CharacterGalleryViewModel",
        "GalleryEntryViewModel", "CharacterLayerViewModel", "DirectionOptionViewModel", "ForkOptionViewModel",
        "JobOptionViewModel", "MarkingOptionViewModel", "SpeciesOptionViewModel"
    };

    public static void Remove(ModuleDefinition module, MethodDefinition windowConstructor)
    {
        RemoveTabInitialization(windowConstructor);
        var window = windowConstructor.DeclaringType;
        var property = window.Properties.Single(property => property.Name == "CharacterTab");
        window.Methods.Remove(property.GetMethod);
        window.Properties.Remove(property);
        window.Fields.Remove(window.Fields.Single(field => field.Name == "<CharacterTab>k__BackingField"));

        var loader = module.GetType("CompiledAvaloniaXaml.!XamlLoader").Methods.Single(method => method.Name == "TryLoad" && method.Parameters.Count == 2);
        var resourcePath = loader.Body.Instructions.Single(instruction =>
            instruction.Operand is string path && path.EndsWith("/CharacterTabView.xaml", StringComparison.Ordinal));
        var branch = resourcePath.Next.Next.Next;
        var constructor = branch.Next;
        if (resourcePath.Previous.OpCode != OpCodes.Ldarg_1 ||
            branch.OpCode != OpCodes.Brfalse || constructor.OpCode != OpCodes.Newobj ||
            constructor.Operand is not MethodReference { DeclaringType.Name: "CharacterTabView" } ||
            constructor.Next.OpCode != OpCodes.Ret || branch.Operand != constructor.Next.Next)
            throw new InvalidOperationException("Unexpected compiled character preview loader.");
        var stop = constructor.Next.Next;
        for (var instruction = resourcePath.Previous; instruction != stop; instruction = instruction.Next)
        {
            instruction.OpCode = OpCodes.Nop;
            instruction.Operand = null;
        }

        foreach (var type in module.Types.Where(IsPreviewType).ToArray())
            module.Types.Remove(type);
        var xamlResources = module.GetType("CompiledAvaloniaXaml.!AvaloniaResources");
        foreach (var type in xamlResources.NestedTypes.Where(IsPreviewType).ToArray())
            xamlResources.NestedTypes.Remove(type);

        var resource = (EmbeddedResource)module.Resources.Single(item => item.Name == "!AvaloniaResources");
        module.Resources[module.Resources.IndexOf(resource)] =
            new EmbeddedResource(resource.Name, resource.Attributes, StripResources(resource));
    }

    private static void RemoveTabInitialization(MethodDefinition windowConstructor)
    {
        var characterGetter = windowConstructor.Body.Instructions.Single(instruction =>
            instruction.OpCode == OpCodes.Call && instruction.Operand is MethodReference { Name: "get_CharacterTab" });
        var loadTabList = characterGetter.Previous.Previous;
        var addTab = characterGetter.Next;
        if (loadTabList.OpCode != OpCodes.Ldloc_0 || characterGetter.Previous.OpCode != OpCodes.Ldarg_0 ||
            addTab.OpCode != OpCodes.Callvirt || addTab.Operand is not MethodReference { Name: "Add" })
            throw new InvalidOperationException("Unexpected character tab registration.");
        var characterConstructor = windowConstructor.Body.Instructions.Single(instruction =>
            instruction.OpCode == OpCodes.Newobj && instruction.Operand is MethodReference method &&
            method.DeclaringType.Name == "CharacterTabViewModel");
        var storeTab = characterConstructor.Next;
        if (characterConstructor.Previous.OpCode != OpCodes.Ldarg_0 || storeTab.OpCode != OpCodes.Stfld ||
            storeTab.Operand is not FieldReference { Name: "<CharacterTab>k__BackingField" })
            throw new InvalidOperationException("Unexpected character tab initialization.");
        foreach (var instruction in new[] { loadTabList, characterGetter.Previous, characterGetter, addTab,
                     characterConstructor.Previous, characterConstructor, storeTab })
        {
            instruction.OpCode = OpCodes.Nop;
            instruction.Operand = null;
        }
    }

    public static bool IsPreviewType(TypeReference type) =>
        type.Namespace == "SS14.Launcher.Models.Character" || PreviewTypes.Contains(type.Name) ||
        (type.Name.StartsWith("NamespaceInfo:", StringComparison.Ordinal) &&
         (type.Name.EndsWith("/CharacterTabView.xaml", StringComparison.Ordinal) ||
          type.Name.EndsWith("/CharacterGalleryDialog.xaml", StringComparison.Ordinal))) ||
        (type.Namespace == "System.Text.RegularExpressions.Generated" &&
         Regex.IsMatch(type.Name, @"__(SpeciesParts|MarkingFile|SpeciesPrototype|JobFile|ClothingFile|LocaleFile|Entry)Regex_[0-6]$"));

    private static byte[] StripResources(EmbeddedResource resource)
    {
        using var reader = new BinaryReader(resource.GetResourceStream(), Encoding.UTF8);
        var indexLength = reader.ReadInt32();
        var version = reader.ReadInt32();
        if (version != 2)
            throw new InvalidDataException("Unsupported Avalonia resource index.");
        var count = reader.ReadInt32();
        var entries = new (string Path, int Offset, int Length)[count];
        for (var i = 0; i < count; i++)
            entries[i] = (reader.ReadString(), reader.ReadInt32(), reader.ReadInt32());
        if (reader.BaseStream.Position != indexLength + sizeof(int))
            throw new InvalidDataException("Unexpected Avalonia resource index length.");

        using var index = new MemoryStream();
        using var indexWriter = new BinaryWriter(index, Encoding.UTF8, leaveOpen: true);
        using var content = new MemoryStream();
        indexWriter.Write(version);
        indexWriter.Write(count);
        foreach (var entry in entries)
        {
            reader.BaseStream.Position = indexLength + sizeof(int) + entry.Offset;
            var bytes = reader.ReadBytes(entry.Length);
            if (bytes.Length != entry.Length)
                throw new EndOfStreamException(entry.Path);
            if (entry.Path.StartsWith("/Assets/Locale/", StringComparison.Ordinal) && entry.Path.EndsWith(".ftl", StringComparison.Ordinal))
            {
                var text = Encoding.UTF8.GetString(bytes);
                text = Regex.Replace(text, @"(?m)^(?:character-|gallery-|tab-character-)[\w-]*[ \t]*=[^\r\n]*(?:\r?\n[ \t]+[^\r\n]*)*\r?\n?", "");
                bytes = Encoding.UTF8.GetBytes(text);
            }
            else if (entry.Path == "/!AvaloniaResourceXamlInfo")
            {
                var document = XDocument.Parse(Encoding.UTF8.GetString(bytes));
                foreach (var pair in document.Descendants().Where(element =>
                    element.Name.LocalName == "KeyValueOfstringstring" && element.Elements().Any(child =>
                        child.Name.LocalName == "Key" && (child.Value.EndsWith(".CharacterTabView", StringComparison.Ordinal) ||
                                                         child.Value.EndsWith(".CharacterGalleryDialog", StringComparison.Ordinal)))).ToArray())
                    pair.Remove();
                bytes = Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
            }
            indexWriter.Write(entry.Path);
            indexWriter.Write(checked((int)content.Length));
            indexWriter.Write(bytes.Length);
            content.Write(bytes);
        }

        using var result = new MemoryStream();
        using var writer = new BinaryWriter(result, Encoding.UTF8, leaveOpen: true);
        writer.Write(checked((int)index.Length));
        index.WriteTo(result);
        content.WriteTo(result);
        return result.ToArray();
    }
}
