using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

if (args.Length != 3)
    throw new ArgumentException("Usage: Patcher <original launcher DLL> <features DLL> <output DLL>");

const string originalLauncherHash = "54B23427598A4FDC73C9FA6C5FDEC1535965DAA371B1C91E995A6CB2DB9BF3AA";
var originalLauncherPath = Path.GetFullPath(args[0]);
var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(originalLauncherPath)));
if (actualHash != originalLauncherHash)
    throw new InvalidOperationException("Unrecognized Airlock Launcher assembly.");

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(originalLauncherPath)!);
resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
using var launcher = ModuleDefinition.ReadModule(originalLauncherPath, new ReaderParameters { AssemblyResolver = resolver });
using var hooks = ModuleDefinition.ReadModule(args[1]);
if (launcher.Assembly.Name.Version.Major != 4 || launcher.Assembly.Name.Version.Minor != 2 ||
    launcher.AssemblyReferences.Any(reference => reference.Name == hooks.Assembly.Name.Name))
    throw new InvalidOperationException("Expected the original Airlock Launcher 4.2 assembly.");

var entryPoints = hooks.GetType("Airlock.Features.LauncherFeatures");
MethodReference Hook(string name) =>
    launcher.ImportReference(entryPoints.Methods.Single(method => method.Name == name));

MethodDefinition Method(string type, string name) =>
    launcher.GetType("SS14.Launcher." + type).Methods.Single(method => method.Name == name);

void Attach(string type, string hook)
{
    var constructor = Method(type, ".ctor");
    var il = constructor.Body.GetILProcessor();
    var end = constructor.Body.Instructions.Single(instruction => instruction.OpCode == OpCodes.Ret);
    il.InsertBefore(end, il.Create(OpCodes.Ldarg_0));
    il.InsertBefore(end, il.Create(OpCodes.Call, Hook(hook)));
}

Attach("Views.MainWindowTabs.ServerEntryView", "AttachServerCard");
Attach("Views.ConnectingOverlay", "AttachRecovery");
Attach("Views.MainWindow", "AttachReleaseCheck");
Attach("ViewModels.MainWindowTabs.GuidesTabViewModel", "AddCookingGuide");

var versionGetter = Method("ViewModels.MainWindowViewModel", "get_Version");
versionGetter.Body.Instructions.Clear();
versionGetter.Body.Variables.Clear();
var versionIl = versionGetter.Body.GetILProcessor();
versionIl.Emit(OpCodes.Call, Hook("DisplayReleaseVersion"));
versionIl.Emit(OpCodes.Ret);

const string repositoryPath = "github.com/Sk1rs/Airlock-Launcher-reload";
foreach (var type in launcher.GetTypes())
{
    foreach (var method in type.Methods.Where(method => method.HasBody))
    {
        foreach (var instruction in method.Body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Ldstr))
        {
            instruction.Operand = instruction.Operand switch
            {
                "https://github.com/Sk1rs" => "https://" + repositoryPath,
                "github.com/Sk1rs" => repositoryPath,
                _ => instruction.Operand
            };
        }
    }
}

var disclaimer = launcher.GetType("SS14.Launcher.Views.DisclaimerDialog");
disclaimer.Fields.Single(field => field.Name == "ForkUrl").Constant = "https://" + repositoryPath;

CharacterPreviewRemoval.Remove(launcher, Method("ViewModels.MainWindowViewModel", ".ctor"));

var start = Method("ViewModels.ConnectingViewModel", "Start");
var startIl = start.Body.GetILProcessor();
var first = start.Body.Instructions[0];
startIl.InsertBefore(first, startIl.Create(OpCodes.Ldarg_0));
startIl.InsertBefore(first, startIl.Create(OpCodes.Ldarg_1));
startIl.InsertBefore(first, startIl.Create(OpCodes.Call, Hook("RememberServer")));

foreach (var name in new[] { "StartConnect", "StartContentBundle" })
{
    var method = start.DeclaringType.Methods.Single(method => method.Name == name && method.IsStatic);
    var il = method.Body.GetILProcessor();
    var original = method.Body.Instructions[0];
    il.InsertBefore(original, il.Create(OpCodes.Call, Hook("OperationInProgress")));
    il.InsertBefore(original, il.Create(OpCodes.Brfalse, original));
    il.InsertBefore(original, il.Create(OpCodes.Ret));
}

var registerProtocol = Method("App", "RegisterProtocol");
registerProtocol.Body.Instructions.Clear();
registerProtocol.Body.ExceptionHandlers.Clear();
registerProtocol.Body.Variables.Clear();
registerProtocol.Body.GetILProcessor().Emit(OpCodes.Ret);

using (var destination = new FileStream(args[2], FileMode.CreateNew))
{
    launcher.Write(destination);
}

using var verified = ModuleDefinition.ReadModule(args[2]);
if (verified.GetTypes().Concat(verified.GetTypeReferences()).Any(CharacterPreviewRemoval.IsPreviewType))
    throw new InvalidOperationException("Character preview types or references remain in the output.");
Console.WriteLine($"Patched {launcher.Assembly.Name.FullName}");
