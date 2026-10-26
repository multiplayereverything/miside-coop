using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;

namespace MiSideTogether
{
    // Lab-only research: -mt-trace Type1,Type2 (game types, e.g. Location14_GameQuestPlayer).
    //   MT-RVA   Type.Method 0x...         the native code address in GameAssembly.dll of every method of those types,
    //                                      so the method bodies can be decompiled (REA, Ghidra) for designing a hook
    //   MT-TRACE Type.Method(args) #n      every call of their methods while playing (per-frame methods left out,
    //                                      the first 40 calls of each), which shows what a player's input really calls
    // Patching some types freezes the game (ObjectDoor did, 2026-10-26): trace few types at a time and drop one that hangs.
    static class Trace
    {
        static readonly HashSet<string> Skip = new HashSet<string> { "Update", "FixedUpdate", "LateUpdate", "OnGUI", "Awake", "OnEnable", "OnDisable" };
        static readonly Dictionary<string, int> calls = new Dictionary<string, int>();

        public static void Install(string typeNames)
        {
            Harmony harmony = new Harmony("local.misidetogether.trace");
            Assembly game = typeof(ObjectInteractive).Assembly;
            IntPtr gameAssembly = IntPtr.Zero;
            foreach (ProcessModule m in Process.GetCurrentProcess().Modules)
                if (string.Equals(m.ModuleName, "GameAssembly.dll", StringComparison.OrdinalIgnoreCase)) gameAssembly = m.BaseAddress;
            foreach (string name in typeNames.Split(','))
            {
                Type type = game.GetType(name.Trim());
                if (type == null) { TogetherRuntime.Log("Trace: no game type " + name); continue; }
                // Each generated NativeMethodInfoPtr_* holds an Il2Cpp MethodInfo*, whose first field is the code pointer.
                foreach (FieldInfo f in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic))
                {
                    if (!f.Name.StartsWith("NativeMethodInfoPtr_", StringComparison.Ordinal) || f.FieldType != typeof(IntPtr)) continue;
                    IntPtr info = (IntPtr)f.GetValue(null);
                    if (info == IntPtr.Zero) continue;
                    long code = Marshal.ReadIntPtr(info).ToInt64();
                    TogetherRuntime.Log("MT-RVA " + type.Name + "." + f.Name.Substring("NativeMethodInfoPtr_".Length) + " 0x" + (code - gameAssembly.ToInt64()).ToString("x"));
                }
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    if (method.IsSpecialName || Skip.Contains(method.Name)) continue;
                    try { harmony.Patch(method, new HarmonyMethod(typeof(Trace), nameof(Called))); }
                    catch (Exception ex) { TogetherRuntime.Log("Trace: cannot patch " + type.Name + "." + method.Name + ": " + ex.Message); }
                }
            }
        }

        static void Called(MethodBase __originalMethod, object[] __args)
        {
            try
            {
                string name = __originalMethod.DeclaringType.Name + "." + __originalMethod.Name;
                calls.TryGetValue(name, out int n);
                calls[name] = ++n;
                if (n > 40) return;
                List<string> args = new List<string>();
                foreach (object a in __args ?? new object[0])
                {
                    UnityEngine.Object o = a as UnityEngine.Object;
                    args.Add(o != null ? o.name : a == null ? "null" : a.ToString());
                }
                TogetherRuntime.Log("MT-TRACE " + name + "(" + string.Join(", ", args) + ") #" + n);
            }
            catch { }
        }
    }
}
