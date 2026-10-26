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

        public static void BeginSeeded(Component c)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || c == null) return;
            seedDepth++;
            string key = SceneKeys.Key(c);
            int n;
            rt.seededCalls.TryGetValue(key, out n);
            rt.seededCalls[key] = n + 1;
            unchecked { UnityEngine.Random.InitState(Wire.Hash(key) * 31 + n * 7919); }
            seededRolls++;
        }

        public static void EndSeeded()
        {
            if (seedDepth == 0) return;
            if (--seedDepth == 0) UnityEngine.Random.InitState(Environment.TickCount);
        }
    }
}
