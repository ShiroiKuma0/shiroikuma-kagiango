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
using System.IO;
using System.Linq;
using System.Threading;
using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using Google.Android.Material.Dialog;
using keepass2android.Theming;

namespace keepass2android.Backup
{
    /// <summary>
    /// The Export / Import panel: one bordered black-and-yellow box carrying the whole flow — title,
    /// description, the tappable backup-folder box (warn-red until 白い熊 sets one), the newest-backup
    /// line queried when the panel opens, then 全選択 plus the category checkboxes (sub-options indented
    /// under their parent), and finally the ArcaneChat button bar: Cancel alone on the left, Import and
    /// Export grouped on the right, all as round pills.
    /// <para>
    /// Dismissal chain (白い熊): acknowledging a <b>successful</b> export or import closes the info dialog,
    /// this panel and the UI settings page underneath it in one go — the job is done, so the whole stack
    /// gets out of the way. Failures ("Export failed…", "No categories selected.") only close the info
    /// dialog, leaving the panel open so the problem can be fixed and retried.
    /// </para>
    /// </summary>
    public sealed class ExportImportDialog
    {
        private readonly Activity _activity;
        private readonly Action _onChainFinished;
        private readonly HashSet<string> _selected;

        private AndroidX.AppCompat.App.AlertDialog _dialog;
        private LinearLayout _box;
        private string _exportDir;

        private readonly Color _accent;
        private readonly Color _background;
        private readonly Color _dim;
        private static readonly Color Warn = new Color(unchecked((int)0xFFFF6666));

        private ExportImportDialog(Activity activity, Action onChainFinished)
        {
            _activity = activity;
            _onChainFinished = onChainFinished;
            // Seeded from the categories' own DefaultSelected flag — the very same answer LIST_CATEGORIES
            // hands 保存復元, so the in-app sheet and the automation picker open on one set of ticks.
            _selected = new HashSet<string>(BackupCategory.DefaultIds);

            _accent = Kp2aTheme.TryColor(activity, ThemeSlot.Accent, out var a)
                ? a : new Color(unchecked((int)0xFFFFFF00));
            _background = Kp2aTheme.TryColor(activity, ThemeSlot.PageBackground, out var b)
                ? b : new Color(unchecked((int)0xFF000000));
            _dim = new Color(Color.Argb(0xCC, _accent.R, _accent.G, _accent.B));
        }

        /// <param name="onChainFinished">
        /// Run when a successful export/import is acknowledged: closes the UI settings page behind us.
        /// </param>
        /// <param name="onDismissed">Run whenever the panel closes, so the page can re-read the folder.</param>
        public static void Show(Activity activity, Action onChainFinished, Action onDismissed = null)
        {
            var panel = new ExportImportDialog(activity, onChainFinished);
            panel.BuildPanel(onDismissed);
        }

        /// <summary>
        /// Set the backup folder straight from the UI page's folder row, without opening the panel.
        /// </summary>
        public static void EditDirectory(Activity activity, Action onChanged)
        {
            var panel = new ExportImportDialog(activity, null) { _onDirChanged = onChanged };
            panel._exportDir = BackupConfig.GetExportDir(activity);
            panel.EditDir();
        }

        private Action _onDirChanged;

        private void BuildPanel(Action onDismissed)
        {
            var scroll = new ScrollView(_activity);
            _box = new LinearLayout(_activity) { Orientation = Android.Widget.Orientation.Vertical };
            _box.SetPadding(Dp(18), Dp(14), Dp(18), Dp(16));
            scroll.AddView(_box);

            _dialog = new MaterialAlertDialogBuilder(_activity).SetView(scroll).Create();
            if (onDismissed != null)
                _dialog.DismissEvent += (s, e) => onDismissed();
            _dialog.Show();
            Kp2aTheme.ApplyAlertDialog(_dialog);
            Rebuild();
        }

        /// <summary>Re-render the panel — the folder, the newest backup and the ticks are all re-read.</summary>
        private void Rebuild()
        {
            _exportDir = BackupConfig.GetExportDir(_activity);
            _box.RemoveAllViews();

            _box.AddView(Heading(_activity.GetString(Resource.String.backup_title)));
            _box.AddView(Caption(_activity.GetString(Resource.String.backup_intro), bottomGap: 10));

            if (!HasAllFilesAccess())
            {
                _box.AddView(Caption(_activity.GetString(Resource.String.backup_need_access), Warn));
                _box.AddView(PillButton(_activity.GetString(Resource.String.backup_grant_access),
                    RequestAllFilesAccess));
                _box.AddView(Spacer(6));
            }

            _box.AddView(DirRow());
            _box.AddView(StatusLine());

            _box.AddView(Divider());
            _box.AddView(SelectAllRow());
            foreach (var category in BackupCategory.All.Where(c => c.ParentId == null))
            {
                _box.AddView(CategoryRow(category, indent: 0));
                foreach (var child in BackupCategory.Children(category.Id))
                    _box.AddView(CategoryRow(child, indent: 1));
            }

            _box.AddView(Divider(topGap: 8));
            _box.AddView(ActionRow());
        }

        // ---- rows ------------------------------------------------------------------------------------

        /// <summary>The folder box: a small label over the bold value — warn-red until a folder is set.</summary>
        private View DirRow()
        {
            var row = new LinearLayout(_activity) { Orientation = Android.Widget.Orientation.Vertical, Clickable = true, Focusable = true };
            row.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));
            var shape = new GradientDrawable();
            shape.SetColor(_background);
            shape.SetStroke(Dp(2), _accent);
            shape.SetCornerRadius(Dp(10));
            row.Background = shape;
            row.Click += (s, e) => EditDir();

            var label = new TextView(_activity) { Text = _activity.GetString(Resource.String.backup_dir_label) };
            label.SetTextColor(_accent);
            label.SetTextSize(Android.Util.ComplexUnitType.Sp, 12);
            row.AddView(label);

            var value = new TextView(_activity)
            {
                Text = _exportDir ?? _activity.GetString(Resource.String.backup_dir_unset)
            };
            value.SetTextColor(_exportDir == null ? Warn : _dim);
            value.SetTypeface(value.Typeface, TypefaceStyle.Bold);
            value.SetTextSize(Android.Util.ComplexUnitType.Sp, 15);
            row.AddView(value);

            row.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent) { TopMargin = Dp(6), BottomMargin = Dp(6) };
            return row;
        }

        /// <summary>When the newest backup in the folder was written — red while there is nothing to say.</summary>
        private View StatusLine()
        {
            string text;
            bool warn;
            if (_exportDir == null)
            {
                text = _activity.GetString(Resource.String.backup_last_nodir);
                warn = true;
            }
            else
            {
                var newest = Kp2aBackup.ListBackups(_exportDir).FirstOrDefault();
                if (newest == null)
                {
                    text = _activity.GetString(Resource.String.backup_last_none);
                    warn = true;
                }
                else
                {
                    string stamp = newest.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
                    text = string.Format(_activity.GetString(Resource.String.backup_last), stamp,
                        Kp2aBackup.HumanSize(newest.Length));
                    warn = false;
                }
            }
            var view = Caption(text, warn ? Warn : _dim);
            view.SetPadding(Dp(2), 0, 0, Dp(8));
            return view;
        }

        private View SelectAllRow()
        {
            var box = Checkbox(_activity.GetString(Resource.String.backup_select_all), bold: true);
            box.Checked = _selected.Count == BackupCategory.All.Length;
            box.Click += (s, e) =>
            {
                _selected.Clear();
                if (box.Checked)
                    foreach (string id in BackupCategory.AllIds)
                        _selected.Add(id);
                Rebuild();
            };
            return box;
        }

        private View CategoryRow(BackupCategory category, int indent)
        {
            var box = Checkbox(_activity.GetString(category.LabelRes));
            box.Checked = _selected.Contains(category.Id);
            box.SetPadding(Dp(8) + indent * Dp(24), Dp(7), 0, Dp(7));
            box.Click += (s, e) =>
            {
                if (box.Checked)
                    _selected.Add(category.Id);
                else
                    _selected.Remove(category.Id);
                // A parent carries its sub-options with it; each remains selectable on its own.
                foreach (var child in BackupCategory.Children(category.Id))
                {
                    if (box.Checked)
                        _selected.Add(child.Id);
                    else
                        _selected.Remove(child.Id);
                }
                Rebuild();
            };
            return box;
        }

        /// <summary>The ArcaneChat button bar: Cancel alone on the left, Import + Export on the right.</summary>
        private View ActionRow()
        {
            var row = new LinearLayout(_activity) { Orientation = Android.Widget.Orientation.Horizontal };
            row.SetPadding(0, Dp(14), 0, 0);

            row.AddView(PillButton(_activity.GetString(Resource.String.backup_cancel), () => _dialog?.Dismiss()));

            var gap = new View(_activity) { LayoutParameters = new LinearLayout.LayoutParams(0, 0, 1f) };
            row.AddView(gap);

            var import = PillButton(_activity.GetString(Resource.String.backup_import), OnImport);
            ((LinearLayout.LayoutParams)import.LayoutParameters).MarginEnd = Dp(8);
            row.AddView(import);
            row.AddView(PillButton(_activity.GetString(Resource.String.backup_export), OnExport));
            return row;
        }

        // ---- export ----------------------------------------------------------------------------------

        private void OnExport()
        {
            if (!EnsureReady())
                return;
            if (_selected.Count == 0)
            {
                ShowInfo(Resource.String.backup_export_failed_title,
                    _activity.GetString(Resource.String.backup_none_selected), closeChain: false);
                return;
            }

            var categories = _selected.ToList();
            string dir = _exportDir;

            RunInBackground(() =>
            {
                // The same writer the automation path uses: built under a .part name and renamed only once
                // whole, so a failure here leaves the backup folder exactly as it found it.
                var result = Kp2aBackup.ExportToDirectory(_activity, categories, dir);
                string name = System.IO.Path.GetFileName(result.Path);
                return (Func<string>)(() => string.Format(
                    _activity.GetString(Resource.String.backup_export_done),
                    name, Kp2aBackup.HumanSize(result.Size), result.CategoryCount) + ErrorSuffix(result));
            },
            onDone: body => ShowInfo(Resource.String.backup_export_done_title, body, closeChain: true),
            onFailed: message => ShowInfo(Resource.String.backup_export_failed_title,
                string.Format(_activity.GetString(Resource.String.backup_export_failed), message),
                closeChain: false));
        }

        // ---- import ----------------------------------------------------------------------------------

        private void OnImport()
        {
            if (!EnsureReady())
                return;
            if (_selected.Count == 0)
            {
                ShowInfo(Resource.String.backup_import_failed_title,
                    _activity.GetString(Resource.String.backup_none_selected), closeChain: false);
                return;
            }

            var backups = Kp2aBackup.ListBackups(_exportDir);
            if (backups.Count == 0)
            {
                ShowInfo(Resource.String.backup_import_failed_title,
                    _activity.GetString(Resource.String.backup_no_backups), closeChain: false);
                return;
            }

            var names = backups.Select(f => f.Name).ToArray();
            var picker = new MaterialAlertDialogBuilder(_activity)
                .SetTitle(Resource.String.backup_pick)
                .SetItems(names, (s, e) => RunImport(backups[e.Which].FullName))
                .SetNegativeButton(Resource.String.backup_cancel,
                    (EventHandler<DialogClickEventArgs>)((s, e) => { }))
                .Create();
            picker.Show();
            Kp2aTheme.ApplyAlertDialog(picker);
        }

        private void RunImport(string path)
        {
            var categories = _selected.ToList();
            RunInBackground(() =>
            {
                BackupResult result;
                using (var stream = File.OpenRead(path))
                    result = Kp2aBackup.Import(_activity, categories, stream);
                string body = string.Join("\n", result.Lines);
                if (body.Length == 0)
                    body = _activity.GetString(Resource.String.backup_import_nothing);
                return (Func<string>)(() => body + ErrorSuffix(result) + "\n\n" +
                    _activity.GetString(Resource.String.backup_restart_hint));
            },
            onDone: ShowImportDone,
            onFailed: message => ShowInfo(Resource.String.backup_import_failed_title,
                string.Format(_activity.GetString(Resource.String.backup_import_failed), message),
                closeChain: false));
        }

        /// <summary>
        /// The import result. "Restart now" restarts the app so every restored setting is re-read;
        /// "Later" just acknowledges — and either way the whole stack closes behind it.
        /// </summary>
        private void ShowImportDone(string body)
        {
            var dialog = new MaterialAlertDialogBuilder(_activity)
                .SetTitle(Resource.String.backup_import_done_title)
                .SetMessage(body)
                .SetCancelable(false)
                .SetPositiveButton(Resource.String.backup_restart_now,
                    (EventHandler<DialogClickEventArgs>)((s, e) => RestartApp()))
                .SetNegativeButton(Resource.String.backup_later,
                    (EventHandler<DialogClickEventArgs>)((s, e) => CloseChain()))
                .Create();
            dialog.Show();
            Kp2aTheme.ApplyAlertDialog(dialog);
        }

        private void RestartApp()
        {
            try
            {
                var intent = _activity.PackageManager.GetLaunchIntentForPackage(_activity.PackageName);
                intent?.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask);
                if (intent != null)
                    _activity.StartActivity(intent);
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Backup: restart failed: " + e);
            }
            // The database must not survive as a half-restored session: drop the process outright.
            Android.OS.Process.KillProcess(Android.OS.Process.MyPid());
        }

        // ---- info dialog ------------------------------------------------------------------------------

        /// <summary>
        /// The black-and-yellow acknowledgement box (yellow border via <see cref="Kp2aTheme.ApplyAlertDialog"/>).
        /// When <paramref name="closeChain"/> is set, OK also dismisses the panel and finishes the UI page.
        /// </summary>
        private void ShowInfo(int titleRes, string body, bool closeChain)
        {
            var dialog = new MaterialAlertDialogBuilder(_activity)
                .SetTitle(titleRes)
                .SetMessage(body)
                .SetCancelable(!closeChain)
                .SetPositiveButton(Android.Resource.String.Ok, (EventHandler<DialogClickEventArgs>)((s, e) =>
                {
                    if (closeChain)
                        CloseChain();
                }))
                .Create();
            dialog.Show();
            Kp2aTheme.ApplyAlertDialog(dialog);
        }

        /// <summary>Close the info dialog's panel and the UI settings page beneath it.</summary>
        private void CloseChain()
        {
            try { _dialog?.Dismiss(); }
            catch (Exception) { /* already gone */ }
            _onChainFinished?.Invoke();
        }

        private string ErrorSuffix(BackupResult result) =>
            result.Errors.Count == 0 ? "" : "\n\n⚠ " + string.Join(", ", result.Errors);

        // ---- folder ------------------------------------------------------------------------------------

        private bool EnsureReady()
        {
            if (!HasAllFilesAccess())
            {
                RequestAllFilesAccess();
                return false;
            }
            if (_exportDir == null)
            {
                EditDir();
                return false;
            }
            return true;
        }

        private void EditDir()
        {
            var input = new EditText(_activity)
            {
                Text = _exportDir ?? "",
                Hint = _activity.GetString(Resource.String.backup_dir_hint)
            };
            input.SetSingleLine();
            var frame = new FrameLayout(_activity);
            frame.SetPadding(Dp(20), Dp(8), Dp(20), 0);
            frame.AddView(input);

            var dialog = new MaterialAlertDialogBuilder(_activity)
                .SetTitle(Resource.String.backup_dir_dialog_title)
                .SetMessage(Resource.String.backup_dir_dialog_message)
                .SetView(frame)
                .SetPositiveButton(Resource.String.backup_dir_save,
                    (EventHandler<DialogClickEventArgs>)((s, e) => SaveDir(input.Text)))
                .SetNeutralButton(Resource.String.backup_dir_browse,
                    (EventHandler<DialogClickEventArgs>)((s, e) =>
                    {
                        var start = _exportDir != null && Directory.Exists(_exportDir)
                            ? new DirectoryInfo(_exportDir)
                            : new DirectoryInfo(Android.OS.Environment.ExternalStorageDirectory.AbsolutePath);
                        BrowseForFolder(start, picked => SaveDir(picked.FullName));
                    }))
                .SetNegativeButton(Resource.String.backup_cancel,
                    (EventHandler<DialogClickEventArgs>)((s, e) => { }))
                .Create();
            dialog.Show();
            Kp2aTheme.ApplyAlertDialog(dialog);
        }

        private void SaveDir(string path)
        {
            BackupConfig.SetExportDir(_activity, path);
            _exportDir = BackupConfig.GetExportDir(_activity);
            if (_box != null)
                Rebuild();
            _onDirChanged?.Invoke();
        }

        private void BrowseForFolder(DirectoryInfo dir, Action<DirectoryInfo> onPick)
        {
            var labels = new List<string>();
            var targets = new List<DirectoryInfo>();
            labels.Add("✓ " + _activity.GetString(Resource.String.backup_dir_pick_here));
            targets.Add(null);
            if (dir.Parent != null)
            {
                labels.Add(".. (" + dir.Parent.Name + ")");
                targets.Add(dir.Parent);
            }
            try
            {
                foreach (var sub in dir.GetDirectories().OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    labels.Add("📁 " + sub.Name);
                    targets.Add(sub);
                }
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Backup: cannot list " + dir.FullName + ": " + e);
            }

            var dialog = new MaterialAlertDialogBuilder(_activity)
                .SetTitle(dir.FullName)
                .SetItems(labels.ToArray(), (s, e) =>
                {
                    var target = targets[e.Which];
                    if (target == null)
                        onPick(dir);
                    else
                        BrowseForFolder(target, onPick);
                })
                .SetNegativeButton(Resource.String.backup_cancel,
                    (EventHandler<DialogClickEventArgs>)((s, e) => { }))
                .Create();
            dialog.Show();
            Kp2aTheme.ApplyAlertDialog(dialog);
        }

        private bool HasAllFilesAccess() =>
            Build.VERSION.SdkInt < BuildVersionCodes.R || Android.OS.Environment.IsExternalStorageManager;

        private void RequestAllFilesAccess()
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.R)
                return;
            try
            {
                _activity.StartActivity(new Intent(Settings.ActionManageAppAllFilesAccessPermission,
                    Android.Net.Uri.Parse("package:" + _activity.PackageName)));
            }
            catch (Exception)
            {
                try
                {
                    _activity.StartActivity(new Intent(Settings.ActionManageAllFilesAccessPermission));
                }
                catch (Exception e)
                {
                    Kp2aLog.Log("Backup: cannot open all-files-access settings: " + e);
                    Toast.MakeText(_activity, Resource.String.backup_need_access, ToastLength.Long).Show();
                }
            }
        }

        // ---- plumbing ----------------------------------------------------------------------------------

        /// <summary>Run the archive work off the UI thread, then report back on it.</summary>
        private void RunInBackground(Func<Func<string>> work, Action<string> onDone, Action<string> onFailed)
        {
            new Thread(() =>
            {
                try
                {
                    var body = work();
                    _activity.RunOnUiThread(() => onDone(body()));
                }
                catch (Exception e)
                {
                    Kp2aLog.Log("Backup: " + e);
                    _activity.RunOnUiThread(() => onFailed(e.Message ?? e.GetType().Name));
                }
            }).Start();
        }

        // ---- view builders -----------------------------------------------------------------------------

        private TextView Heading(string text)
        {
            var view = new TextView(_activity) { Text = text, Gravity = GravityFlags.Center };
            view.SetTextColor(_accent);
            view.SetTypeface(view.Typeface, TypefaceStyle.Bold);
            view.SetTextSize(Android.Util.ComplexUnitType.Sp, 18);
            view.SetPadding(0, Dp(2), 0, Dp(6));
            return view;
        }

        private TextView Caption(string text, Color? color = null, int bottomGap = 0)
        {
            var view = new TextView(_activity) { Text = text };
            view.SetTextColor(color ?? _dim);
            view.SetTextSize(Android.Util.ComplexUnitType.Sp, 13);
            view.SetPadding(0, 0, 0, Dp(bottomGap));
            return view;
        }

        private View Divider(int topGap = 0)
        {
            var view = new View(_activity)
            {
                LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(1))
                    { TopMargin = Dp(topGap) },
                Alpha = 0.4f
            };
            view.SetBackgroundColor(_accent);
            return view;
        }

        private CheckBox Checkbox(string text, bool bold = false)
        {
            var box = new CheckBox(_activity) { Text = text };
            box.SetTextColor(_accent);
            if (bold)
                box.SetTypeface(box.Typeface, TypefaceStyle.Bold);
            box.SetTextSize(Android.Util.ComplexUnitType.Sp, 15);
            box.ButtonTintList = ColorStateList.ValueOf(_accent);
            box.SetPadding(Dp(8), Dp(7), 0, Dp(7));
            return box;
        }

        private View Spacer(int height) => new View(_activity)
        {
            LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(height))
        };

        /// <summary>An ArcaneChat round pill: black fill, thin accent stroke, accent text and ripple.</summary>
        private Button PillButton(string text, Action onClick)
        {
            var button = new Button(_activity) { Text = text };
            button.SetAllCaps(false);
            button.SetTextColor(_accent);

            var shape = new GradientDrawable();
            shape.SetColor(_background);
            shape.SetStroke((int)(1.5f * _activity.Resources.DisplayMetrics.Density), _accent);
            shape.SetCornerRadius(Dp(50));
            button.Background = new RippleDrawable(
                ColorStateList.ValueOf(new Color(Color.Argb(0x33, _accent.R, _accent.G, _accent.B))),
                shape, null);

            // Explicit padding and zeroed minimums so the rounded stroke is never clipped at the edge.
            button.SetMinHeight(0);
            button.SetMinimumHeight(0);
            button.SetPadding(Dp(20), Dp(6), Dp(20), Dp(6));
            button.StateListAnimator = null;
            button.Click += (s, e) => onClick();
            button.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent) { TopMargin = Dp(8), Gravity = GravityFlags.Center };
            return button;
        }

        private int Dp(int value) => (int)(value * _activity.Resources.DisplayMetrics.Density + 0.5f);
    }
}
