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
using System.Linq;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using keepass2android.Backup;
using keepass2android.Theming;

namespace keepass2android
{
    /// <summary>
    /// The "白い熊 鍵暗号 UI" page: a programmatically-built, deeply-indented list of sections,
    /// subgroups, colour rows and font (family / weight / size / live sample) rows.
    /// </summary>
    [Activity(Label = "@string/shiroikuma_ui_category",
        Theme = "@style/Kp2aTheme_BlueNoActionBar",
        Exported = true,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden)]
    [IntentFilter(new[] { "kp2a.action.ShiroikumaUiActivity" }, Categories = new[] { Intent.CategoryDefault })]
    public class ShiroikumaUiActivity : LockingActivity
    {
        private const int ReqImportFont = 0x5C01;

        private static readonly int[] WeightValues = { 0, 100, 300, 400, 500, 600, 700, 900 };
        private static readonly string[] WeightLabels = { "Default", "Thin 100", "Light 300", "Regular 400", "Medium 500", "SemiBold 600", "Bold 700", "Black 900" };
        private static readonly int[] SizeValues = { 0, 12, 14, 16, 18, 20, 24, 28, 32, 40 };

        private readonly ActivityDesign _design;
        private LinearLayout _container;
        private int _baseStartPx;
        private int _stepPx;
        private string _pendingFontKey;

        private sealed class FontRowRefs
        {
            public TextView Family;
            public TextView Weight;
            public TextView Size;
            public TextView Sample;
        }

        private readonly Dictionary<string, FontRowRefs> _fontRows = new Dictionary<string, FontRowRefs>();
        private readonly Dictionary<ThemeSlot, View> _swatches = new Dictionary<ThemeSlot, View>();

        // Export / Import section rows that are refreshed in place rather than by rebuilding the page.
        private TextView _backupDirSummary;
        private TextView _automationTokenSummary;
        private View _automationTokenRow;

        /// <summary>The warning colour for "no backup directory set" — the same red the panel uses.</summary>
        private static readonly Color BackupWarn = new Color(unchecked((int)0xFFFF6666));

        public ShiroikumaUiActivity()
        {
            _design = new ActivityDesign(this);
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            _design.ApplyTheme();
            base.OnCreate(savedInstanceState);
            _pendingFontKey = savedInstanceState?.GetString("pending_font_key");
            SetContentView(Resource.Layout.activity_shiroikuma_ui);

            // Our own toolbar (NoActionBar theme) so the title row obeys the colour overrides.
            SetSupportActionBar(FindViewById<AndroidX.AppCompat.Widget.Toolbar>(Resource.Id.theme_toolbar));
            SupportActionBar?.SetDisplayHomeAsUpEnabled(true);
            Window?.DecorView?.Post(() => Kp2aTheme.ApplyToolbarChrome(this, null));
            OnSupportNavigateUpListener = () => { Finish(); return true; };

            _baseStartPx = Dp(16);
            _stepPx = Resources.GetDimensionPixelSize(Resource.Dimension.theme_indent_step);
            _container = FindViewById<LinearLayout>(Resource.Id.theme_container);
            new Util.InsetListener(_container).Apply();

            BuildPage();
        }

        // ---- page construction --------------------------------------------------------------------

        private void BuildPage()
        {
            _container.RemoveAllViews();
            _fontRows.Clear();
            _swatches.Clear();

            // Export / Import first, as on the Kōjiki UI page: backing the app up is what 白い熊 reaches
            // for most, and the 保存復元 automation rows belong under it rather than in a section of
            // their own — every sister app puts backup in the same place.
            // The link to the app's own settings: the first, thick pill, right under the page title.
            AddPillRow(Resource.String.menu_app_settings, Resource.String.theme_app_settings_desc, 0,
                main: true, opens: true, () => AppSettingsActivity.Launch(this));

            AddBackupSection();

            // App language.
            AddSection(Resource.String.theme_section_language);
            AddLanguageRow();

            // The 白い熊 鍵暗号 UI page itself — its background, text colour and font.
            AddSection(Resource.String.theme_section_page);
            AddSlot(ThemeSlot.PageBackground, 1);
            AddSlot(ThemeSlot.PageText, 1);
            AddSlot(ThemeSlot.PageNote, 1);

            AddSection(Resource.String.theme_section_global);
            AddFontRows(ThemeColors.AppFontKey, 1, Resource.String.theme_app_font);
            AddPillRow(Resource.String.theme_reset_palette, Resource.String.theme_reset_palette_desc, 1,
                main: false, opens: false, ResetPalette);

            AddSection(Resource.String.theme_section_chrome);
            AddSlot(ThemeSlot.ToolbarTitle, 1);
            AddSlot(ThemeSlot.Accent, 1);

            AddSection(Resource.String.theme_section_titlebar);
            AddSlot(ThemeSlot.TitlebarBackground, 1);
            AddSlot(ThemeSlot.TitlebarText, 1);
            AddSlot(ThemeSlot.TitlebarIcon, 1);

            AddSection(Resource.String.theme_section_unlock);
            AddSlot(ThemeSlot.UnlockBackground, 1);
            AddSlot(ThemeSlot.UnlockLabel, 1);
            AddSlot(ThemeSlot.UnlockPasswordText, 1);
            AddSlot(ThemeSlot.UnlockButtonText, 1);
            AddSlot(ThemeSlot.UnlockFilename, 1);

            AddSection(Resource.String.theme_section_list);
            AddSlot(ThemeSlot.ListBackground, 1);
            AddSubgroup(Resource.String.theme_subgroup_entries, 1);
            AddSlot(ThemeSlot.EntryTitle, 2);
            AddSlot(ThemeSlot.EntryUsername, 2);
            AddSlot(ThemeSlot.EntryGroupPath, 2);
            AddSubgroup(Resource.String.theme_subgroup_groups, 1);
            AddSlot(ThemeSlot.GroupTitle, 2);
            AddSlot(ThemeSlot.GroupSubtitle, 2);

            AddSection(Resource.String.theme_section_entryview);
            AddSlot(ThemeSlot.EntryViewBackground, 1);
            AddSlot(ThemeSlot.EntryFieldLabel, 1);
            AddSlot(ThemeSlot.EntryFieldValue, 1);

            AddSection(Resource.String.theme_section_settings);
            AddSlot(ThemeSlot.SettingsBackground, 1);
            AddSlot(ThemeSlot.SettingsTitle, 1);
            AddSlot(ThemeSlot.SettingsSubtitle, 1);
            AddSlot(ThemeSlot.SettingsCategory, 1);

            AddSection(Resource.String.theme_section_icons);
            AddSlot(ThemeSlot.IconMain, 1);
            AddSlot(ThemeSlot.IconSecondary, 1);

            AddSection(Resource.String.theme_section_fab);
            AddSlot(ThemeSlot.FabBackground, 1);
            AddSlot(ThemeSlot.FabBorder, 1);
            AddSlot(ThemeSlot.FabIcon, 1);

            // Diagnostics (diagnostics-ui-settings skill): every trace behind its own switch, off by default.
            AddSection(Resource.String.theme_section_diagnostics);
            var leakRow = AddDetailRow(Resource.String.diag_leaks_title, GetString(Resource.String.diag_leaks_desc), 1,
                null, out _, out _);
            AddTagLine(leakRow, GetString(Resource.String.diag_leaks_tag), GetString(Resource.String.diag_leaks_qualifier));
            AttachSwitch(leakRow, LeakTracer.IsEnabled(this), value => LeakTracer.SetEnabled(this, value));

            ApplyPageBackground();
        }

        private void AddSlot(ThemeSlot slot, int level)
        {
            ColorSlotInfo info = ThemeColors.Get(slot);
            if (info.HasColor)
                AddColorRow(slot, level);
            if (info.HasFont)
                AddFontRows(info.Key, level + (info.HasColor ? 1 : 0), Resource.String.theme_font_family);
        }

        /// <summary>
        /// A top-level heading in the kxkb house style: a full-width hairline spacer marking the border
        /// with the previous group, then the bold accent title carrying a word-width underline.
        /// </summary>
        private void AddSection(int titleRes)
        {
            var v = LayoutInflater.Inflate(Resource.Layout.item_theme_section, _container, false);
            var title = v.FindViewById<TextView>(Resource.Id.section_title);
            var separator = v.FindViewById<View>(Resource.Id.section_separator);
            var underline = v.FindViewById<View>(Resource.Id.section_divider);
            title.Text = GetString(titleRes);
            Color accent = Kp2aTheme.AccentOr(this, new Color(title.CurrentTextColor));
            title.SetTextColor(accent);
            ApplyPageFont(title);
            separator.SetBackgroundColor(accent);
            underline.SetBackgroundColor(accent);
            _container.AddView(v);
        }

        /// <summary>A sub-heading: indented, word-width underline, and no full-width spacer.</summary>
        private void AddSubgroup(int titleRes, int level)
        {
            var v = LayoutInflater.Inflate(Resource.Layout.item_theme_subgroup, _container, false);
            var title = v.FindViewById<TextView>(Resource.Id.subgroup_title);
            var underline = v.FindViewById<View>(Resource.Id.subgroup_divider);
            title.Text = GetString(titleRes);
            Color accent = Kp2aTheme.AccentOr(this, new Color(title.CurrentTextColor));
            title.SetTextColor(accent);
            ApplyPageFont(title);
            underline.SetBackgroundColor(accent);
            Indent(v, level);
            _container.AddView(v);
        }

        private void AddColorRow(ThemeSlot slot, int level)
        {
            var v = LayoutInflater.Inflate(Resource.Layout.item_theme_color, _container, false);
            var label = v.FindViewById<TextView>(Resource.Id.row_label);
            var swatch = v.FindViewById<View>(Resource.Id.color_swatch);
            label.Text = GetString(ThemeColors.Get(slot).LabelResId);
            ApplyPageLabel(label);
            _swatches[slot] = swatch;
            RefreshSwatch(slot);
            v.Click += (s, e) => OpenColorPicker(slot);
            Indent(v, level);
            _container.AddView(v);
        }

        private LinearLayout AddValueRow(int labelRes, int level, Action onClick, out TextView valueView)
        {
            var v = (LinearLayout)LayoutInflater.Inflate(Resource.Layout.item_theme_value, _container, false);
            var label = v.FindViewById<TextView>(Resource.Id.row_label);
            label.Text = GetString(labelRes);
            valueView = v.FindViewById<TextView>(Resource.Id.row_value);
            ApplyPageLabel(label);
            ApplyPageLabel(valueView);
            if (onClick != null)
                v.Click += (s, e) => onClick();
            Indent(v, level);
            _container.AddView(v);
            return v;
        }

        /// <summary>
        /// Something to press (black-yellow-theme skill, "every pressable row is a pill"): a full-width
        /// outlined pill — never filled — holding the label, 3 dp frame and bold for the main action of its
        /// section, 1.25 dp otherwise; › inside it when it opens something; the explanation beneath it,
        /// aligned with the label. The whole pill is the touch target, the explanation is not.
        /// </summary>
        private void AddPillRow(int labelRes, int summaryRes, int level, bool main, bool opens, Action onClick)
        {
            var row = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
            row.SetPadding(0, Dp(3), Dp(16), Dp(3));
            var column = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };

            var label = new TextView(this) { Text = GetString(labelRes) };
            label.SetTextSize(Android.Util.ComplexUnitType.Sp, 16);
            ApplyPageLabel(label);
            Color ink = new Color(label.CurrentTextColor);
            if (main)
                label.SetTypeface(label.Typeface, TypefaceStyle.Bold);

            var pill = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
            pill.SetGravity(GravityFlags.CenterVertical);
            pill.SetPadding(Dp(18), Dp(9), Dp(16), Dp(9));
            pill.Background = PillBackground(ink, main);
            pill.Clickable = true;
            pill.Focusable = true;
            pill.Click += (s, e) => onClick();
            pill.AddView(label, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            if (opens)
            {
                var chevron = new TextView(this) { Text = "\u203A" };
                chevron.SetTextSize(Android.Util.ComplexUnitType.Sp, 22);
                chevron.SetIncludeFontPadding(false);
                ApplyPageLabel(chevron);
                if (main)
                    chevron.SetTypeface(chevron.Typeface, TypefaceStyle.Bold);
                var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
                lp.LeftMargin = Dp(10);
                pill.AddView(chevron, lp);
            }
            column.AddView(pill, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

            var summary = new TextView(this) { Text = GetString(summaryRes) };
            summary.SetTextSize(Android.Util.ComplexUnitType.Sp, 13);
            summary.SetPadding(Dp(18), Dp(3), 0, 0);
            ApplyPageNote(summary);
            column.AddView(summary);

            // the rule spans pill + explanation, as on the rows with explanations
            row.AddView(NoteRule(), RuleParams());
            row.AddView(column, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            Indent(row, level);
            _container.AddView(row);
        }

        private void AddFontRows(string key, int level, int familyLabelRes)
        {
            AddValueRow(familyLabelRes, level, () => OpenFontPicker(key), out var family);
            AddValueRow(Resource.String.theme_font_weight, level, () => OpenWeightPicker(key), out var weight);
            AddValueRow(Resource.String.theme_font_size, level, () => OpenSizePicker(key), out var size);

            var sampleRow = (LinearLayout)LayoutInflater.Inflate(Resource.Layout.item_theme_value, _container, false);
            var sample = sampleRow.FindViewById<TextView>(Resource.Id.row_label);
            sample.Text = GetString(Resource.String.theme_font_sample);
            sampleRow.FindViewById<TextView>(Resource.Id.row_value).Visibility = ViewStates.Gone;
            Indent(sampleRow, level);
            _container.AddView(sampleRow);

            ApplyPageTextColor(sample);
            _fontRows[key] = new FontRowRefs { Family = family, Weight = weight, Size = size, Sample = sample };
            RefreshFontRows(key);
        }

        // ---- export / import ------------------------------------------------------------------------

        /// <summary>
        /// The first section of the page: the Export / Import panel, the backup directory, and — directly
        /// beneath those, never as a section of its own — the 保存復元 automation rows: the master switch
        /// (default ON), 「Use authorization token?」 (default OFF), and the token itself, which is shown
        /// only while it is actually being asked for.
        /// </summary>
        private void AddBackupSection()
        {
            AddSection(Resource.String.theme_section_backup);

            AddPillRow(Resource.String.backup_open_row, Resource.String.backup_open_desc, 1,
                main: true, opens: true, OpenExportImport);

            AddDetailRow(Resource.String.backup_dir_row, "", 1, EditBackupDir, out _backupDirSummary, out _);
            RefreshBackupDir();

            AddSwitchRow(Resource.String.automation_title, Resource.String.automation_summary, 1,
                AutomationAuth.IsEnabled(this), value => AutomationAuth.SetEnabled(this, value));

            // Row 2 is what reveals row 3, and hides it again: a 48-character secret sitting under a
            // switch that is off invites 白い熊 to paste it somewhere it will do nothing.
            AddSwitchRow(Resource.String.automation_require_token_title,
                Resource.String.automation_require_token_summary, 1,
                AutomationAuth.RequiresToken(this), value =>
                {
                    AutomationAuth.SetRequiresToken(this, value);
                    if (_automationTokenRow != null)
                        _automationTokenRow.Visibility = value ? ViewStates.Visible : ViewStates.Gone;
                });

            AddTokenRow(1);
        }

        /// <summary>The directory, shown in warn-red until one is set — as the panel shows it.</summary>
        private void RefreshBackupDir()
        {
            if (_backupDirSummary == null)
                return;
            string dir = BackupConfig.GetExportDir(this);
            _backupDirSummary.Visibility = ViewStates.Visible;
            _backupDirSummary.Text = dir ?? GetString(Resource.String.backup_dir_unset);
            if (dir == null)
                _backupDirSummary.SetTextColor(BackupWarn);
            else
                ApplyPageNote(_backupDirSummary);
        }

        private void OpenExportImport() =>
            ExportImportDialog.Show(this, onChainFinished: Finish, onDismissed: RefreshBackupDir);

        /// <summary>Set the directory from the page itself, without opening the whole panel first.</summary>
        private void EditBackupDir() =>
            ExportImportDialog.EditDirectory(this, RefreshBackupDir);

        private void AddTokenRow(int level)
        {
            _automationTokenRow = AddDetailRow(Resource.String.automation_token_title,
                AutomationAuth.Abbreviate(AutomationAuth.Token(this)), level,
                CopyAutomationToken, out _automationTokenSummary, out var action);
            // Shown only when the row above asks for a token (contract v2 §2).
            _automationTokenRow.Visibility =
                AutomationAuth.RequiresToken(this) ? ViewStates.Visible : ViewStates.Gone;

            action.Visibility = ViewStates.Visible;
            action.Text = GetString(Resource.String.automation_token_regenerate);
            Color accent = Kp2aTheme.AccentOr(this, new Color(action.CurrentTextColor));
            action.SetTextColor(accent);
            ApplyPageFont(action);
            // every tappable action is a pill — a regular one: thin frame, normal weight, never filled
            action.SetTypeface(action.Typeface, TypefaceStyle.Normal);
            action.Background = PillBackground(accent, main: false);
            action.SetPadding(Dp(14), Dp(5), Dp(14), Dp(5));
            action.Click += (s, e) =>
            {
                string fresh = AutomationAuth.RegenerateToken(this);
                _automationTokenSummary.Text = AutomationAuth.Abbreviate(fresh);
                Toast.MakeText(this, Resource.String.automation_token_regenerated, ToastLength.Long).Show();
            };
        }

        private void CopyAutomationToken()
        {
            var clipboard = (ClipboardManager)GetSystemService(ClipboardService);
            if (clipboard != null)
                clipboard.PrimaryClip = ClipData.NewPlainText(
                    GetString(Resource.String.automation_token_title), AutomationAuth.Token(this));
            Toast.MakeText(this, Resource.String.automation_token_copied, ToastLength.Short).Show();
        }

        /// <summary>A row with a second line, an optional right-hand action and an optional widget slot.</summary>
        private View AddDetailRow(int labelRes, string summary, int level, Action onClick,
            out TextView summaryView, out TextView actionView)
        {
            var v = LayoutInflater.Inflate(Resource.Layout.item_theme_detail, _container, false);
            var label = v.FindViewById<TextView>(Resource.Id.row_label);
            summaryView = v.FindViewById<TextView>(Resource.Id.row_summary);
            actionView = v.FindViewById<TextView>(Resource.Id.row_action);

            label.Text = GetString(labelRes);
            summaryView.Text = summary ?? "";
            summaryView.Visibility = string.IsNullOrEmpty(summary) ? ViewStates.Gone : ViewStates.Visible;
            ApplyPageLabel(label);
            ApplyPageNote(summaryView);

            // black-yellow-theme skill, settings rows with explanations: bold label, dim-gold description,
            // a thin dim-gold rule down the item's left edge, tight padding.
            label.SetTypeface(label.Typeface, TypefaceStyle.Bold);
            ((LinearLayout)v).AddView(NoteRule(), 0, RuleParams());
            v.SetMinimumHeight(0);
            v.SetPadding(v.PaddingLeft, Dp(2), v.PaddingRight, Dp(2));

            if (onClick != null)
                v.Click += (s, e) => onClick();
            Indent(v, level);
            _container.AddView(v);
            return v;
        }

        private void AddSwitchRow(int labelRes, int summaryRes, int level, bool initial, Action<bool> onChanged)
        {
            var v = AddDetailRow(labelRes, GetString(summaryRes), level, null, out _, out _);
            AttachSwitch(v, initial, onChanged);
        }

        private void AttachSwitch(View v, bool initial, Action<bool> onChanged)
        {
            var widget = v.FindViewById<FrameLayout>(Resource.Id.row_widget);

            var toggle = new AndroidX.AppCompat.Widget.SwitchCompat(this) { Checked = initial };
            toggle.SetMinimumHeight(0);
            toggle.SetMinHeight(0);
            Color accent = Kp2aTheme.AccentOr(this, new Color(toggle.CurrentTextColor));
            toggle.ThumbTintList = ColorStateList.ValueOf(accent);
            toggle.TrackTintList = ColorStateList.ValueOf(
                new Color(Color.Argb(0x80, accent.R, accent.G, accent.B)));
            widget.AddView(toggle);

            toggle.CheckedChange += (s, e) => onChanged(e.IsChecked);
            v.Click += (s, e) => toggle.Checked = !toggle.Checked;
        }

        // ---- refresh ------------------------------------------------------------------------------

        /// <summary>Page colours repaint the whole page (descriptions, rules, pills); others just their swatch.</summary>
        private void AfterColorChange(ThemeSlot slot)
        {
            if (slot == ThemeSlot.PageText || slot == ThemeSlot.PageNote || slot == ThemeSlot.PageBackground ||
                slot == ThemeSlot.Accent)
                BuildPage();
            else
                RefreshSwatch(slot);
        }

        private void RefreshSwatch(ThemeSlot slot)
        {
            if (!_swatches.TryGetValue(slot, out var swatch)) return;
            int v = ThemeConfig.GetColor(this, ThemeColors.KeyOf(slot));
            var d = new GradientDrawable();
            d.SetShape(ShapeType.Rectangle);
            d.SetCornerRadius(Dp(4));
            d.SetStroke(Dp(1), new Color(unchecked((int)0x80888888)));
            d.SetColor(v == ThemeColors.ThemeUnset ? Color.Transparent : new Color(v));
            swatch.Background = d;
        }

        private void RefreshFontRows(string key)
        {
            if (!_fontRows.TryGetValue(key, out var r)) return;
            string family = ThemeConfig.GetFontFamily(this, key);
            int weight = ThemeConfig.GetFontWeight(this, key);
            int sizeSp = ThemeConfig.GetFontSizeSp(this, key);

            r.Family.Text = FontDisplayName(family);
            r.Weight.Text = WeightLabel(weight);
            r.Size.Text = sizeSp > 0 ? sizeSp + " sp" : GetString(Resource.String.theme_default);

            r.Sample.Typeface = FontManager.FontTypeface(this, family, weight);
            r.Sample.SetTextSize(Android.Util.ComplexUnitType.Sp, sizeSp > 0 ? sizeSp : 18);
        }

        // ---- pickers ------------------------------------------------------------------------------

        private void OpenColorPicker(ThemeSlot slot)
        {
            int current = ThemeConfig.GetColor(this, ThemeColors.KeyOf(slot));
            ColorPickerDialog.Show(this, current,
                onPicked: argb => { ThemeConfig.SetColor(this, ThemeColors.KeyOf(slot), argb); AfterColorChange(slot); },
                onCleared: () => { ThemeConfig.ClearColor(this, ThemeColors.KeyOf(slot)); AfterColorChange(slot); });
        }

        private void OpenFontPicker(string key)
        {
            int weight = ThemeConfig.GetFontWeight(this, key);
            FontPickerDialog.Show(this, weight,
                onPick: fileName => { ThemeConfig.SetFontFamily(this, key, fileName); RefreshFontRows(key); },
                onAddFont: () => { _pendingFontKey = key; LaunchFontImport(); });
        }

        private void OpenWeightPicker(string key)
        {
            new Google.Android.Material.Dialog.MaterialAlertDialogBuilder(this)
                .SetTitle(Resource.String.theme_font_weight)
                .SetItems(WeightLabels, (s, e) =>
                {
                    ThemeConfig.SetFontWeight(this, key, WeightValues[e.Which]);
                    RefreshFontRows(key);
                })
                .Show();
        }

        private void OpenSizePicker(string key)
        {
            var labels = new string[SizeValues.Length];
            for (int i = 0; i < SizeValues.Length; i++)
                labels[i] = SizeValues[i] == 0 ? GetString(Resource.String.theme_default) : SizeValues[i] + " sp";
            new Google.Android.Material.Dialog.MaterialAlertDialogBuilder(this)
                .SetTitle(Resource.String.theme_font_size)
                .SetItems(labels, (s, e) =>
                {
                    ThemeConfig.SetFontSizeSp(this, key, SizeValues[e.Which]);
                    RefreshFontRows(key);
                })
                .Show();
        }

        private void ResetPalette()
        {
            ThemeSeeder.ApplySignaturePalette(this);
            BuildPage();
        }

        // ---- font import --------------------------------------------------------------------------

        private void LaunchFontImport()
        {
            // Match the app's canonical document-picker (Util.ShowBrowseDialog): plain "*/*" + openable.
            // Do NOT add EXTRA_MIME_TYPES — most providers don't tag fonts with a font/* MIME, so a
            // font filter would hide every file and the picker would come back empty (silent failure).
            var intent = new Intent(Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("*/*");
            try
            {
                StartActivityForResult(intent, ReqImportFont);
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Theme: font import launch failed: " + e);
                Toast.MakeText(this, Resource.String.theme_font_import_failed, ToastLength.Long).Show();
            }
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            if (requestCode != ReqImportFont)
                return;
            string key = _pendingFontKey;
            _pendingFontKey = null;
            if (resultCode != Result.Ok || data?.Data == null)
                return;   // user cancelled

            string name = FontManager.ImportFont(this, data.Data);
            if (name == null)
            {
                Toast.MakeText(this, Resource.String.theme_font_import_failed, ToastLength.Long).Show();
                return;
            }
            // The font is now available in the picker; if we know the slot, select it too.
            if (key != null)
            {
                ThemeConfig.SetFontFamily(this, key, name);
                RefreshFontRows(key);
            }
            Toast.MakeText(this, FontDisplayName(name), ToastLength.Short).Show();
        }

        protected override void OnSaveInstanceState(Bundle outState)
        {
            base.OnSaveInstanceState(outState);
            if (_pendingFontKey != null)
                outState.PutString("pending_font_key", _pendingFontKey);
        }

        // ---- this page (background / text / font) -------------------------------------------------

        private void ApplyPageBackground()
        {
            if (Kp2aTheme.TryColor(this, ThemeSlot.PageBackground, out var c))
            {
                FindViewById(Android.Resource.Id.Content)?.SetBackgroundColor(c);
                _container?.SetBackgroundColor(c);
            }
        }

        private void ApplyPageFont(TextView tv) =>
            Kp2aTheme.ApplyFont(tv, ThemeColors.KeyOf(ThemeSlot.PageText), this);

        private void ApplyPageTextColor(TextView tv)
        {
            if (Kp2aTheme.TryColor(this, ThemeSlot.PageText, out var c))
                tv.SetTextColor(c);
        }

        private void ApplyPageLabel(TextView tv)
        {
            ApplyPageTextColor(tv);
            ApplyPageFont(tv);
        }

        /// <summary>The dim-gold "Descriptions" colour; derived from the page text if it was cleared.</summary>
        private Color NoteColor()
        {
            if (Kp2aTheme.TryColor(this, ThemeSlot.PageNote, out var c))
                return c;
            Color t = Kp2aTheme.TryColor(this, ThemeSlot.PageText, out var text) ? text : new Color(unchecked((int)0xFFFFFF00));
            return new Color(Color.Argb(t.A, t.R * 200 / 255, t.G * 200 / 255, t.B * 200 / 255));
        }

        private void ApplyPageNote(TextView tv)
        {
            tv.SetTextColor(NoteColor());
            ApplyPageFont(tv);
        }

        private View NoteRule()
        {
            var rule = new View(this);
            rule.SetBackgroundColor(NoteColor());
            return rule;
        }

        private LinearLayout.LayoutParams RuleParams()
        {
            var lp = new LinearLayout.LayoutParams(Dp(2), ViewGroup.LayoutParams.MatchParent);
            lp.MarginEnd = Dp(10);
            return lp;
        }

        /// <summary>An outlined pill (never filled): 3 dp for the main action, 1.25 dp otherwise.</summary>
        private Drawable PillBackground(Color ink, bool main)
        {
            var frame = new GradientDrawable();
            frame.SetColor(Color.Transparent);
            frame.SetStroke(Math.Max(1, (int)Math.Round((main ? 3f : 1.25f) * Resources.DisplayMetrics.Density)), ink);
            frame.SetCornerRadius(Dp(50));
            return new RippleDrawable(
                ColorStateList.ValueOf(new Color(Color.Argb(0x4D, ink.R, ink.G, ink.B))), frame, null);
        }

        /// <summary>
        /// The optional tag line under a label: the tag in bold monospace accent, then "  ·  " and a short
        /// qualifier in dim gold (black-yellow-theme skill — only where the item has a natural tag).
        /// </summary>
        private void AddTagLine(View row, string tag, string qualifier)
        {
            var label = row.FindViewById<TextView>(Resource.Id.row_label);
            var texts = (LinearLayout)label.Parent;
            var line = new TextView(this);
            var sb = new Android.Text.SpannableStringBuilder();
            sb.Append(tag);
            int tagEnd = sb.Length();
            Color accent = Kp2aTheme.AccentOr(this, new Color(label.CurrentTextColor));
            sb.SetSpan(new Android.Text.Style.ForegroundColorSpan(accent), 0, tagEnd, Android.Text.SpanTypes.ExclusiveExclusive);
            sb.SetSpan(new Android.Text.Style.StyleSpan(TypefaceStyle.Bold), 0, tagEnd, Android.Text.SpanTypes.ExclusiveExclusive);
            sb.SetSpan(new Android.Text.Style.TypefaceSpan("monospace"), 0, tagEnd, Android.Text.SpanTypes.ExclusiveExclusive);
            sb.Append("  ·  " + qualifier);
            sb.SetSpan(new Android.Text.Style.ForegroundColorSpan(NoteColor()), tagEnd, sb.Length(), Android.Text.SpanTypes.ExclusiveExclusive);
            line.SetText(sb, TextView.BufferType.Spannable);
            line.SetTextSize(Android.Util.ComplexUnitType.Sp, 13);
            texts.AddView(line, texts.IndexOfChild(label) + 1);
        }

        // ---- app language -------------------------------------------------------------------------

        private void AddLanguageRow()
        {
            AddValueRow(Resource.String.theme_app_language, 0, OpenLanguagePicker, out var value);
            value.Text = CurrentLanguageName();
        }

        private string LanguagePref() =>
            AndroidX.Preference.PreferenceManager.GetDefaultSharedPreferences(this)
                .GetString(GetString(Resource.String.app_language_pref_key), null);

        private string CurrentLanguageName()
        {
            string lang = LanguageEntry.PrefCodeToLanguage(LanguagePref());
            if (string.IsNullOrEmpty(lang))
                return GetString(Resource.String.SystemLanguage);
            var loc = new Java.Util.Locale(lang);
            return Capitalize(loc.GetDisplayLanguage(loc));
        }

        private void OpenLanguagePicker()
        {
            var items = BuildLanguageItems();
            var names = items.Select(i => i.name).ToArray();
            string currentLang = LanguageEntry.PrefCodeToLanguage(LanguagePref());
            int checkedIndex = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (LanguageEntry.PrefCodeToLanguage(items[i].code) == currentLang)
                {
                    checkedIndex = i;
                    break;
                }
            }

            AndroidX.AppCompat.App.AlertDialog dlg = null;
            dlg = new Google.Android.Material.Dialog.MaterialAlertDialogBuilder(this)
                .SetTitle(Resource.String.theme_app_language)
                .SetSingleChoiceItems(names, checkedIndex, (s, e) =>
                {
                    string code = items[e.Which].code;
                    dlg?.Dismiss();
                    PickLanguage(code);
                })
                .SetNegativeButton(Android.Resource.String.Cancel, (EventHandler<Android.Content.DialogClickEventArgs>)((s, e) => { }))
                .Create();
            dlg.Show();
        }

        private void PickLanguage(string prefCode)
        {
            AndroidX.Preference.PreferenceManager.GetDefaultSharedPreferences(this)
                .Edit().PutString(GetString(Resource.String.app_language_pref_key), prefCode).Apply();
            LocaleManager.Language = LanguageEntry.PrefCodeToLanguage(prefCode);
            Recreate();
        }

        private List<(string code, string name)> BuildLanguageItems()
        {
            var supported = new HashSet<string>
            {
                "en","af","ar","az","be","bg","ca","cs","da","de","el","es","eu","fa","fi","fr","gl","he",
                "hr","hu","id","in","it","iw","ja","ko","ml","nb","nl","nn","no","pl","pt","ro","ru","si",
                "sk","sl","sr","sv","tr","uk","vi","zh"
            };
            var byCode = new Dictionary<string, string>();
            foreach (var loc in Java.Util.Locale.GetAvailableLocales())
            {
                if (!supported.Contains(loc.Language) || byCode.ContainsKey(loc.Language))
                    continue;
                string native = loc.GetDisplayLanguage(loc);
                byCode[loc.Language] = string.IsNullOrEmpty(native) ? loc.Language : Capitalize(native);
            }
            var list = byCode
                .Select(kv => (code: kv.Key, name: kv.Value))
                .OrderBy(t => t.name, System.StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            list.Insert(0, (LanguageEntry.SystemDefault("").Code, GetString(Resource.String.SystemLanguage)));
            return list;
        }

        private static string Capitalize(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

        // ---- helpers ------------------------------------------------------------------------------

        private string FontDisplayName(string family)
        {
            if (string.IsNullOrEmpty(family))
                return GetString(Resource.String.theme_font_system_default);
            if (family == FontManager.Monospace)
                return GetString(Resource.String.theme_font_monospace);
            int dot = family.LastIndexOf('.');
            return dot > 0 ? family.Substring(0, dot) : family;
        }

        private string WeightLabel(int weight)
        {
            for (int i = 0; i < WeightValues.Length; i++)
                if (WeightValues[i] == weight)
                    return WeightLabels[i];
            return weight.ToString();
        }

        private void Indent(View v, int level)
        {
            int start = _baseStartPx + level * _stepPx;
            v.SetPaddingRelative(start, v.PaddingTop, v.PaddingEnd, v.PaddingBottom);
        }

        private int Dp(int dp) => (int)(dp * Resources.DisplayMetrics.Density + 0.5f);
    }
}
