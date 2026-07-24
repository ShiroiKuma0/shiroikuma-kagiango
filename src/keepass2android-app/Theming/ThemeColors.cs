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

using System.Collections.Generic;

namespace keepass2android.Theming
{
    /// <summary>Logical grouping of slots into page sections.</summary>
    public enum ColorGroup
    {
        Global,
        Page,
        Chrome,
        TitleBar,
        Icons,
        Fab,
        Unlock,
        List,
        Settings,
        EntryScreen
    }

    /// <summary>Every themeable element of the 白い熊 鍵暗号 UI page.</summary>
    public enum ThemeSlot
    {
        AppFont,            // pseudo-slot: only a global font, no colour
        PageBackground,     // the 白い熊 鍵暗号 UI page's own background
        PageText,           // the 白い熊 鍵暗号 UI page's own text colour + font
        ToolbarTitle,
        Accent,
        UnlockBackground,
        UnlockLabel,
        UnlockPasswordText,
        UnlockButtonText,
        UnlockFilename,
        ListBackground,
        EntryTitle,
        EntryUsername,
        EntryGroupPath,
        GroupTitle,
        GroupSubtitle,
        TitlebarBackground,
        TitlebarText,
        TitlebarIcon,
        IconMain,
        IconSecondary,
        FabBackground,
        FabBorder,
        FabIcon,
        SettingsBackground,
        SettingsTitle,
        SettingsSubtitle,
        SettingsCategory,
        EntryViewBackground,
        EntryFieldLabel,
        EntryFieldValue
    }

    /// <summary>Metadata describing one themeable slot.</summary>
    public sealed class ColorSlotInfo
    {
        public ThemeSlot Slot { get; }
        public string Key { get; }          // preference key suffix
        public int LabelResId { get; }
        public ColorGroup Group { get; }
        public bool HasColor { get; }
        public bool HasFont { get; }

        public ColorSlotInfo(ThemeSlot slot, string key, int labelResId, ColorGroup group, bool hasColor, bool hasFont)
        {
            Slot = slot;
            Key = key;
            LabelResId = labelResId;
            Group = group;
            HasColor = hasColor;
            HasFont = hasFont;
        }
    }

    public static class ThemeColors
    {
        /// <summary>Sentinel meaning "no override; use the app's normal colour". Mirrors the siblings' THEME_UNSET.</summary>
        public const int ThemeUnset = int.MinValue;

        /// <summary>Reserved key for the app-wide global font.</summary>
        public const string AppFontKey = "__app__";

        public static readonly ColorSlotInfo[] All =
        {
            // Global
            new ColorSlotInfo(ThemeSlot.AppFont,            AppFontKey,             Resource.String.theme_app_font,            ColorGroup.Global, false, true),
            // The 白い熊 鍵暗号 UI page itself
            new ColorSlotInfo(ThemeSlot.PageBackground,     "page_background",      Resource.String.theme_page_background,      ColorGroup.Page,   true,  false),
            new ColorSlotInfo(ThemeSlot.PageText,           "page_text",            Resource.String.theme_page_text,           ColorGroup.Page,   true,  true),
            // App chrome / foundation
            new ColorSlotInfo(ThemeSlot.ToolbarTitle,       "toolbar_title",        Resource.String.theme_toolbar_title,       ColorGroup.Chrome, true,  true),
            new ColorSlotInfo(ThemeSlot.Accent,             "accent",               Resource.String.theme_accent,              ColorGroup.Chrome, true,  false),
            // Unlock screen
            new ColorSlotInfo(ThemeSlot.UnlockBackground,   "unlock_background",    Resource.String.theme_unlock_background,   ColorGroup.Unlock, true,  false),
            new ColorSlotInfo(ThemeSlot.UnlockLabel,        "unlock_label",         Resource.String.theme_unlock_label,        ColorGroup.Unlock, true,  true),
            new ColorSlotInfo(ThemeSlot.UnlockPasswordText, "unlock_password_text", Resource.String.theme_unlock_password_text,ColorGroup.Unlock, true,  true),
            new ColorSlotInfo(ThemeSlot.UnlockButtonText,   "unlock_button_text",   Resource.String.theme_unlock_button_text,  ColorGroup.Unlock, true,  true),
            new ColorSlotInfo(ThemeSlot.UnlockFilename,     "unlock_filename",      Resource.String.theme_unlock_filename,     ColorGroup.Unlock, true,  true),
            // Entry & group list
            new ColorSlotInfo(ThemeSlot.ListBackground,     "list_background",      Resource.String.theme_list_background,     ColorGroup.List,   true,  false),
            new ColorSlotInfo(ThemeSlot.EntryTitle,         "entry_title",          Resource.String.theme_entry_title,         ColorGroup.List,   true,  true),
            new ColorSlotInfo(ThemeSlot.EntryUsername,      "entry_username",       Resource.String.theme_entry_username,      ColorGroup.List,   true,  false),
            new ColorSlotInfo(ThemeSlot.EntryGroupPath,     "entry_group_path",     Resource.String.theme_entry_group_path,    ColorGroup.List,   true,  false),
            new ColorSlotInfo(ThemeSlot.GroupTitle,         "group_title",          Resource.String.theme_group_title,         ColorGroup.List,   true,  true),
            new ColorSlotInfo(ThemeSlot.GroupSubtitle,      "group_subtitle",       Resource.String.theme_group_subtitle,      ColorGroup.List,   true,  false),
            // Title row (the group-screen ActionBar)
            new ColorSlotInfo(ThemeSlot.TitlebarBackground, "titlebar_background",  Resource.String.theme_titlebar_background, ColorGroup.TitleBar, true, false),
            new ColorSlotInfo(ThemeSlot.TitlebarText,       "titlebar_text",        Resource.String.theme_titlebar_text,       ColorGroup.TitleBar, true, true),
            new ColorSlotInfo(ThemeSlot.TitlebarIcon,       "titlebar_icon",        Resource.String.theme_titlebar_icon,       ColorGroup.TitleBar, true, false),
            // List icons (traced)
            new ColorSlotInfo(ThemeSlot.IconMain,           "icon_main",            Resource.String.theme_icon_main,           ColorGroup.Icons,  true,  false),
            new ColorSlotInfo(ThemeSlot.IconSecondary,      "icon_secondary",       Resource.String.theme_icon_secondary,      ColorGroup.Icons,  true,  false),
            // Floating action buttons
            new ColorSlotInfo(ThemeSlot.FabBackground,      "fab_background",       Resource.String.theme_fab_background,      ColorGroup.Fab,    true,  false),
            new ColorSlotInfo(ThemeSlot.FabBorder,          "fab_border",           Resource.String.theme_fab_border,          ColorGroup.Fab,    true,  false),
            new ColorSlotInfo(ThemeSlot.FabIcon,            "fab_icon",             Resource.String.theme_fab_icon,            ColorGroup.Fab,    true,  false),
            // Settings pages (AndroidX preference screens)
            new ColorSlotInfo(ThemeSlot.SettingsBackground, "settings_background",  Resource.String.theme_settings_background, ColorGroup.Settings, true, false),
            new ColorSlotInfo(ThemeSlot.SettingsTitle,      "settings_title",       Resource.String.theme_settings_title,      ColorGroup.Settings, true, true),
            new ColorSlotInfo(ThemeSlot.SettingsSubtitle,   "settings_subtitle",    Resource.String.theme_settings_subtitle,   ColorGroup.Settings, true, false),
            new ColorSlotInfo(ThemeSlot.SettingsCategory,   "settings_category",    Resource.String.theme_settings_category,   ColorGroup.Settings, true, false),
            // Entry view (the single-entry screen)
            new ColorSlotInfo(ThemeSlot.EntryViewBackground,"entryview_background", Resource.String.theme_entryview_background,ColorGroup.EntryScreen, true, false),
            new ColorSlotInfo(ThemeSlot.EntryFieldLabel,    "entryview_label",      Resource.String.theme_entryview_label,     ColorGroup.EntryScreen, true, true),
            new ColorSlotInfo(ThemeSlot.EntryFieldValue,    "entryview_value",      Resource.String.theme_entryview_value,     ColorGroup.EntryScreen, true, true),
        };

        private static readonly Dictionary<ThemeSlot, ColorSlotInfo> BySlot = BuildIndex();

        private static Dictionary<ThemeSlot, ColorSlotInfo> BuildIndex()
        {
            var d = new Dictionary<ThemeSlot, ColorSlotInfo>();
            foreach (var s in All)
                d[s.Slot] = s;
            return d;
        }

        public static ColorSlotInfo Get(ThemeSlot slot) => BySlot[slot];

        public static string KeyOf(ThemeSlot slot) => BySlot[slot].Key;
    }
}
