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
    /// Seeds the signature 白い熊 palette: pure black (#000000) bodies/backgrounds and pure yellow
    /// (#FFFF00) text/accents/borders/icons. The palette has a version; bumping it re-applies the
    /// signature defaults on next launch (so changed defaults reach existing installs). Between
    /// version bumps, newly-added slots self-heal to their default without touching user choices.
    /// </summary>
    public static class ThemeSeeder
    {
        private const string SeededKey = "shiroikuma_palette_seeded";
        private const string VersionKey = "shiroikuma_palette_version";

        // Bump when the signature palette changes, to re-apply it to existing installs.
        private const int PaletteVersion = 2;

        private const int Black = unchecked((int)0xFF000000);   // #000000
        private const int Yellow = unchecked((int)0xFFFFFF00);  // #FFFF00

        public static void SeedSignaturePaletteIfFirstRun(Context ctx)
        {
            try
            {
                var prefs = PreferenceManager.GetDefaultSharedPreferences(ctx);
                bool firstRun = !prefs.GetBoolean(SeededKey, false);
                bool paletteChanged = prefs.GetInt(VersionKey, 0) < PaletteVersion;

                // First run or a new signature palette -> set everything; otherwise top up unset slots only.
                ApplyPalette(ctx, force: firstRun || paletteChanged);

                prefs.Edit()
                    .PutBoolean(SeededKey, true)
                    .PutInt(VersionKey, PaletteVersion)
                    .Apply();
            }
            catch (System.Exception e)
            {
                Kp2aLog.Log("Theme: seeding failed: " + e);
            }
        }

        /// <summary>Force the full signature palette (the page's "reset to signature palette" action).</summary>
        public static void ApplySignaturePalette(Context ctx) => ApplyPalette(ctx, force: true);

        private static void ApplyPalette(Context ctx, bool force)
        {
            // Backgrounds / bodies -> pure black.
            SetIf(ctx, ThemeSlot.PageBackground, Black, force);
            SetIf(ctx, ThemeSlot.UnlockBackground, Black, force);
            SetIf(ctx, ThemeSlot.ListBackground, Black, force);
            SetIf(ctx, ThemeSlot.TitlebarBackground, Black, force);
            SetIf(ctx, ThemeSlot.FabBackground, Black, force);
            SetIf(ctx, ThemeSlot.IconSecondary, Black, force);   // traced icon body

            // Everything else -> pure yellow.
            SetIf(ctx, ThemeSlot.PageText, Yellow, force);
            SetIf(ctx, ThemeSlot.Accent, Yellow, force);
            SetIf(ctx, ThemeSlot.ToolbarTitle, Yellow, force);
            SetIf(ctx, ThemeSlot.TitlebarText, Yellow, force);
            SetIf(ctx, ThemeSlot.TitlebarIcon, Yellow, force);
            SetIf(ctx, ThemeSlot.IconMain, Yellow, force);       // traced icon border + content
            SetIf(ctx, ThemeSlot.FabBorder, Yellow, force);
            SetIf(ctx, ThemeSlot.FabIcon, Yellow, force);
            SetIf(ctx, ThemeSlot.UnlockLabel, Yellow, force);
            SetIf(ctx, ThemeSlot.UnlockPasswordText, Yellow, force);
            SetIf(ctx, ThemeSlot.UnlockButtonText, Yellow, force);
            SetIf(ctx, ThemeSlot.UnlockFilename, Yellow, force);
            SetIf(ctx, ThemeSlot.EntryTitle, Yellow, force);
            SetIf(ctx, ThemeSlot.EntryUsername, Yellow, force);
            SetIf(ctx, ThemeSlot.EntryGroupPath, Yellow, force);
            SetIf(ctx, ThemeSlot.GroupTitle, Yellow, force);
            SetIf(ctx, ThemeSlot.GroupSubtitle, Yellow, force);
        }

        private static void SetIf(Context ctx, ThemeSlot slot, int argb, bool force)
        {
            if (force || !ThemeConfig.HasColor(ctx, ThemeColors.KeyOf(slot)))
                ThemeConfig.SetColor(ctx, ThemeColors.KeyOf(slot), argb);
        }
    }
}
