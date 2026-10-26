using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Which interactions put an item in the player's hand, read from the game's own wiring instead of per-case rules.
    // MiSide attaches a held item with Transform_Magnet (ReMagnerAndParentPlayerRI/LI..., or a magnet aimed at the player)
    // called from the interaction's UnityEvents: the click, the animation's start/loop/mid-animation events, and whatever
    // components those call in turn. An interaction whose chain reaches such a call "holds an item": only the presser
    // performs it and everyone sees the item in their hand (CutsceneRules.ForInteraction). The others stay mirrored.
    public static class HeldItems
    {
        static readonly Dictionary<IntPtr, bool> cache = new Dictionary<IntPtr, bool>();
        static readonly Dictionary<string, Type> types = new Dictionary<string, Type>();
        const int MaxDepth = 4;

        public static void Clear() { cache.Clear(); }

        // The click (ObjectInteractive) or the animation (ObjectAnimationPlayer) of an interaction, by scene key.
        public static bool Holds(string clickKey, string animationKey)
        {
            ObjectInteractive click = string.IsNullOrEmpty(clickKey) ? null : SceneKeys.Find<ObjectInteractive>(clickKey);
            if (click != null && Holds(click)) return true;
            ObjectAnimationPlayer anim = string.IsNullOrEmpty(animationKey) ? null : SceneKeys.Find<ObjectAnimationPlayer>(animationKey);
            return anim != null && Holds(anim);
        }

        public static bool Holds(Component c)
        {
            if (c == null) return false;
            bool known;
            if (cache.TryGetValue(c.Pointer, out known)) return known;
            cache[c.Pointer] = false;   // cycles end here
            bool holds = false;
            try { holds = WalkComponent(c, 0, new HashSet<IntPtr>()); }
            catch (Exception ex) { TogetherRuntime.Log("Held-item check failed on " + c.name + ": " + ex.Message); }
            cache[c.Pointer] = holds;
            return holds;
        }

        static bool WalkComponent(Component c, int depth, HashSet<IntPtr> seen)
        {
            if (c == null || depth > MaxDepth || !seen.Add(c.Pointer)) return false;
            ObjectInteractive click = c.TryCast<ObjectInteractive>();
            if (click != null) return WalkEvent(click.eventClick, depth, seen);
            ObjectAnimationPlayer anim = c.TryCast<ObjectAnimationPlayer>();
            if (anim != null)
            {
                if (WalkEvent(anim.eventStartAnimaiton, depth, seen) || WalkEvent(anim.eventStartLoop, depth, seen)) return true;
                if (anim.eventsPlayer != null) foreach (UnityEvent e in anim.eventsPlayer) if (WalkEvent(e, depth, seen)) return true;
                return false;
            }
            // Any other game component: every UnityEvent it exposes (timed events, event hubs, quest steps...).
            foreach (UnityEventBase e in EventsOf(c)) if (WalkEvent(e, depth, seen)) return true;
            return false;
        }

        static bool WalkEvent(UnityEventBase e, int depth, HashSet<IntPtr> seen)
        {
            if (e == null) return false;
            int count = e.GetPersistentEventCount();
            for (int i = 0; i < count; i++)
            {
                Object target = e.GetPersistentTarget(i);
                string method = e.GetPersistentMethodName(i) ?? "";
                if (target == null) continue;
                Transform_Magnet magnet = target.TryCast<Transform_Magnet>();
                if (magnet != null)
                {
                    if (method.StartsWith("ReMagnerAndParentPlayer", StringComparison.Ordinal)) return true;
                    if ((magnet.playerRoot || !string.IsNullOrEmpty(magnet.magnetPlayer)) && (method.StartsWith("Activation", StringComparison.Ordinal) || method.Contains("Magnet"))) return true;
                    continue;
                }
                Component next = target.TryCast<Component>();
                if (next != null && WalkComponent(next, depth + 1, seen)) return true;
                // An object switched on can start its own events (Time_Events, OnEnable players).
                GameObject go = target.TryCast<GameObject>();
                if (go != null && method == "SetActive" && depth + 1 <= MaxDepth)
                    foreach (MonoBehaviour m in go.GetComponents<MonoBehaviour>()) if (m != null && WalkComponent(m, depth + 1, seen)) return true;
            }
            return false;
        }

        // The UnityEvent fields of a game component, through its interop class (properties typed UnityEvent).
        static IEnumerable<UnityEventBase> EventsOf(Component c)
        {
            string name = c.GetIl2CppType().Name;
            Type t;
            if (!types.TryGetValue(name, out t))
            {
                Assembly game = typeof(ObjectInteractive).Assembly;
                string ns = typeof(ObjectInteractive).Namespace;
                t = game.GetType(string.IsNullOrEmpty(ns) ? name : ns + "." + name);
                types[name] = t;
            }
            if (t == null) yield break;
            object typed = null;
            try { typed = Activator.CreateInstance(t, c.Pointer); } catch { }
            if (typed == null) yield break;
            foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!typeof(UnityEventBase).IsAssignableFrom(p.PropertyType)) continue;
                UnityEventBase e = null;
                try { e = p.GetValue(typed) as UnityEventBase; } catch { }
                if (e != null) yield return e;
            }
        }
    }
}
