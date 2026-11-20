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
using Android.Graphics.Drawables;
using Android.Views;
using Java.Lang;
using SupportActionMode = AndroidX.AppCompat.View.ActionMode;
using SupportToolbar = AndroidX.AppCompat.Widget.Toolbar;

namespace keepass2android.Theming
{
    /// <summary>
    /// Fork (白い熊 鍵暗号 UI): the multi-select action mode of the group list, drawn into a toolbar that
    /// sits below the title bar instead of AppCompat's own action-mode bar. The stock bar is inserted above
    /// the content, which pushes the whole title row down while entries are selected; this one leaves the
    /// title row where it is and only shows the edit / delete / copy / move icons underneath it.
    /// </summary>
    public class SelectionBarActionMode : SupportActionMode
    {
        private readonly Android.App.Activity _activity;
        private readonly SupportToolbar _bar;
        private readonly ICallback _callback;
        private ICharSequence _title, _subtitle;
        private View _customView;
        private bool _finished;

        private SelectionBarActionMode(Android.App.Activity activity, SupportToolbar bar, ICallback callback)
        {
            _activity = activity;
            _bar = bar;
            _callback = callback;
        }

        /// <summary>Starts the mode, or returns null when the callback declines (AppCompat then falls back).</summary>
        public static SelectionBarActionMode Start(Android.App.Activity activity, SupportToolbar bar, ICallback callback)
        {
            var mode = new SelectionBarActionMode(activity, bar, callback);
            bar.Menu.Clear();
            if (!callback.OnCreateActionMode(mode, bar.Menu))
            {
                bar.Menu.Clear();
                return null;
            }
            bar.SetNavigationIcon(Resource.Drawable.baseline_close_24);
            bar.NavigationClick += mode.OnNavigationClick;
            bar.MenuItemClick += mode.OnMenuItemClick;
            mode.Invalidate();
            bar.Visibility = ViewStates.Visible;
            return mode;
        }

        private void OnNavigationClick(object sender, SupportToolbar.NavigationClickEventArgs e) => Finish();

        private void OnMenuItemClick(object sender, SupportToolbar.MenuItemClickEventArgs e)
        {
            e.Handled = !_finished && _callback.OnActionItemClicked(this, e.Item);
        }

        private void ApplyColors()
        {
            try
            {
                var ctx = _bar.Context;
                if (Kp2aTheme.TryColor(ctx, ThemeSlot.TitlebarBackground, out var bg))
                    _bar.SetBackgroundColor(bg);
                if (Kp2aTheme.TryColor(ctx, ThemeSlot.TitlebarIcon, out var icon))
                {
                    TintIcon(_bar.NavigationIcon, icon);
                    TintIcon(_bar.OverflowIcon, icon);
                    var menu = _bar.Menu;
                    for (int i = 0; i < menu.Size(); i++)
                    {
                        var item = menu.GetItem(i);
                        var d = item?.Icon;
                        if (d != null) { d.Mutate(); d.SetTint(icon.ToArgb()); item.SetIcon(d); }
                    }
                }
                if (Kp2aTheme.TryColor(ctx, ThemeSlot.TitlebarText, out var text))
                {
                    _bar.SetTitleTextColor(text.ToArgb());
                    _bar.SetSubtitleTextColor(text.ToArgb());
                }
            }
            catch (System.Exception ex)
            {
                Kp2aLog.Log("Theme: selection bar colours failed: " + ex);
            }
        }

        private static void TintIcon(Drawable d, Android.Graphics.Color color)
        {
            if (d == null) return;
            d.Mutate();
            d.SetTint(color.ToArgb());
        }

        public override void Invalidate()
        {
            if (_finished) return;
            _callback.OnPrepareActionMode(this, _bar.Menu);
            ApplyColors();
        }

        public override void Finish()
        {
            if (_finished) return;
            _finished = true;
            _bar.NavigationClick -= OnNavigationClick;
            _bar.MenuItemClick -= OnMenuItemClick;
            _bar.Visibility = ViewStates.Gone;
            _callback.OnDestroyActionMode(this);
            _bar.Menu.Clear();
            _bar.NavigationIcon = null;
            _bar.TitleFormatted = null;
            _bar.SubtitleFormatted = null;
        }

        public override IMenu Menu => _bar.Menu;

        public override MenuInflater MenuInflater => _activity.MenuInflater;

        public override View CustomView
        {
            get => _customView;
            set => _customView = value;
        }

        public override ICharSequence TitleFormatted
        {
            get => _title;
            set { _title = value; _bar.TitleFormatted = value; }
        }

        public override ICharSequence SubtitleFormatted
        {
            get => _subtitle;
            set { _subtitle = value; _bar.SubtitleFormatted = value; }
        }

        public override void SetTitle(int resId) => TitleFormatted = new Java.Lang.String(_activity.GetString(resId));

        public override void SetSubtitle(int resId) => SubtitleFormatted = new Java.Lang.String(_activity.GetString(resId));
    }
}
