// This file is part of the 白い熊 鍵暗号 fork of Keepass2Android.
//
//   Keepass2Android is free software: you can redistribute it and/or modify
//   it under the terms of the GNU General Public License as published by
//   the Free Software Foundation, either version 3 of the License, or
//   (at your option) any later version.
//
//   Keepass2Android is distributed in the hope that it will be useful,
//   but WITHOUT ANY WARRANTY; without even the implied warranty of
//   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//   GNU General Public License for more details.
//
//   You should have received a copy of the GNU General Public License
//   along with Keepass2Android.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Linq;
using System.Reflection;
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Preference;

namespace keepass2android
{
    /// <summary>
    /// Fork diagnostic (UI page → Diagnostics → Leak trace, off by default): finds screens that stay in
    /// memory after they were destroyed, and what holds them. EMUI hides the app's own logcat lines and
    /// refuses a heap dump of a release build, so this keeps a weak reference to every activity and, a
    /// few seconds after one is destroyed, forces a full managed + Java collection. A destroyed activity
    /// that survives is reported once with the chain of fields leading to it, found by walking the
    /// managed object graph from every static field and from the live activities. A leak with no such
    /// chain is held from the Java side (or by a C# callback that Java holds).
    /// Writes &lt;external-files&gt;/diag/kagiango-leaks_&lt;yyyy-MM-dd&gt;.txt.
    /// </summary>
    public class LeakTracer : Java.Lang.Object, Application.IActivityLifecycleCallbacks
    {
        public const string PrefKey = "diag_leaks";

        private const long CheckDelayMs = 5000;
        private const long MaxDayBytes = 8 * 1024 * 1024;
        private const int MaxNodes = 600_000;
        private const long MaxWalkMs = 4000;

        private class Tracked
        {
            public WeakReference<Activity> Ref;
            public string Name;
            public DateTime Created;
            public DateTime? Destroyed;
            public bool Reported;
        }

        private static LeakTracer _instance;

        private readonly Application _app;
        private readonly Handler _handler = new Handler(Looper.MainLooper);
        private readonly List<Tracked> _tracked = new List<Tracked>();
        private int _serial;
        private bool _checkPending;

        private LeakTracer(Application app)
        {
            _app = app;
        }

        public static void Install(Application app)
        {
            try
            {
                _instance = new LeakTracer(app);
                app.RegisterActivityLifecycleCallbacks(_instance);
                MigrateOldTrace(app);
            }
            catch (Exception e)
            {
                Kp2aLog.Log("LeakTracer: install failed: " + e);
            }
        }

        public static bool IsEnabled(Context ctx) =>
            PreferenceManager.GetDefaultSharedPreferences(ctx).GetBoolean(PrefKey, false);

        public static void SetEnabled(Context ctx, bool value)
        {
            PreferenceManager.GetDefaultSharedPreferences(ctx).Edit().PutBoolean(PrefKey, value).Apply();
            if (value)
            {
                string version = "?";
                try
                {
                    version = ctx.PackageManager.GetPackageInfo(ctx.PackageName, 0).VersionName;
                }
                catch (Exception) { }
                _instance?.Write("diag leaks on, " + version);
            }
        }

        public static string DiagDir(Context ctx)
        {
            string root = ctx.GetExternalFilesDir(null)?.AbsolutePath;
            return root == null ? null : System.IO.Path.Combine(root, "diag");
        }

        /// <summary>The 1.15-r3+017 trace sat at the files root as leak-trace.txt; move it in.</summary>
        private static void MigrateOldTrace(Context ctx)
        {
            try
            {
                string root = ctx.GetExternalFilesDir(null)?.AbsolutePath;
                string dir = DiagDir(ctx);
                if (root == null || dir == null)
                    return;
                foreach (string name in new[] { "leak-trace.txt.1", "leak-trace.txt" })
                {
                    string old = System.IO.Path.Combine(root, name);
                    if (!System.IO.File.Exists(old))
                        continue;
                    System.IO.Directory.CreateDirectory(dir);
                    string day = System.IO.File.GetLastWriteTime(old).ToString("yyyy-MM-dd");
                    string target = System.IO.Path.Combine(dir, "kagiango-leaks_" + day + ".txt");
                    string text = System.IO.File.ReadAllText(old);
                    if (System.IO.File.Exists(target))
                        text += System.IO.File.ReadAllText(target);
                    System.IO.File.WriteAllText(target, text);
                    System.IO.File.Delete(old);
                }
            }
            catch (Exception e)
            {
                Kp2aLog.Log("LeakTracer: migration failed: " + e);
            }
        }

        public void OnActivityCreated(Activity activity, Bundle savedInstanceState)
        {
            _tracked.Add(new Tracked
            {
                Ref = new WeakReference<Activity>(activity),
                Name = activity.GetType().Name + "#" + (++_serial),
                Created = DateTime.Now
            });
        }

        public void OnActivityDestroyed(Activity activity)
        {
            foreach (var t in _tracked)
            {
                if (t.Destroyed == null && t.Ref.TryGetTarget(out var a) && ReferenceEquals(a, activity))
                {
                    t.Destroyed = DateTime.Now;
                    break;
                }
            }
            if (!_checkPending && IsEnabled(_app))
            {
                _checkPending = true;
                _handler.PostDelayed(Check, CheckDelayMs);
            }
        }

        private void Check()
        {
            _checkPending = false;
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Java.Lang.Runtime.GetRuntime()?.Gc();
                }
                GC.Collect();

                _tracked.RemoveAll(t => !t.Ref.TryGetTarget(out _));
                var now = DateTime.Now;
                var leaked = _tracked
                    .Where(t => t.Destroyed != null && (now - t.Destroyed.Value).TotalMilliseconds >= CheckDelayMs - 500)
                    .ToList();
                var alive = _tracked.Where(t => t.Destroyed == null).Select(t => t.Name);

                Write("live=[" + string.Join(", ", alive) + "]"
                      + " leaked=" + leaked.Count
                      + (leaked.Any()
                          ? " [" + string.Join(", ", leaked.Select(t =>
                              t.Name + " created " + t.Created.ToString("HH:mm:ss") +
                              " destroyed " + t.Destroyed.Value.ToString("HH:mm:ss"))) + "]"
                          : ""));

                var fresh = leaked.Where(t => !t.Reported).ToList();
                if (fresh.Any())
                {
                    foreach (string line in FindHolders(fresh))
                        Write("  " + line);
                    foreach (var t in fresh)
                        t.Reported = true;
                }
            }
            catch (Exception e)
            {
                Kp2aLog.Log("LeakTracer: check failed: " + e);
            }
        }

        // ---- holder search ----------------------------------------------------------------------

        private sealed class Edge
        {
            public object Parent;
            public string Name;
        }

        private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new Dictionary<Type, FieldInfo[]>();

        private static FieldInfo[] InstanceFields(Type t)
        {
            if (FieldCache.TryGetValue(t, out var f))
                return f;
            var list = new List<FieldInfo>();
            for (Type c = t; c != null && c != typeof(object); c = c.BaseType)
            {
                foreach (var fi in c.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                               BindingFlags.DeclaredOnly))
                {
                    if (!IsLeaf(fi.FieldType))
                        list.Add(fi);
                }
            }
            f = list.ToArray();
            FieldCache[t] = f;
            return f;
        }

        private static bool IsLeaf(Type t) =>
            t.IsPrimitive || t.IsEnum || t.IsPointer || t == typeof(string) || t == typeof(IntPtr) ||
            t == typeof(UIntPtr) || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(TimeSpan) ||
            t == typeof(Guid) || typeof(MemberInfo).IsAssignableFrom(t) || typeof(Assembly).IsAssignableFrom(t) ||
            (t.IsArray && IsLeaf(t.GetElementType()));

        private static bool SkipDescending(object o)
        {
            string ns = o.GetType().Namespace ?? "";
            // the database model holds thousands of objects and never an activity
            return ns.StartsWith("KeePassLib") || o is System.Threading.Thread || o is Type;
        }

        private IEnumerable<string> FindHolders(List<Tracked> targets)
        {
            var targetNames = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
            foreach (var t in targets)
                if (t.Ref.TryGetTarget(out var a))
                    targetNames[a] = t.Name;

            var seen = new Dictionary<object, Edge>(ReferenceEqualityComparer.Instance);
            var queue = new Queue<object>();
            var found = new Dictionary<object, bool>(ReferenceEqualityComparer.Instance);
            var sw = Stopwatch.StartNew();

            void Enqueue(object child, object parent, string name)
            {
                if (child == null || seen.ContainsKey(child) || IsLeaf(child.GetType()))
                    return;
                seen[child] = new Edge { Parent = parent, Name = name };
                queue.Enqueue(child);
            }

            // roots 1: every static field of the app's own assemblies
            var roots = new object();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string an = asm.GetName().Name ?? "";
                if (!(an.StartsWith("keepass2android", StringComparison.OrdinalIgnoreCase) ||
                      an.StartsWith("Kp2a", StringComparison.OrdinalIgnoreCase)))
                    continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(x => x != null).ToArray(); }
                foreach (var type in types)
                {
                    if (type.ContainsGenericParameters)
                        continue;
                    FieldInfo[] statics;
                    try
                    {
                        statics = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                                                 BindingFlags.DeclaredOnly);
                    }
                    catch (Exception) { continue; }
                    foreach (var fi in statics)
                    {
                        if (fi.IsLiteral || IsLeaf(fi.FieldType))
                            continue;
                        try { Enqueue(fi.GetValue(null), roots, "static " + type.Name + "." + fi.Name); }
                        catch (Exception) { }
                    }
                }
            }
            // roots 2: the activities that are still alive
            foreach (var t in _tracked.Where(x => x.Destroyed == null))
                if (t.Ref.TryGetTarget(out var a))
                    Enqueue(a, roots, "live " + t.Name);

            int nodes = 0;
            bool truncated = false;
            while (queue.Count > 0)
            {
                if (++nodes > MaxNodes || sw.ElapsedMilliseconds > MaxWalkMs)
                {
                    truncated = true;
                    break;
                }
                object o = queue.Dequeue();
                if (targetNames.ContainsKey(o))
                {
                    found[o] = true;
                    if (found.Count == targetNames.Count)
                        break;
                    // keep walking through it: a screen held only by another leaked screen shows that chain
                }
                if (SkipDescending(o))
                    continue;
                try
                {
                    if (o is Array arr)
                    {
                        int i = 0;
                        foreach (object el in arr)
                        {
                            if (el != null)
                                Enqueue(el, o, "[" + i + "]");
                            if (++i > 50_000) break;
                        }
                        continue;
                    }
                    foreach (var fi in InstanceFields(o.GetType()))
                    {
                        object v;
                        try { v = fi.GetValue(o); }
                        catch (Exception) { continue; }
                        Enqueue(v, o, fi.Name);
                    }
                }
                catch (Exception) { }
            }

            var lines = new List<string>();
            foreach (var kv in targetNames)
            {
                if (!found.ContainsKey(kv.Key))
                {
                    lines.Add(kv.Value + ": no managed path" + (truncated ? " (search truncated)" : "")
                              + " — held from the Java side or by a C# callback Java holds");
                    continue;
                }
                var chain = new List<string>();
                object cur = kv.Key;
                while (seen.TryGetValue(cur, out var edge) && edge.Parent != roots)
                {
                    chain.Add("." + edge.Name + " (" + ShortType(edge.Parent) + ")");
                    cur = edge.Parent;
                    if (chain.Count > 40) break;
                }
                string rootName = seen.TryGetValue(cur, out var re) ? re.Name : "?";
                chain.Reverse();
                lines.Add(kv.Value + " held by: " + rootName + " (" + ShortType(cur) + ") " + string.Join(" ", chain));
            }
            lines.Add("search: " + nodes + " objects, " + sw.ElapsedMilliseconds + " ms" + (truncated ? ", truncated" : ""));
            return lines;
        }

        private static string ShortType(object o)
        {
            if (o == null) return "null";
            Type t = o.GetType();
            string n = t.Name;
            if (t.DeclaringType != null) n = t.DeclaringType.Name + "+" + n;
            return n;
        }

        // ---- output -----------------------------------------------------------------------------

        private void Write(string line)
        {
            try
            {
                string dir = DiagDir(_app);
                if (dir == null)
                    return;
                System.IO.Directory.CreateDirectory(dir);
                var now = DateTime.Now;
                string path = System.IO.Path.Combine(dir, "kagiango-leaks_" + now.ToString("yyyy-MM-dd") + ".txt");
                var info = new System.IO.FileInfo(path);
                if (info.Exists && info.Length > MaxDayBytes)
                    return;
                System.IO.File.AppendAllText(path, now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + "\n");
            }
            catch (Exception)
            {
                // diagnostics must never break the app
            }
        }

        public void OnActivityPaused(Activity activity) { }
        public void OnActivityResumed(Activity activity) { }
        public void OnActivitySaveInstanceState(Activity activity, Bundle outState) { }
        public void OnActivityStarted(Activity activity) { }
        public void OnActivityStopped(Activity activity) { }
    }
}
