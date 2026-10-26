using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace MiSideTogether
{
    [BepInPlugin("local.misidetogether.multiplayer", "MiSide Together", TogetherRuntime.ModVersion)]
    public sealed class BepInExEntry : BasePlugin
    {
        public override void Load()
        {
            TogetherRuntime.Log = message => Log.LogInfo(message);
            // Lab control runs (-mt-nopatch): the mod without any game hook, to tell game-side failures from the mod's.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-mt-nopatch") < 0) ModHooks.InstallPatches();
            // Registers TogetherBehaviour in IL2CPP and hosts it on a hidden DontDestroyOnLoad object.
            AddComponent<TogetherBehaviour>();
        }
    }
}
