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
using Android.Views;
using Android.Widget;
using Google.Android.Material.Dialog;

namespace keepass2android.Theming
{
    /// <summary>
    /// The external-font picker: lists "System default", "Monospace" and every imported font,
    /// rendering each option's name IN ITS OWN TYPEFACE, plus an "Add font…" action.
    /// </summary>
    public static class FontPickerDialog
    {
        public static void Show(Context ctx, int previewWeight, Action<string> onPick, Action onAddFont)
        {
            var inflater = LayoutInflater.From(ctx);
            var view = inflater.Inflate(Resource.Layout.dialog_font_picker, null);
            var list = view.FindViewById<ListView>(Resource.Id.font_list);
            var addBtn = view.FindViewById<Button>(Resource.Id.add_font);

            var options = FontManager.AvailableFontOptions(ctx);
            list.Adapter = new FontOptionAdapter(ctx, options, previewWeight);

            var dlg = new MaterialAlertDialogBuilder(ctx)
                .SetTitle(Resource.String.theme_font_family)
                .SetView(view)
                .SetNegativeButton(Android.Resource.String.Cancel, (EventHandler<Android.Content.DialogClickEventArgs>)((s, e) => { }))
                .Create();

            list.ItemClick += (s, e) =>
            {
                var opt = options[e.Position];
                dlg.Dismiss();
                onPick(opt.Value);
            };
            addBtn.Click += (s, e) =>
            {
                dlg.Dismiss();
                onAddFont();
            };
            dlg.Show();
        }

        private sealed class FontOptionAdapter : BaseAdapter
        {
            private readonly Context _ctx;
            private readonly IList<FontOption> _options;
            private readonly int _weight;

            public FontOptionAdapter(Context ctx, IList<FontOption> options, int weight)
            {
                _ctx = ctx;
                _options = options;
                _weight = weight;
            }

            public override int Count => _options.Count;
            public override Java.Lang.Object GetItem(int position) => position;
            public override long GetItemId(int position) => position;

            public override View GetView(int position, View convertView, ViewGroup parent)
            {
                var v = convertView ?? LayoutInflater.From(_ctx).Inflate(Resource.Layout.item_font_option, parent, false);
                var tv = v.FindViewById<TextView>(Resource.Id.font_name);
                var opt = _options[position];
                tv.Text = opt.Label;
                tv.Typeface = FontManager.FontTypeface(_ctx, opt.Value, _weight);   // render the name in its own glyphs
                return v;
            }
        }
    }
}
