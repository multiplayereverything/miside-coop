using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab-only, with -mt-dump: every persistent UnityEvent call in the scene, one line each
    // ("source [Type].field[i] -> target [Type].Method"), so a story gate can be traced backwards from what it switches on.
    // Covers UnityEvent properties, arrays and lists of them, and arrays of game structs that hold them (Time_Events.TimePoint).
    public sealed partial class TogetherRuntime
    {
        static readonly Dictionary<string, Type> wireTypes = new Dictionary<string, Type>();

        static void DumpWires(StringBuilder text)
        {
            foreach (MonoBehaviour m in Object.FindObjectsOfType<MonoBehaviour>(true))
            {
                if (m == null) continue;
                object typed = Typed(m);
                if (typed == null) continue;
                try { WireProps(typed, PathOf(m.transform) + " [" + m.GetIl2CppType().Name + "]", text, 0); }
                catch (Exception ex) { text.Append("! ").Append(PathOf(m.transform)).Append(": ").Append(ex.Message).Append('\n'); }
            }
        }

        static object Typed(Component c)
        {
            string name = c.GetIl2CppType().Name;
            Type t;
            if (!wireTypes.TryGetValue(name, out t))
            {
                string ns = typeof(ObjectInteractive).Namespace;
                t = typeof(ObjectInteractive).Assembly.GetType(string.IsNullOrEmpty(ns) ? name : ns + "." + name);
                wireTypes[name] = t;
            }
            if (t == null) return null;
            try { return Activator.CreateInstance(t, c.Pointer); } catch { return null; }
        }

        static void WireProps(object o, string where, StringBuilder text, int depth)
        {
            if (o == null || depth > 2) return;
            foreach (PropertyInfo p in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Type pt = p.PropertyType;
                bool events = typeof(UnityEventBase).IsAssignableFrom(pt);
                bool many = pt.IsGenericType && (pt.Name.StartsWith("Il2CppReferenceArray") || pt.Name.StartsWith("List"));
                if (!events && !many) continue;
                object v = null;
                try { v = p.GetValue(o); } catch { }
                if (v == null) continue;
                if (events) { WireEvent((UnityEventBase)v, where + "." + p.Name, text); continue; }
                Type item = pt.GetGenericArguments()[0];
                if (item.IsValueType || item == typeof(string) || typeof(Object).IsAssignableFrom(item)) continue;
                int n = 0;
                PropertyInfo count = v.GetType().GetProperty("Count") ?? v.GetType().GetProperty("Length");
                MethodInfo get = v.GetType().GetMethod("get_Item", new[] { typeof(int) });
                if (count == null || get == null) continue;
                try { n = Convert.ToInt32(count.GetValue(v)); } catch { continue; }
                for (int i = 0; i < n && i < 200; i++)
                {
                    object e = null;
                    try { e = get.Invoke(v, new object[] { i }); } catch { }
                    if (e == null) continue;
                    UnityEventBase ue = e as UnityEventBase;
                    if (ue != null) WireEvent(ue, where + "." + p.Name + "[" + i + "]", text);
                    else WireProps(e, where + "." + p.Name + "[" + i + "]", text, depth + 1);
                }
            }
        }

        static void WireEvent(UnityEventBase e, string where, StringBuilder text)
        {
            int count = e.GetPersistentEventCount();
            for (int i = 0; i < count; i++)
            {
                Object target = e.GetPersistentTarget(i);
                string t = target == null ? "null" : (target.TryCast<Component>() is Component c ? PathOf(c.transform) : target.TryCast<GameObject>() is GameObject g ? PathOf(g.transform) : target.name) + " [" + target.GetIl2CppType().Name + "]";
                text.Append(where).Append(" -> ").Append(t).Append('.').Append(e.GetPersistentMethodName(i)).Append('\n');
            }
        }
    }
}
