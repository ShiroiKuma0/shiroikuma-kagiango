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

using Android.Content;
using AndroidX.Preference;

namespace keepass2android.Theming
{
    /// <summary>
    /// Reads/writes the 白い熊 鍵暗号 UI overrides in the app's default SharedPreferences
    /// (the same store used by the rest of the settings — AndroidX and the legacy
    /// PreferenceManager share the "&lt;package&gt;_preferences" file).
    /// </summary>
    public static class ThemeConfig
    {
        private static ISharedPreferences Prefs(Context ctx) =>
            PreferenceManager.GetDefaultSharedPreferences(ctx);

        // --- colours (per-slot ARGB int, ThemeUnset = no override) ---------------------------------
        public static int GetColor(Context ctx, string key) =>
            Prefs(ctx).GetInt("theme_color_" + key, ThemeColors.ThemeUnset);

        public static bool HasColor(Context ctx, string key) =>
            GetColor(ctx, key) != ThemeColors.ThemeUnset;

        public static void SetColor(Context ctx, string key, int argb) =>
            Prefs(ctx).Edit().PutInt("theme_color_" + key, argb).Apply();

        public static void ClearColor(Context ctx, string key) =>
            Prefs(ctx).Edit().Remove("theme_color_" + key).Apply();

        // --- per-slot fonts ------------------------------------------------------------------------
        // family: "" = system/global default, "@monospace" = monospace, else an imported file name.
        public static string GetFontFamily(Context ctx, string key) =>
            Prefs(ctx).GetString("font_family_" + key, "");

        public static void SetFontFamily(Context ctx, string key, string value) =>
            Prefs(ctx).Edit().PutString("font_family_" + key, value ?? "").Apply();

        // weight: 0 = default, else 100..900.
        public static int GetFontWeight(Context ctx, string key) =>
            Prefs(ctx).GetInt("font_weight_" + key, 0);

        public static void SetFontWeight(Context ctx, string key, int value) =>
            Prefs(ctx).Edit().PutInt("font_weight_" + key, value).Apply();

        // size in sp: 0 = default.
        public static int GetFontSizeSp(Context ctx, string key) =>
            Prefs(ctx).GetInt("font_size_" + key, 0);

        public static void SetFontSizeSp(Context ctx, string key, int value) =>
            Prefs(ctx).Edit().PutInt("font_size_" + key, value).Apply();
    }
}
