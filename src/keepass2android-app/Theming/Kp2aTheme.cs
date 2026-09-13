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
using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace keepass2android.Theming
{
    /// <summary>
    /// Applies the 白い熊 鍵暗号 UI overrides to live views: a global app font + window background
    /// from the base activity, and targeted per-slot colour/font at specific render sites.
    /// All entry points are defensive — a theming fault must never break navigation.
    /// </summary>
    public static class Kp2aTheme
    {
        public static bool TryColor(Context ctx, ThemeSlot slot, out Color color)
        {
            int v = ThemeConfig.GetColor(ctx, ThemeColors.KeyOf(slot));
            if (v == ThemeColors.ThemeUnset)
            {
                color = Color.Transparent;
                return false;
            }
            color = new Color(v);
            return true;
        }

        /// <summary>Cheap check used to short-circuit the global pass.</summary>
        public static bool IsGlobalActive(Context ctx) =>
            !string.IsNullOrEmpty(ThemeConfig.GetFontFamily(ctx, ThemeColors.AppFontKey));

        /// <summary>
        /// Called from the base activity's OnContentChanged. Applies the global app font to every
        /// TextView in the content view. Deliberately does NOT touch background/text colours app-wide:
        /// colours are applied per-screen only where we also control the text colour, so screens we
        /// don't theme stay readable. No-op unless a global font is set.
        /// </summary>
        public static void ApplyGlobal(Activity activity)
        {
            try
            {
                Context ctx = activity;
                string appFont = ThemeConfig.GetFontFamily(ctx, ThemeColors.AppFontKey);
                int appWeight = ThemeConfig.GetFontWeight(ctx, ThemeColors.AppFontKey);
                if (string.IsNullOrEmpty(appFont))
                    return;

                View root = activity.Window?.DecorView?.FindViewById(Android.Resource.Id.Content);
                if (root == null)
                    return;

                Android.Graphics.Typeface tf = FontManager.FontTypeface(ctx, appFont, appWeight);
                ApplyTypefaceRecursive(root, tf);
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Theme: ApplyGlobal failed: " + e);
            }
        }

        private static void ApplyTypefaceRecursive(View v, Android.Graphics.Typeface tf)
        {
            // the (i) help glyph is drawn with an icon font; a text font would turn it into a box
            if (v is TextView tv && !(v is keepass2android.views.Kp2aShortHelpView))
            {
                TypefaceStyle style = tv.Typeface?.Style ?? TypefaceStyle.Normal;
                tv.SetTypeface(tf, style);
            }
            if (v is ViewGroup vg)
            {
                int n = vg.ChildCount;
                for (int i = 0; i < n; i++)
                    ApplyTypefaceRecursive(vg.GetChildAt(i), tf);
            }
        }

        /// <summary>Targeted: apply a slot's colour (if overridden) and its font to one TextView.</summary>
        public static void ApplyColorAndFont(TextView tv, ThemeSlot slot, Context ctx = null)
        {
            if (tv == null)
                return;
            try
            {
                ctx = ctx ?? tv.Context;
                ColorSlotInfo info = ThemeColors.Get(slot);
                if (info.HasColor && TryColor(ctx, slot, out var c))
                    tv.SetTextColor(c);
                ApplyFont(tv, info.Key, ctx);
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Theme: ApplyColorAndFont failed: " + e);
            }
        }

        /// <summary>Apply a slot's font (family/weight/size), falling back to the global app font.</summary>
        public static void ApplyFont(TextView tv, string slotKey, Context ctx)
        {
            Android.Graphics.Typeface tf = SlotTypeface(ctx, slotKey);
            if (tf != null)
            {
                TypefaceStyle style = tv.Typeface?.Style ?? TypefaceStyle.Normal;
                tv.SetTypeface(tf, style);
            }
            int sizeSp = ThemeConfig.GetFontSizeSp(ctx, slotKey);
            if (sizeSp > 0)
                tv.SetTextSize(Android.Util.ComplexUnitType.Sp, sizeSp);
        }

        /// <summary>
        /// A slot's typeface (family/weight), inheriting the global app font when the slot has none of
        /// its own; null when neither is set. For text that is not a TextView (a collapsing title).
        /// </summary>
        public static Android.Graphics.Typeface SlotTypeface(Context ctx, string slotKey)
        {
            string family = ThemeConfig.GetFontFamily(ctx, slotKey);
            int weight = ThemeConfig.GetFontWeight(ctx, slotKey);

            if (string.IsNullOrEmpty(family))
            {
                family = ThemeConfig.GetFontFamily(ctx, ThemeColors.AppFontKey);
                if (weight == 0)
                    weight = ThemeConfig.GetFontWeight(ctx, ThemeColors.AppFontKey);
            }

            if (string.IsNullOrEmpty(family) && weight <= 0)
                return null;
            return FontManager.FontTypeface(ctx, family, weight);
        }

        public static void ApplyBackground(View v, ThemeSlot slot)
        {
            if (v == null)
                return;
            try
            {
                if (TryColor(v.Context, slot, out var c))
                    v.SetBackgroundColor(c);
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Theme: ApplyBackground failed: " + e);
            }
        }

        /// <summary>Accent override, or the supplied fallback when unset.</summary>
        public static Color AccentOr(Context ctx, Color fallback) =>
            TryColor(ctx, ThemeSlot.Accent, out var c) ? c : fallback;

        private static ColorStateList Csl(Color c) => ColorStateList.ValueOf(c);

        private static int Dp(Context ctx, int dp) =>
            (int)(dp * ctx.Resources.DisplayMetrics.Density + 0.5f);

        // ---- list icons (traced: black body, yellow ring, yellow line content) --------------------

        public static void ApplyTracedIcon(ImageView bkg, ImageView foreground)
        {
            try
            {
                if (foreground == null)
                    return;
                Context ctx = foreground.Context;
                bool hasMain = TryColor(ctx, ThemeSlot.IconMain, out var main);
                bool hasSecondary = TryColor(ctx, ThemeSlot.IconSecondary, out var secondary);

                if (bkg != null && (hasMain || hasSecondary))
                {
                    int size = Dp(ctx, 40);
                    var d = new GradientDrawable();
                    d.SetShape(ShapeType.Oval);
                    d.SetSize(size, size);
                    d.SetColor(hasSecondary ? secondary : Color.Transparent);
                    if (hasMain)
                        d.SetStroke(Dp(ctx, 2), main);
                    bkg.SetImageDrawable(d);
                }

                if (hasMain)
                    foreground.SetColorFilter(main);
                else
                    foreground.ClearColorFilter();
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Theme: ApplyTracedIcon failed: " + e);
            }
        }

        // ---- floating action buttons --------------------------------------------------------------

        public static void ApplyFab(View fab)
        {
            if (fab == null)
                return;
            try
            {
                Context ctx = fab.Context;
                bool hasBg = TryColor(ctx, ThemeSlot.FabBackground, out var bg);
                bool hasIcon = TryColor(ctx, ThemeSlot.FabIcon, out var icon);
                bool hasBorder = TryColor(ctx, ThemeSlot.FabBorder, out var border);

                if (fab is Google.Android.Material.FloatingActionButton.ExtendedFloatingActionButton efab)
                {
                    if (hasBg) efab.BackgroundTintList = Csl(bg);
                    if (hasIcon) { efab.IconTint = Csl(icon); efab.SetTextColor(icon); }
                    if (hasBorder) { efab.StrokeColor = Csl(border); efab.StrokeWidth = Dp(ctx, 2); }
                }
                else if (fab is Google.Android.Material.FloatingActionButton.FloatingActionButton f)
                {
                    if (hasBg) f.BackgroundTintList = Csl(bg);
                    if (hasIcon) f.ImageTintList = Csl(icon);
                    if (hasBorder && Build.VERSION.SdkInt >= BuildVersionCodes.M)
                    {
                        var ring = new GradientDrawable();
                        ring.SetShape(ShapeType.Oval);
                        ring.SetColor(Color.Transparent);
                        ring.SetStroke(Dp(ctx, 2), border);
                        f.Foreground = ring;
                    }
                }
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Theme: ApplyFab failed: " + e);
            }
        }

        // ---- unlock screens (the collapsing banner above the password field) ----------------------

        /// <summary>
        /// Paints an unlock screen's collapsing banner in the unlock background: the blue artwork
        /// backdrop becomes a solid colour, the app bar and the scrims shown when the bar folds follow
        /// it, and the banner title takes the toolbar-title colour and font, expanded and collapsed.
        /// </summary>
        public static void ApplyUnlockBanner(Activity activity)
        {
            try
            {
                Context ctx = activity;
                bool hasBg = TryColor(ctx, ThemeSlot.UnlockBackground, out var bg);
                if (hasBg)
                {
                    activity.FindViewById<ImageView>(Resource.Id.backdrop)?.SetImageDrawable(new ColorDrawable(bg));
                    activity.FindViewById<View>(Resource.Id.appbar)?.SetBackgroundColor(bg);
                    // The status bar sits over the banner: the window colour (the theme leaves it at the
                    // Material primary) and the drawer's own status-bar strip both follow the banner.
                    activity.Window?.SetStatusBarColor(bg);
                    activity.FindViewById<AndroidX.DrawerLayout.Widget.DrawerLayout>(Resource.Id.drawer_layout)?.SetStatusBarBackgroundColor(bg);
                }

                var collapsing = activity.FindViewById<Google.Android.Material.AppBar.CollapsingToolbarLayout>(Resource.Id.collapsing_toolbar);
                if (collapsing == null)
                    return;
                if (hasBg)
                {
                    collapsing.SetContentScrimColor(bg);
                    collapsing.SetStatusBarScrimColor(bg);
                }
                if (TryColor(ctx, ThemeSlot.ToolbarTitle, out var title))
                {
                    collapsing.SetCollapsedTitleTextColor(title);
                    collapsing.SetExpandedTitleColor(title);
                }
                Android.Graphics.Typeface tf = SlotTypeface(ctx, ThemeColors.KeyOf(ThemeSlot.ToolbarTitle));
                if (tf != null)
                {
                    collapsing.CollapsedTitleTypeface = tf;
                    collapsing.ExpandedTitleTypeface = tf;
                }
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Theme: ApplyUnlockBanner failed: " + e);
            }
        }

        /// <summary>
        /// A button on an unlock screen: a filled button takes the unlock background as fill and an
        /// accent border; every button takes the unlock button-text colour and font, and any
        /// compound (drawableLeft) icon is tinted accent. Text buttons keep their transparent fill.
        /// </summary>
        public static void ApplyUnlockButton(Button button)
        {
            if (button == null)
                return;
            try
            {
                Context ctx = button.Context;
                bool hasAccent = TryColor(ctx, ThemeSlot.Accent, out var accent);
                bool filled = button is Google.Android.Material.Button.MaterialButton
                    && (((button.BackgroundTintList?.DefaultColor ?? 0) >> 24) & 0xFF) != 0;
                if (filled)
                {
                    if (TryColor(ctx, ThemeSlot.UnlockBackground, out var bg))
                        button.BackgroundTintList = Csl(bg);
                    if (hasAccent && button is Google.Android.Material.Button.MaterialButton mb)
                    {
                        mb.StrokeColor = Csl(accent);
                        mb.StrokeWidth = Dp(ctx, 2);
                    }
                }
                if (hasAccent)
                    AndroidX.Core.Widget.TextViewCompat.SetCompoundDrawableTintList(button, Csl(accent));
                ApplyColorAndFont(button, ThemeSlot.UnlockButtonText, ctx);
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Theme: ApplyUnlockButton failed: " + e);
            }
        }

        /// <summary>
        /// Walks an unlock-screen form and puts every element into the unlock slots: labels and
        /// checkbox captions (label), fields with hint/underline/cursor (password text + accent),
        /// buttons, checkbox boxes and icons (accent), spinners (themed rows and popup), and the
        /// 1–2 dp hairline rules (accent). Solid-fill images (the banner) are left alone.
        /// </summary>
        public static void ApplyUnlockForm(View root)
        {
            if (root == null)
                return;
            try
            {
                WalkUnlockForm(root, root.Context);
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Theme: ApplyUnlockForm failed: " + e);
            }
        }

        private static void WalkUnlockForm(View v, Context ctx)
        {
            bool hasAccent = TryColor(ctx, ThemeSlot.Accent, out var accent);
            switch (v)
            {
                case Spinner spinner:
                    ApplySpinner(spinner);
                    break;
                case EditText field:
                    ApplyColorAndFont(field, ThemeSlot.UnlockPasswordText, ctx);
                    if (TryColor(ctx, ThemeSlot.UnlockPasswordText, out var text))
                        field.SetHintTextColor(new Color(text.R, text.G, text.B, (byte)0x99));
                    if (hasAccent)
                    {
                        field.BackgroundTintList = Csl(accent);
                        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                            field.TextCursorDrawable?.SetTint(accent.ToArgb());
                    }
                    break;
                case CompoundButton toggle:
                    ApplyColorAndFont(toggle, ThemeSlot.UnlockLabel, ctx);
                    if (hasAccent)
                        toggle.ButtonTintList = Csl(accent);
                    break;
                case Button button:
                    ApplyUnlockButton(button);
                    break;
                case keepass2android.views.Kp2aShortHelpView help:
                    // colour only: its glyph comes from an icon font
                    if (TryColor(ctx, ThemeSlot.UnlockLabel, out var helpColor))
                        help.SetTextColor(helpColor);
                    break;
                case TextView label:
                    ApplyColorAndFont(label, ThemeSlot.UnlockLabel, ctx);
                    break;
                case ImageButton fp when fp.Id == Resource.Id.fingerprintbtn:
                    SetFingerprintIcon(fp, Resource.Drawable.baseline_fingerprint_24);
                    break;
                case ImageView image:
                    if (hasAccent && image.Drawable != null && !(image.Drawable is ColorDrawable))
                        image.SetColorFilter(accent);
                    break;
                case ViewGroup group:
                    for (int i = 0; i < group.ChildCount; i++)
                        WalkUnlockForm(group.GetChildAt(i), ctx);
                    break;
                default:
                    int h = v.LayoutParameters?.Height ?? 0;
                    if (hasAccent && v.Background is ColorDrawable && h > 0 && h <= Dp(ctx, 2))
                        v.SetBackgroundColor(accent);
                    break;
            }
        }

        /// <summary>
        /// The resting fingerprint icon takes the accent; the error/success icons keep their own
        /// red/green so the state still reads.
        /// </summary>
        public static void SetFingerprintIcon(ImageButton btn, int resId)
        {
            if (btn == null)
                return;
            try
            {
                btn.SetImageResource(resId);
                btn.ImageTintList = resId == Resource.Drawable.baseline_fingerprint_24 && TryColor(btn.Context, ThemeSlot.Accent, out var accent)
                    ? Csl(accent)
                    : null;
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Theme: SetFingerprintIcon failed: " + e);
            }
        }

        // ---- spinners -----------------------------------------------------------------------------

        /// <summary>
        /// Themes a spinner: the arrow takes the accent, the popup a black surface with an accent
        /// border, and the adapter is wrapped so the selected row and the dropdown rows render in
        /// the password-text colour/font. Wrapping resets the selection, so call this before the
        /// caller selects an item or attaches ItemSelected; a second call only refreshes the chrome.
        /// </summary>
        public static void ApplySpinner(Spinner spinner)
        {
            if (spinner == null)
                return;
            try
            {
                Context ctx = spinner.Context;
                bool hasAccent = TryColor(ctx, ThemeSlot.Accent, out var accent);
                if (hasAccent)
                    spinner.BackgroundTintList = Csl(accent);
                if (TryColor(ctx, ThemeSlot.UnlockBackground, out var bg))
                {
                    var popup = new GradientDrawable();
                    popup.SetColor(bg);
                    popup.SetCornerRadius(Dp(ctx, 8));
                    if (hasAccent)
                        popup.SetStroke(Dp(ctx, 2), accent);
                    spinner.SetPopupBackgroundDrawable(popup);
                }
                if (spinner.Adapter != null && !(spinner.Adapter is ThemedSpinnerAdapter))
                    spinner.Adapter = new ThemedSpinnerAdapter(spinner.Adapter);
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Theme: ApplySpinner failed: " + e);
            }
        }

        private sealed class ThemedSpinnerAdapter : BaseAdapter
        {
            private readonly ISpinnerAdapter _inner;

            public ThemedSpinnerAdapter(ISpinnerAdapter inner)
            {
                _inner = inner;
            }

            public override int Count => _inner.Count;
            public override Java.Lang.Object GetItem(int position) => _inner.GetItem(position);
            public override long GetItemId(int position) => _inner.GetItemId(position);

            public override View GetView(int position, View convertView, ViewGroup parent) =>
                Themed(_inner.GetView(position, convertView, parent));

            public override View GetDropDownView(int position, View convertView, ViewGroup parent) =>
                Themed(_inner.GetDropDownView(position, convertView, parent));

            private static View Themed(View row)
            {
                if (row is TextView tv)
                    ApplyColorAndFont(tv, ThemeSlot.UnlockPasswordText);
                return row;
            }
        }

        // ---- dialogs ------------------------------------------------------------------------------

        /// <summary>
        /// Themes an alert dialog after Show(): black rounded surface with an accent border,
        /// themed title/message text and accent buttons. No-op per element when a slot is unset.
        /// </summary>
        public static void ApplyAlertDialog(Dialog dialog)
        {
            if (dialog == null)
                return;
            try
            {
                Context ctx = dialog.Context;
                bool hasAccent = TryColor(ctx, ThemeSlot.Accent, out var accent);
                if (TryColor(ctx, ThemeSlot.PageBackground, out var bg))
                {
                    var shape = new GradientDrawable();
                    shape.SetColor(bg);
                    shape.SetCornerRadius(Dp(ctx, 28));
                    if (hasAccent)
                        shape.SetStroke(Dp(ctx, 2), accent);
                    dialog.Window?.SetBackgroundDrawable(shape);
                }
                if (TryColor(ctx, ThemeSlot.PageText, out var text))
                {
                    // AppCompat's title id, or the framework's internal one (ProgressDialog and
                    // other android.app dialogs).
                    var title = dialog.FindViewById<TextView>(Resource.Id.alertTitle);
                    if (title == null)
                    {
                        int frameworkTitle = ctx.Resources.GetIdentifier("alertTitle", "id", "android");
                        if (frameworkTitle != 0)
                            title = dialog.FindViewById<TextView>(frameworkTitle);
                    }
                    title?.SetTextColor(text);
                    var message = dialog.FindViewById<TextView>(Android.Resource.Id.Message);
                    message?.SetTextColor(text);
                }
                if (hasAccent && dialog is AndroidX.AppCompat.App.AlertDialog ad)
                {
                    ad.GetButton((int)Android.Content.DialogButtonType.Positive)?.SetTextColor(accent);
                    ad.GetButton((int)Android.Content.DialogButtonType.Negative)?.SetTextColor(accent);
                    ad.GetButton((int)Android.Content.DialogButtonType.Neutral)?.SetTextColor(accent);
                }
                if (hasAccent)
                    TintProgressBars(dialog.Window?.DecorView, accent);
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Theme: ApplyAlertDialog failed: " + e);
            }
        }

        /// <summary>The spinner of a progress dialog (and any other progress bar in the tree).</summary>
        private static void TintProgressBars(View v, Color accent)
        {
            if (v is ProgressBar pb)
            {
                pb.IndeterminateTintList = Csl(accent);
                pb.ProgressTintList = Csl(accent);
            }
            else if (v is ViewGroup vg)
            {
                for (int i = 0; i < vg.ChildCount; i++)
                    TintProgressBars(vg.GetChildAt(i), accent);
            }
        }

        // ---- settings pages (AndroidX preference screens) -----------------------------------------

        /// <summary>
        /// Themes a preference fragment's RecyclerView: rows visible now and every row the adapter
        /// attaches later (rows recycle, so a one-shot walk would miss them).
        /// </summary>
        public static void ApplyPreferenceList(AndroidX.RecyclerView.Widget.RecyclerView list)
        {
            if (list == null)
                return;
            try
            {
                list.AddOnChildAttachStateChangeListener(new PrefRowThemer());
                for (int i = 0; i < list.ChildCount; i++)
                    ApplyPreferenceRow(list.GetChildAt(i));
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Theme: ApplyPreferenceList failed: " + e);
            }
        }

        private sealed class PrefRowThemer : Java.Lang.Object,
            AndroidX.RecyclerView.Widget.RecyclerView.IOnChildAttachStateChangeListener
        {
            public void OnChildViewAttachedToWindow(View view) => ApplyPreferenceRow(view);
            public void OnChildViewDetachedFromWindow(View view) { }
        }

        /// <summary>One preference row: title/summary/icon; category headers have no summary view.</summary>
        public static void ApplyPreferenceRow(View row)
        {
            if (row == null)
                return;
            try
            {
                var title = row.FindViewById<TextView>(Android.Resource.Id.Title);
                var summary = row.FindViewById<TextView>(Android.Resource.Id.Summary);
                bool isCategory = summary == null;
                if (title != null)
                    ApplyColorAndFont(title, isCategory ? ThemeSlot.SettingsCategory : ThemeSlot.SettingsTitle);
                if (summary != null)
                    ApplyColorAndFont(summary, ThemeSlot.SettingsSubtitle);

                var icon = row.FindViewById<ImageView>(Android.Resource.Id.Icon);
                if (icon?.Drawable != null && TryColor(row.Context, ThemeSlot.SettingsTitle, out var iconColor))
                    icon.SetColorFilter(iconColor);

                ApplyToggleAccent(row);
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Theme: ApplyPreferenceRow failed: " + e);
            }
        }

        /// <summary>Accent the row's switch/checkbox: accent when checked, grey when off.</summary>
        private static void ApplyToggleAccent(View row)
        {
            var frame = row.FindViewById<ViewGroup>(Android.Resource.Id.WidgetFrame);
            if (frame == null || !TryColor(row.Context, ThemeSlot.Accent, out var accent))
                return;
            var off = new Color(unchecked((int)0xFF888888));
            for (int i = 0; i < frame.ChildCount; i++)
            {
                var child = frame.GetChildAt(i);
                if (child is AndroidX.AppCompat.Widget.SwitchCompat sw)
                {
                    sw.ThumbTintList = CheckedCsl(accent, off);
                    var trackOn = new Color(accent.R, accent.G, accent.B, (byte)0x66);
                    var trackOff = new Color(off.R, off.G, off.B, (byte)0x66);
                    sw.TrackTintList = CheckedCsl(trackOn, trackOff);
                }
                else if (child is CompoundButton cb)
                {
                    cb.ButtonTintList = CheckedCsl(accent, off);
                }
            }
        }

        private static ColorStateList CheckedCsl(Color on, Color off) =>
            new ColorStateList(
                new[] { new[] { Android.Resource.Attribute.StateChecked }, new int[0] },
                new[] { on.ToArgb(), off.ToArgb() });

        // ---- title row (the group-screen ActionBar's backing toolbar) -----------------------------

        public static void ApplyToolbarChrome(Activity activity, IMenu menu)
        {
            try
            {
                Context ctx = activity;
                bool hasBg = TryColor(ctx, ThemeSlot.TitlebarBackground, out var bg);
                bool hasText = TryColor(ctx, ThemeSlot.TitlebarText, out var text);
                bool hasIcon = TryColor(ctx, ThemeSlot.TitlebarIcon, out var icon);

                // Background: set on the ActionBar — its container holds the visible colour (the inner
                // toolbar is transparent over it, so toolbar.SetBackgroundColor alone is not enough).
                var ab = (activity as AndroidX.AppCompat.App.AppCompatActivity)?.SupportActionBar;
                if (ab != null && hasBg)
                    ab.SetBackgroundDrawable(new ColorDrawable(bg));
                if (hasBg)
                    activity.Window?.SetStatusBarColor(bg);

                // Title colour/font + overflow/navigation icons: on the backing toolbar.
                var toolbar = FindToolbar(activity.Window?.DecorView);
                if (toolbar != null)
                {
                    if (hasBg) toolbar.SetBackgroundColor(bg);
                    if (hasText) { toolbar.SetTitleTextColor(text.ToArgb()); ApplyTitleFont(toolbar); }
                    if (hasIcon)
                    {
                        Tint(toolbar.OverflowIcon, icon);
                        Tint(toolbar.NavigationIcon, icon);
                    }
                }

                // Menu item icons: tint and re-set so the displayed copy refreshes.
                if (hasIcon && menu != null)
                {
                    for (int i = 0; i < menu.Size(); i++)
                    {
                        var item = menu.GetItem(i);
                        var d = item?.Icon;
                        if (d != null) { d.Mutate(); d.SetTint(icon.ToArgb()); item.SetIcon(d); }
                    }
                }
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Theme: ApplyToolbarChrome failed: " + e);
            }
        }

        private static void Tint(Drawable d, Color color)
        {
            if (d == null)
                return;
            d.Mutate();
            d.SetTint(color.ToArgb());
        }

        private static AndroidX.AppCompat.Widget.Toolbar FindToolbar(View v)
        {
            if (v is AndroidX.AppCompat.Widget.Toolbar tb)
                return tb;
            if (v is ViewGroup vg)
            {
                int n = vg.ChildCount;
                for (int i = 0; i < n; i++)
                {
                    var r = FindToolbar(vg.GetChildAt(i));
                    if (r != null)
                        return r;
                }
            }
            return null;
        }

        private static void ApplyTitleFont(AndroidX.AppCompat.Widget.Toolbar tb)
        {
            for (int i = 0; i < tb.ChildCount; i++)
            {
                if (tb.GetChildAt(i) is TextView tv && tv.Text == tb.Title)
                {
                    ApplyFont(tv, ThemeColors.KeyOf(ThemeSlot.TitlebarText), tb.Context);
                    break;
                }
            }
        }
    }
}
