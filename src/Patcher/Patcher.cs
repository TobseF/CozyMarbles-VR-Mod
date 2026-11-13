using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace CozyMarblesVR.Patcher
{
    /// <summary>
    /// BepInEx preloader patcher. Rewrites the game's calls to UnityEngine.Input and to the camera
    /// screen-projection methods so that they go through <c>CozyMarblesVR.VRShim</c>. The assemblies
    /// on disk are not touched; the rewrite happens in memory on every start.
    /// </summary>
    public static class VRPatcher
    {
        static readonly ManualLogSource Log = Logger.CreateLogSource("CozyMarblesVR.Patcher");

        public static IEnumerable<string> TargetDLLs { get; } = new[] { "Assembly-CSharp.dll", "Assembly-CSharp-firstpass.dll" };

        static readonly HashSet<string> RedirectedTypes = new HashSet<string> { "UnityEngine.Input", "UnityEngine.Camera", "UnityEngine.Screen" };

        const string ShimFile = "CozyMarblesVR.Shim.dll";
        static string _shimPath;

        /// <summary>
        /// The shim normally sits in BepInEx\plugins\CozyMarblesVR. Mod managers may unpack plugins into other
        /// sub-folders, so fall back to searching the plugin folder.
        /// </summary>
        static string ShimPath
        {
            get
            {
                if (_shimPath != null) return _shimPath;
                string standard = Path.Combine(Path.Combine(Paths.PluginPath, "CozyMarblesVR"), ShimFile);
                _shimPath = standard;
                if (!File.Exists(standard) && Directory.Exists(Paths.PluginPath))
                {
                    string found = Directory.GetFiles(Paths.PluginPath, ShimFile, SearchOption.AllDirectories).FirstOrDefault();
                    if (found != null) _shimPath = found;
                }
                return _shimPath;
            }
        }

        static AssemblyDefinition _shim;
        static Dictionary<string, MethodDefinition> _shimMethods;

        public static void Initialize()
        {
            try
            {
                CheckNativeFiles();
                if (!File.Exists(ShimPath))
                {
                    Log.LogError("Shim assembly missing: " + ShimPath + " - the game will run unmodified.");
                    return;
                }
                // Make the shim resolvable for the rewritten game assembly.
                Assembly.LoadFrom(ShimPath);
                _shim = AssemblyDefinition.ReadAssembly(ShimPath);
                var type = _shim.MainModule.GetType("CozyMarblesVR.VRShim");
                _shimMethods = new Dictionary<string, MethodDefinition>();
                foreach (var m in type.Methods.Where(m => m.IsPublic && m.IsStatic && m.Name.Contains("_")))
                    _shimMethods[Signature(m.Name, m.Parameters.Select(p => p.ParameterType.FullName))] = m;
            }
            catch (Exception e)
            {
                Log.LogError("Patcher initialisation failed: " + e);
                _shimMethods = null;
            }
        }

        static void CheckNativeFiles()
        {
            string data = Path.Combine(Paths.GameRootPath, Paths.ProcessName + "_Data");
            string[] needed =
            {
                Path.Combine(data, "Plugins", "x86_64", "UnityOpenXR.dll"),
                Path.Combine(data, "Plugins", "x86_64", "openxr_loader.dll"),
                Path.Combine(data, "UnitySubsystems", "UnityOpenXR", "UnitySubsystemsManifest.json"),
            };
            // copies shipped with the mod: release builds carry them in <plugin folder>\native,
            // a source checkout inside the game folder in CozyMarblesVR\lib\native
            string[] sources =
            {
                Path.Combine(Path.GetDirectoryName(ShimPath), "native"),
                Path.Combine(Path.Combine(Path.Combine(Paths.GameRootPath, "CozyMarblesVR"), "lib"), "native"),
            };
            foreach (string target in needed)
            {
                if (File.Exists(target)) continue;
                string src = sources.Select(s => Path.Combine(s, Path.GetFileName(target))).FirstOrDefault(File.Exists);
                if (src == null)
                {
                    Log.LogError("OpenXR runtime file missing and no copy to restore it from: " + target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(src, target, true);
                Log.LogWarning("Restored " + target + " (restart the game once if VR does not start).");
            }
        }

        static string Signature(string name, IEnumerable<string> parameterTypes) => name + "(" + string.Join(",", parameterTypes) + ")";

        public static void Patch(AssemblyDefinition assembly)
        {
            if (_shimMethods == null) return;
            int replaced = 0, methods = 0;
            foreach (var module in assembly.Modules)
            {
                foreach (var type in AllTypes(module.Types))
                {
                    foreach (var method in type.Methods)
                    {
                        if (!method.HasBody) continue;
                        bool touched = false;
                        foreach (var ins in method.Body.Instructions)
                        {
                            if (ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt) continue;
                            if (!(ins.Operand is MethodReference target)) continue;
                            string declaring = target.DeclaringType.FullName;
                            if (!RedirectedTypes.Contains(declaring)) continue;

                            var parameters = new List<string>();
                            if (target.HasThis) parameters.Add(declaring);
                            parameters.AddRange(target.Parameters.Select(p => p.ParameterType.FullName));
                            string key = Signature(target.DeclaringType.Name + "_" + target.Name, parameters);
                            if (!_shimMethods.TryGetValue(key, out var replacement)) continue;

                            ins.OpCode = OpCodes.Call;
                            ins.Operand = module.ImportReference(replacement);
                            replaced++;
                            touched = true;
                        }
                        if (touched) methods++;
                    }
                }
            }
            Log.LogInfo($"{assembly.Name.Name}: redirected {replaced} input/camera calls in {methods} methods.");
        }

        static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
        {
            foreach (var t in types)
            {
                yield return t;
                foreach (var n in AllTypes(t.NestedTypes))
                    yield return n;
            }
        }
    }
}
