using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiSideTogether
{
    // Shared randomness: the game's random placement, materials, idle animations and random dialogue lines run with a
    // seed derived from the scene, the object's scene key and how many times that object has rolled, so every machine
    // rolls the same result without any network traffic. Afterwards Unity's generator is re-seeded from the clock.
    public sealed partial class TogetherRuntime
    {
        readonly Dictionary<string, int> seededCalls = new Dictionary<string, int>();
        public static int seededRolls;
        static int seedDepth;

        // count false: a roll that depends on how far the counted rolls have got, not on how often it is asked.
        public static void BeginSeeded(Component c, string salt = "", bool count = true)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || c == null) return;
            seedDepth++;
            string key = SceneKeys.Key(c) + salt;
            int n;
            rt.seededCalls.TryGetValue(key, out n);
            if (count) rt.seededCalls[key] = n + 1;
            unchecked { UnityEngine.Random.InitState(Wire.Hash(key) * 31 + n * 7919); }
            seededRolls++;
        }

        // Scene 20's corridor is rebuilt after a death. The players had built different amounts of the old one, so every
        // count starts again, from a place no earlier attempt used.
        public static void CorridorRestart(Component c)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || c == null) return;
            string key = SceneKeys.Key(c);
            int restarts;
            rt.seededCalls.TryGetValue(key + "#restart", out restarts);
            rt.seededCalls[key + "#part"] = rt.seededCalls[key + "#turn"] = (restarts + 1) * 100000;
            BeginSeeded(c, "#restart");
        }

        public static void EndSeeded()
        {
            if (seedDepth == 0) return;
            if (--seedDepth == 0) UnityEngine.Random.InitState(Environment.TickCount);
        }
    }
}
