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
using Android.Content;
using Android.Graphics;
using Android.OS;
using Uri = Android.Net.Uri;

namespace keepass2android.Theming
{
    public sealed class FontOption
    {
        public string Label { get; }
        public string Value { get; }   // "" = system, "@monospace" = monospace, else a file name
        public FontOption(string label, string value) { Label = label; Value = value; }
    }

    /// <summary>
    /// Imports external font files into the app's internal "fonts" directory, lists them, and
    /// builds (cached) Typefaces — including the per-glyph previews used by the font picker.
    /// </summary>
    public static class FontManager
    {
        public const string Monospace = "@monospace";
        private static readonly string[] FontExtensions = { ".ttf", ".otf" };
        private static readonly Dictionary<string, Typeface> Cache = new Dictionary<string, Typeface>();

        public static Java.IO.File FontsDir(Context ctx)
        {
            var dir = new Java.IO.File(ctx.FilesDir, "fonts");
            if (!dir.Exists())
                dir.Mkdirs();
            return dir;
        }

        /// <summary>Resolve a typeface for a family name (file/"@monospace"/"") at an optional weight.</summary>
        public static Typeface FontTypeface(Context ctx, string family, int weight)
        {
            Typeface baseFace;
            if (string.IsNullOrEmpty(family))
                baseFace = Typeface.Default;
            else if (family == Monospace)
                baseFace = Typeface.Monospace;
            else if (!Cache.TryGetValue(family, out baseFace))
            {
                try
                {
                    baseFace = Typeface.CreateFromFile(new Java.IO.File(FontsDir(ctx), family));
                }
                catch (Exception e)
                {
                    Kp2aLog.Log("Theme: font load failed for " + family + ": " + e);
                    baseFace = Typeface.Default;
                }
                Cache[family] = baseFace;
            }

            if (weight > 0)
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
                {
                    try { return Typeface.Create(baseFace, weight, false); }
                    catch { /* fall through */ }
                }
                else if (weight >= 600)
                {
                    return Typeface.Create(baseFace, TypefaceStyle.Bold);
                }
            }
            return baseFace ?? Typeface.Default;
        }

        public static IList<FontOption> AvailableFontOptions(Context ctx)
        {
            var list = new List<FontOption>
            {
                new FontOption(ctx.GetString(Resource.String.theme_font_system_default), ""),
                new FontOption(ctx.GetString(Resource.String.theme_font_monospace), Monospace),
            };

            var files = FontsDir(ctx).ListFiles();
            if (files != null)
            {
                Array.Sort(files, (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                foreach (var f in files)
                {
                    if (f.IsFile && HasFontExtension(f.Name))
                        list.Add(new FontOption(NameWithoutExtension(f.Name), f.Name));
                }
            }
            return list;
        }

        /// <summary>Copy a picked ttf/otf into the fonts dir. Returns the stored file name, or null.</summary>
        public static string ImportFont(Context ctx, Uri uri)
        {
            try
            {
                string name = SanitizeFileName(QueryDisplayName(ctx, uri) ?? uri.LastPathSegment ?? "font.ttf");
                if (!HasFontExtension(name))
                    return null;

                var target = new Java.IO.File(FontsDir(ctx), name);
                using (var input = ctx.ContentResolver.OpenInputStream(uri))
                using (var output = new System.IO.FileStream(target.AbsolutePath, System.IO.FileMode.Create))
                {
                    if (input == null)
                        return null;
                    input.CopyTo(output);
                }
                Cache.Remove(name);
                return name;
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Theme: ImportFont failed: " + e);
                return null;
            }
        }

        public static bool DeleteFont(Context ctx, string fileName)
        {
            try
            {
                Cache.Remove(fileName);
                var f = new Java.IO.File(FontsDir(ctx), fileName);
                return f.Exists() && f.Delete();
            }
            catch { return false; }
        }

        private static bool HasFontExtension(string name)
        {
            string lower = name.ToLowerInvariant();
            foreach (var e in FontExtensions)
                if (lower.EndsWith(e, StringComparison.Ordinal))
                    return true;
            return false;
        }

        private static string NameWithoutExtension(string name)
        {
            int dot = name.LastIndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Replace('/', '_').Replace('\\', '_');
        }

        private static string QueryDisplayName(Context ctx, Uri uri)
        {
            try
            {
                using var c = ctx.ContentResolver.Query(uri, null, null, null, null);
                if (c != null && c.MoveToFirst())
                {
                    int idx = c.GetColumnIndex("_display_name");
                    if (idx >= 0)
                        return c.GetString(idx);
                }
            }
            catch { /* fall through to last path segment */ }
            return null;
        }
    }
}
