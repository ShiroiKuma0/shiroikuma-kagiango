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
            if (v is TextView tv)
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
            string family = ThemeConfig.GetFontFamily(ctx, slotKey);
            int weight = ThemeConfig.GetFontWeight(ctx, slotKey);
            int sizeSp = ThemeConfig.GetFontSizeSp(ctx, slotKey);

            if (string.IsNullOrEmpty(family))
            {
                // inherit the global app font when this slot has none of its own
                family = ThemeConfig.GetFontFamily(ctx, ThemeColors.AppFontKey);
                if (weight == 0)
                    weight = ThemeConfig.GetFontWeight(ctx, ThemeColors.AppFontKey);
            }

            if (!string.IsNullOrEmpty(family) || weight > 0)
            {
                Android.Graphics.Typeface tf = FontManager.FontTypeface(ctx, family, weight);
                TypefaceStyle style = tv.Typeface?.Style ?? TypefaceStyle.Normal;
                tv.SetTypeface(tf, style);
            }
            if (sizeSp > 0)
                tv.SetTextSize(Android.Util.ComplexUnitType.Sp, sizeSp);
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
