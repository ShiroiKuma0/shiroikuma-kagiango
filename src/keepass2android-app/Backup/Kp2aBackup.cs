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
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Android.Content;
using AndroidX.Preference;
using keepass2android.Theming;
using Org.Json;

namespace keepass2android.Backup
{
    /// <summary>Outcome of an export or import: a human line per part that worked, plus per-part errors.</summary>
    public sealed class BackupResult
    {
        public readonly List<string> Lines = new List<string>();
        public readonly List<string> Errors = new List<string>();
        public int CategoryCount;
        public bool Ok => Errors.Count == 0;

        /// <summary>Set when the run was stopped before it was whole — nothing was left on disk.</summary>
        public bool Cancelled;

        /// <summary>Where the finished ZIP landed. Null when the run was cancelled or wrote no file.</summary>
        public string Path;

        /// <summary>Size of <see cref="Path"/> in bytes.</summary>
        public long Size;
    }

    /// <summary>
    /// The modular export / import engine. An export is a SINGLE ZIP: one <c>&lt;id&gt;.json</c> per
    /// selected <see cref="BackupCategory"/> plus a <c>manifest.json</c> (and the imported font files
    /// under <c>fonts/</c> when that sub-option is selected). Import reads the ZIP and applies each
    /// selected category that is present, <b>merging</b> per key rather than wiping, so a restore never
    /// destroys settings a category didn't cover and re-importing the same file is idempotent.
    /// <para>
    /// Each category is isolated: one failing part is reported but never aborts the others. The panel
    /// (<see cref="ExportImportDialog"/>) and the headless automation receiver
    /// (<see cref="StateExportReceiver"/>) are the two thin callers of this one engine — the export logic
    /// exists exactly once.
    /// </para>
    /// <para>
    /// What may travel is allow-listed by <see cref="BackupCategory.ResolveKeys"/> and filtered again by
    /// <see cref="BackupCategory.IsForbiddenKey"/>, on export <b>and</b> on import: no database, no master
    /// password, no biometric-wrapped credential and no remote-storage login ever enters a backup, and a
    /// doctored ZIP cannot inject one back.
    /// </para>
    /// </summary>
    public static class Kp2aBackup
    {
        public const string Format = "kagiango-backup";
        public const int FormatVersion = 1;

        /// <summary>
        /// The family file-name convention (白い熊, 2026-07-25): every sister app writes
        /// <c>&lt;english-app-name&gt;_&lt;yyyy-MM-dd_HH-mm-ss&gt;.zip</c> — no version, no infix, no
        /// suffix — because all apps' backups share one folder and must sort and read uniformly.
        /// </summary>
        public const string ExportPrefix = "shiroikuma-kagiango_";

        private const string ManifestEntry = "manifest.json";
        private const string FontsEntryDir = "fonts/";

        /// <summary>
        /// What a half-written archive is called until it is whole. It deliberately falls outside
        /// <see cref="IsBackupFileName"/>, so a partial can never be offered for import.
        /// </summary>
        private const string PartSuffix = ".part";

        public static string ExportFileName(DateTime now) =>
            ExportPrefix + now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + ".zip";

        public static bool IsBackupFileName(string name) =>
            name != null && name.StartsWith(ExportPrefix, StringComparison.Ordinal) &&
            name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        /// <summary>Our backups in <paramref name="dir"/>, newest first.</summary>
        public static List<FileInfo> ListBackups(string dir)
        {
            var result = new List<FileInfo>();
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return result;
                // Filtered by our own prefix: 白い熊 keeps every sister app's backups in one folder, so an
                // unfiltered *.zip scan would offer their files too.
                result.AddRange(new DirectoryInfo(dir).GetFiles()
                    .Where(f => IsBackupFileName(f.Name))
                    .OrderByDescending(f => f.LastWriteTimeUtc));
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Backup: listing " + dir + " failed: " + e);
            }
            return result;
        }

        public static string HumanSize(long bytes)
        {
            if (bytes < 1024)
                return bytes + " B";
            string[] units = { "KB", "MB", "GB", "TB" };
            double value = bytes / 1024.0;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return value.ToString(value >= 100 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + units[unit];
        }

        // ---- export ----------------------------------------------------------------------------------

        /// <summary>
        /// Write one backup into <paramref name="dir"/> — the single entry point both callers use, so the
        /// partial-file discipline exists exactly once.
        /// <para>
        /// The archive is built under <c>&lt;name&gt;.part</c> and renamed only once it is whole. A run that
        /// is cancelled or throws therefore leaves the backup folder <b>exactly as it found it</b>: no short
        /// archive, no stray partial. On success <see cref="BackupResult.Path"/> and
        /// <see cref="BackupResult.Size"/> describe the finished file; on cancel
        /// <see cref="BackupResult.Cancelled"/> is set and <c>Path</c> stays null.
        /// </para>
        /// </summary>
        public static BackupResult ExportToDirectory(Context ctx, ICollection<string> categoryIds, string dir,
            Action<int, int, string> onProgress = null, Func<bool> isCancelled = null)
        {
            Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, ExportFileName(DateTime.Now));
            string partPath = path + PartSuffix;

            BackupResult result;
            try
            {
                using (var stream = File.Create(partPath))
                    result = Export(ctx, categoryIds, stream, onProgress, isCancelled);

                // Checked once more after the loop: a cancel that lands while the manifest is being written
                // must still stop the file from being published under its final name.
                if (result.Cancelled || (isCancelled != null && isCancelled()))
                {
                    result.Cancelled = true;
                    return result;
                }

                File.Move(partPath, path, true);
                result.Path = path;
                result.Size = new FileInfo(path).Length;
                return result;
            }
            finally
            {
                // Cancel, exception or success alike — nothing partial may survive in the backup folder.
                TryDelete(partPath);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Backup: could not remove " + path + ": " + e);
            }
        }

        /// <summary>
        /// Write the selected <paramref name="categoryIds"/> to <paramref name="output"/> as one backup ZIP.
        /// <paramref name="onProgress"/> is called after each category with (done, total, label) — the
        /// headless path turns those into the contract's real-count progress broadcasts.
        /// <paramref name="isCancelled"/> is polled <b>between</b> categories: the loop unwinds at the next
        /// entry boundary, never mid-<c>Write</c>, and never by killing a thread or the process.
        /// </summary>
        public static BackupResult Export(Context ctx, ICollection<string> categoryIds, Stream output,
            Action<int, int, string> onProgress = null, Func<bool> isCancelled = null)
        {
            var result = new BackupResult();
            var selected = BackupCategory.All.Where(c => categoryIds.Contains(c.Id)).ToList();
            var written = new List<string>();
            int total = selected.Count;
            int done = 0;

            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                foreach (var category in selected)
                {
                    if (isCancelled != null && isCancelled())
                    {
                        result.Cancelled = true;
                        break;
                    }
                    string label = ctx.GetString(category.LabelRes);
                    try
                    {
                        int count = category.IsFiles
                            ? WriteFiles(ctx, zip, category)
                            : WritePrefs(ctx, zip, category);
                        written.Add(category.Id);
                        result.Lines.Add(label + " — " + count);
                    }
                    catch (Exception e)
                    {
                        Kp2aLog.Log("Backup: export of " + category.Id + " failed: " + e);
                        result.Errors.Add(label);
                    }
                    done++;
                    onProgress?.Invoke(done, total, label);
                }

                // A cancelled run gets no manifest: the file is about to be deleted, and nothing
                // half-described should ever be capable of reading as a finished backup.
                if (result.Cancelled)
                {
                    result.CategoryCount = written.Count;
                    return result;
                }

                var manifest = new JSONObject();
                manifest.Put("format", Format);
                manifest.Put("version", FormatVersion);
                manifest.Put("app", ctx.PackageName);
                manifest.Put("appVersion", AppVersionName(ctx));
                manifest.Put("createdTs", Java.Lang.JavaSystem.CurrentTimeMillis());
                var cats = new JSONArray();
                foreach (string id in written)
                    cats.Put(id);
                manifest.Put("categories", cats);
                WriteEntry(zip, ManifestEntry, Encoding.UTF8.GetBytes(manifest.ToString()));
            }

            result.CategoryCount = written.Count;
            return result;
        }

        /// <summary>Dump this category's allow-listed preference keys as a type-tagged JSON entry.</summary>
        private static int WritePrefs(Context ctx, ZipArchive zip, BackupCategory category)
        {
            var prefs = PreferenceManager.GetDefaultSharedPreferences(ctx);
            var resolved = category.ResolveKeys(ctx);
            var entries = new JSONArray();
            int count = 0;

            foreach (var kv in prefs.All)
            {
                if (kv.Key == null || !category.Owns(ctx, kv.Key, resolved))
                    continue;
                var entry = TypedEntry(kv.Key, kv.Value);
                if (entry == null)
                    continue;
                entries.Put(entry);
                count++;
            }

            var payload = new JSONObject();
            payload.Put("format", Format);
            payload.Put("version", FormatVersion);
            payload.Put("category", category.Id);
            payload.Put("entries", entries);
            WriteEntry(zip, category.Id + ".json", Encoding.UTF8.GetBytes(payload.ToString()));
            return count;
        }

        /// <summary>Copy the fonts 白い熊 imported on the UI page into the archive, bytes and all.</summary>
        private static int WriteFiles(Context ctx, ZipArchive zip, BackupCategory category)
        {
            int count = 0;
            var dir = FontManager.FontsDir(ctx);
            var files = dir?.ListFiles();
            if (files == null)
                return 0;
            foreach (var file in files)
            {
                if (!file.IsFile)
                    continue;
                WriteEntry(zip, FontsEntryDir + file.Name, File.ReadAllBytes(file.AbsolutePath));
                count++;
            }
            return count;
        }

        private static void WriteEntry(ZipArchive zip, string name, byte[] payload)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var stream = entry.Open())
                stream.Write(payload, 0, payload.Length);
        }

        private static JSONObject TypedEntry(string key, object value)
        {
            var entry = new JSONObject();
            entry.Put("k", key);
            switch (value)
            {
                case bool b:
                    entry.Put("t", "b");
                    entry.Put("v", b);
                    return entry;
                case int i:
                    entry.Put("t", "i");
                    entry.Put("v", i);
                    return entry;
                case long l:
                    entry.Put("t", "l");
                    entry.Put("v", l);
                    return entry;
                case float f:
                    entry.Put("t", "f");
                    entry.Put("v", (double)f);
                    return entry;
                case string s:
                    entry.Put("t", "s");
                    entry.Put("v", s);
                    return entry;
                case ICollection<string> set:
                    entry.Put("t", "ss");
                    var array = new JSONArray();
                    foreach (string item in set)
                        array.Put(item);
                    entry.Put("v", array);
                    return entry;
                default:
                    return null;   // unknown preference type — never guess at it
            }
        }

        // ---- import ----------------------------------------------------------------------------------

        /// <summary>
        /// Apply the selected <paramref name="categoryIds"/> from a backup ZIP. Categories absent from the
        /// archive are skipped silently; present ones are merged key by key.
        /// </summary>
        public static BackupResult Import(Context ctx, ICollection<string> categoryIds, Stream input)
        {
            var result = new BackupResult();
            using (var zip = new ZipArchive(input, ZipArchiveMode.Read))
            {
                foreach (var category in BackupCategory.All.Where(c => categoryIds.Contains(c.Id)))
                {
                    string label = ctx.GetString(category.LabelRes);
                    try
                    {
                        int count = category.IsFiles
                            ? ReadFiles(ctx, zip, category)
                            : ReadPrefs(ctx, zip, category);
                        if (count < 0)
                            continue;               // not in this archive
                        result.Lines.Add(label + " — " + count);
                        result.CategoryCount++;
                    }
                    catch (Exception e)
                    {
                        Kp2aLog.Log("Backup: import of " + category.Id + " failed: " + e);
                        result.Errors.Add(label);
                    }
                }
            }
            return result;
        }

        /// <summary>Merge one category's preferences. Returns -1 when the entry is absent.</summary>
        private static int ReadPrefs(Context ctx, ZipArchive zip, BackupCategory category)
        {
            var entry = zip.GetEntry(category.Id + ".json");
            if (entry == null)
                return -1;

            string json;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                json = reader.ReadToEnd();

            var payload = new JSONObject(json);
            var entries = payload.OptJSONArray("entries");
            if (entries == null)
                return 0;

            var resolved = category.ResolveKeys(ctx);
            var editor = PreferenceManager.GetDefaultSharedPreferences(ctx).Edit();
            int count = 0;

            for (int i = 0; i < entries.Length(); i++)
            {
                var item = entries.OptJSONObject(i);
                string key = item?.OptString("k", null);
                if (string.IsNullOrEmpty(key))
                    continue;
                // The same allow-list as the export: an archive may only restore keys this category owns,
                // so a hand-edited ZIP cannot smuggle a credential key back into the app's preferences.
                if (!category.Owns(ctx, key, resolved))
                {
                    Kp2aLog.Log("Backup: ignoring foreign key in " + category.Id + ": " + key);
                    continue;
                }
                switch (item.OptString("t", ""))
                {
                    case "b": editor.PutBoolean(key, item.OptBoolean("v", false)); break;
                    case "i": editor.PutInt(key, item.OptInt("v", 0)); break;
                    case "l": editor.PutLong(key, item.OptLong("v", 0)); break;
                    case "f": editor.PutFloat(key, (float)item.OptDouble("v", 0)); break;
                    case "s": editor.PutString(key, item.OptString("v", "")); break;
                    case "ss":
                        var array = item.OptJSONArray("v");
                        var set = new List<string>();
                        for (int j = 0; array != null && j < array.Length(); j++)
                            set.Add(array.OptString(j, ""));
                        editor.PutStringSet(key, set);
                        break;
                    default: continue;
                }
                count++;
            }
            editor.Commit();
            return count;
        }

        /// <summary>Restore imported font files. Returns -1 when the archive carries none.</summary>
        private static int ReadFiles(Context ctx, ZipArchive zip, BackupCategory category)
        {
            var fontEntries = zip.Entries
                .Where(e => e.FullName.StartsWith(FontsEntryDir, StringComparison.Ordinal) && e.Length > 0)
                .ToList();
            if (fontEntries.Count == 0)
                return -1;

            var dir = FontManager.FontsDir(ctx);
            int count = 0;
            foreach (var entry in fontEntries)
            {
                // Flatten to the bare file name: a ZIP entry must never be able to write outside the
                // fonts folder through a "../" path.
                string name = System.IO.Path.GetFileName(entry.FullName);
                if (string.IsNullOrEmpty(name))
                    continue;
                using (var source = entry.Open())
                using (var target = File.Create(System.IO.Path.Combine(dir.AbsolutePath, name)))
                    source.CopyTo(target);
                count++;
            }
            return count;
        }

        // ---- helpers ---------------------------------------------------------------------------------

        public static string AppVersionName(Context ctx)
        {
            try
            {
                return ctx.PackageManager.GetPackageInfo(ctx.PackageName, 0).VersionName ?? "0";
            }
            catch (Exception)
            {
                return "0";
            }
        }
    }
}
