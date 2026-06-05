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
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Google.Android.Material.Dialog;

namespace keepass2android.Theming
{
    /// <summary>An alpha-capable colour picker (A/R/G/B sliders + hex), with a "Default" (clear) action.</summary>
    public static class ColorPickerDialog
    {
        public static void Show(Context ctx, int initialArgb, Action<int> onPicked, Action onCleared = null)
        {
            var view = LayoutInflater.From(ctx).Inflate(Resource.Layout.dialog_color_picker, null);
            var preview = view.FindViewById<View>(Resource.Id.color_preview);
            var hex = view.FindViewById<EditText>(Resource.Id.color_hex);
            var seekA = view.FindViewById<SeekBar>(Resource.Id.seek_a);
            var seekR = view.FindViewById<SeekBar>(Resource.Id.seek_r);
            var seekG = view.FindViewById<SeekBar>(Resource.Id.seek_g);
            var seekB = view.FindViewById<SeekBar>(Resource.Id.seek_b);

            int argb = (initialArgb == ThemeColors.ThemeUnset) ? unchecked((int)0xFF000000) : initialArgb;
            bool updating = false;

            void Render(bool moveSeeks)
            {
                updating = true;
                var c = new Color(argb);
                if (moveSeeks)
                {
                    seekA.Progress = c.A;
                    seekR.Progress = c.R;
                    seekG.Progress = c.G;
                    seekB.Progress = c.B;
                }
                preview.SetBackgroundColor(c);
                hex.Text = string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", c.A, c.R, c.G, c.B);
                updating = false;
            }

            void OnSeek(object s, SeekBar.ProgressChangedEventArgs e)
            {
                if (updating) return;
                argb = Color.Argb(seekA.Progress, seekR.Progress, seekG.Progress, seekB.Progress).ToArgb();
                Render(false);
            }

            seekA.ProgressChanged += OnSeek;
            seekR.ProgressChanged += OnSeek;
            seekG.ProgressChanged += OnSeek;
            seekB.ProgressChanged += OnSeek;

            hex.AfterTextChanged += (s, e) =>
            {
                if (updating) return;
                string t = hex.Text?.Trim().TrimStart('#');
                if (string.IsNullOrEmpty(t) || (t.Length != 6 && t.Length != 8)) return;
                try
                {
                    long val = Convert.ToInt64(t, 16);
                    argb = (t.Length == 6) ? unchecked((int)(0xFF000000 | (ulong)val)) : unchecked((int)val);
                    Render(true);
                }
                catch { /* incomplete hex; ignore */ }
            };

            Render(true);

            var builder = new MaterialAlertDialogBuilder(ctx)
                .SetTitle(Resource.String.theme_pick_color)
                .SetView(view)
                .SetPositiveButton(Android.Resource.String.Ok, (EventHandler<Android.Content.DialogClickEventArgs>)((s, e) => onPicked(argb)))
                .SetNegativeButton(Android.Resource.String.Cancel, (EventHandler<Android.Content.DialogClickEventArgs>)((s, e) => { }));

            if (onCleared != null)
                builder.SetNeutralButton(Resource.String.theme_default, (EventHandler<Android.Content.DialogClickEventArgs>)((s, e) => onCleared()));

            builder.Show();
        }
    }
}
