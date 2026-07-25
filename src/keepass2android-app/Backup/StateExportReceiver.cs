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
using System.Text;
using System.Threading;
using Android.App;
using Android.Content;
using Android.OS;

namespace keepass2android.Backup
{
    /// <summary>
    /// The sister-app <b>state-export automation contract</b> — the wire shape every 白い熊 app exposes so
    /// 自由作業盤's 保存復元 project can back them all up headlessly in one run.
    /// <list type="bullet">
    /// <item><c>&lt;pkg&gt;.action.EXPORT_STATE</c> — run the ordinary category-ZIP export
    /// (<see cref="Kp2aBackup"/>) with no Activity and no user interaction. Extras (all String):
    /// <c>token</c> (required), <c>path</c> (optional absolute directory, wins over the configured
    /// folder), <c>items</c> (optional comma list of category ids; absent/empty = everything),
    /// <c>progress_action</c> (optional), plus the reply trio <c>reply_action</c> / <c>reply_package</c> /
    /// <c>reply_id</c>.</item>
    /// <item><c>&lt;pkg&gt;.action.LIST_CATEGORIES</c> — token-gated, instant category enumeration for the
    /// caller's checkbox picker: <c>id&lt;TAB&gt;label</c> per line, with a third <c>parent-id</c> field on
    /// sub-options (this app's <c>fonts.files</c> sits under <c>fonts</c>).</item>
    /// </list>
    /// <para>
    /// <b>ONE ZIP per request</b>: the single file named by <see cref="Kp2aBackup.ExportFileName"/> is the
    /// whole backup, every selected category inside it. Nothing else is written beside it.
    /// </para>
    /// <para>
    /// The reply is a FRESH broadcast — never a binder. No <c>ResultReceiver</c>, no <c>PendingIntent</c>,
    /// no <c>Messenger</c>, and never a reliance on the ordered-broadcast result: EMUI severs both between
    /// third-party apps (verified on 白い熊's Mate XT, 2026-07-23). <c>FLAG_INCLUDE_STOPPED_PACKAGES</c> so a
    /// backgrounded or stopped caller still hears us, and exactly one terminal reply per request, guarded by
    /// <see cref="_replied"/>.
    /// </para>
    /// </summary>
    [BroadcastReceiver(Exported = true, Enabled = true)]
    [IntentFilter(new[] { StateExportReceiver.ActionExportState, StateExportReceiver.ActionListCategories })]
    public class StateExportReceiver : BroadcastReceiver
    {
        // Built the same way as Intents.LockDatabase: the fork's applicationId is shiroikuma.<PackagePart>,
        // so these are exactly the contract's <pkg>.action.* strings.
        public const string ActionExportState = "shiroikuma." + AppNames.PackagePart + ".action.EXPORT_STATE";
        public const string ActionListCategories = "shiroikuma." + AppNames.PackagePart + ".action.LIST_CATEGORIES";

        private const long ProgressThrottleMs = 500;

        /// <summary>Guards the single terminal reply, so an async success and a sync error can never race.</summary>
        private int _replied;

        public override void OnReceive(Context context, Intent intent)
        {
            string action = intent?.Action;
            if (action != ActionExportState && action != ActionListCategories)
                return;

            Context app = context.ApplicationContext;
            string replyAction = intent.GetStringExtra("reply_action");
            string replyPackage = intent.GetStringExtra("reply_package");
            string replyId = intent.GetStringExtra("reply_id");

            // Without somewhere to answer there is nothing useful to do — never act blind.
            if (string.IsNullOrEmpty(replyAction) || string.IsNullOrEmpty(replyPackage))
            {
                Kp2aLog.Log("Backup automation: request without reply_action/reply_package, ignored");
                return;
            }

            var pending = GoAsync();
            new Thread(() =>
            {
                try
                {
                    Handle(app, intent, action, replyAction, replyPackage, replyId);
                }
                catch (Exception e)
                {
                    Kp2aLog.Log("Backup automation: " + e);
                    Reply(app, replyAction, replyPackage, replyId, "ERROR:" + Short(e));
                }
                finally
                {
                    try { pending.Finish(); }
                    catch (Exception) { /* the broadcast is already done with */ }
                }
            }).Start();
        }

        private void Handle(Context app, Intent intent, string action,
            string replyAction, string replyPackage, string replyId)
        {
            // The switch and the token are checked separately so the two failures stay distinguishable.
            if (!AutomationAuth.IsEnabled(app))
            {
                Reply(app, replyAction, replyPackage, replyId, "ERROR:automation disabled");
                return;
            }
            if (!AutomationAuth.IsTokenValid(app, intent.GetStringExtra("token")))
            {
                Reply(app, replyAction, replyPackage, replyId, "ERROR:bad token");
                return;
            }
            // Our settings live in credential-encrypted storage: before the first unlock there is
            // genuinely nothing to read, and a half-empty ZIP would be worse than an honest error.
            if (!IsUserUnlocked(app))
            {
                Reply(app, replyAction, replyPackage, replyId, "ERROR:device-locked");
                return;
            }

            if (action == ActionListCategories)
            {
                Reply(app, replyAction, replyPackage, replyId, "OK:" + CategoryLines(app));
                return;
            }

            RunExport(app, intent, replyAction, replyPackage, replyId);
        }

        /// <summary><c>id&lt;TAB&gt;label</c> per line; sub-options add their parent's id as a third field.</summary>
        private static string CategoryLines(Context app)
        {
            var lines = new List<string>();
            foreach (var category in BackupCategory.All)
            {
                string line = category.Id + "\t" + app.GetString(category.LabelRes);
                if (category.ParentId != null)
                    line += "\t" + category.ParentId;
                lines.Add(line);
            }
            return string.Join("\n", lines);
        }

        private void RunExport(Context app, Intent intent,
            string replyAction, string replyPackage, string replyId)
        {
            // items: absent/empty means everything; every id must be one we actually export.
            string items = intent.GetStringExtra("items");
            List<string> categories;
            if (string.IsNullOrWhiteSpace(items))
            {
                categories = BackupCategory.AllIds.ToList();
            }
            else
            {
                categories = items.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                var unknown = categories.Where(id => !BackupCategory.IsKnownId(id)).ToList();
                if (unknown.Count > 0)
                {
                    Reply(app, replyAction, replyPackage, replyId,
                        "ERROR:unknown category in items: " + string.Join(",", unknown));
                    return;
                }
            }

            // Directory precedence: the path extra, then the app's configured folder, then an error.
            string dir = intent.GetStringExtra("path");
            if (string.IsNullOrWhiteSpace(dir))
                dir = BackupConfig.GetExportDir(app);
            if (string.IsNullOrWhiteSpace(dir))
            {
                Reply(app, replyAction, replyPackage, replyId, "ERROR:no-directory");
                return;
            }
            dir = dir.Trim();

            // We declare MANAGE_EXTERNAL_STORAGE precisely so an arbitrary absolute path can be written.
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R && !Android.OS.Environment.IsExternalStorageManager)
            {
                Reply(app, replyAction, replyPackage, replyId, "ERROR:no-storage-access");
                return;
            }

            string progressAction = intent.GetStringExtra("progress_action");
            string appLabel = app.GetString(AppNames.AppNameResource);
            long lastProgress = 0;

            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, Kp2aBackup.ExportFileName(DateTime.Now));

            BackupResult result;
            using (var stream = File.Create(path))
            {
                result = Kp2aBackup.Export(app, categories, stream, (done, total, label) =>
                {
                    if (string.IsNullOrEmpty(progressAction))
                        return;
                    // Real counts, never a percentage — throttled, but the final one always goes out.
                    long now = Java.Lang.JavaSystem.CurrentTimeMillis();
                    if (done < total && now - lastProgress < ProgressThrottleMs)
                        return;
                    lastProgress = now;
                    SendProgress(app, progressAction, replyPackage, replyId, appLabel,
                        string.Format(app.GetString(Resource.String.backup_progress), done, total, label),
                        done, total, app.GetString(Resource.String.backup_progress_unit));
                });
            }

            long size = new FileInfo(path).Length;
            Reply(app, replyAction, replyPackage, replyId,
                "OK:" + path + "|" + size + "|" + Kp2aBackup.HumanSize(size) + "|" +
                result.CategoryCount + " categories");
        }

        // ---- the reply / progress channel --------------------------------------------------------------

        private void Reply(Context app, string replyAction, string replyPackage, string replyId, string result)
        {
            // Exactly one terminal reply per request.
            if (Interlocked.Exchange(ref _replied, 1) != 0)
                return;
            try
            {
                var intent = new Intent(replyAction);
                intent.SetPackage(replyPackage);
                intent.AddFlags(ActivityFlags.IncludeStoppedPackages);
                intent.PutExtra("reply_id", replyId);
                intent.PutExtra("result", result);
                app.SendBroadcast(intent);
                Kp2aLog.Log("Backup automation: replied " + Truncate(result));
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Backup automation: reply failed: " + e);
            }
        }

        private static void SendProgress(Context app, string progressAction, string replyPackage, string replyId,
            string appLabel, string text, long current, long total, string unit)
        {
            try
            {
                var intent = new Intent(progressAction);
                intent.SetPackage(replyPackage);
                intent.AddFlags(ActivityFlags.IncludeStoppedPackages);
                intent.PutExtra("reply_id", replyId);
                intent.PutExtra("app", appLabel);
                intent.PutExtra("text", text);
                intent.PutExtra("current", current);
                intent.PutExtra("total", total);
                intent.PutExtra("unit", unit);
                app.SendBroadcast(intent);
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Backup automation: progress failed: " + e);
            }
        }

        private static bool IsUserUnlocked(Context app)
        {
            try
            {
                if (Build.VERSION.SdkInt < BuildVersionCodes.N)
                    return true;
                var manager = (UserManager)app.GetSystemService(Context.UserService);
                return manager == null || manager.IsUserUnlocked;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static string Short(Exception e) =>
            (e.Message ?? e.GetType().Name).Replace('\n', ' ').Trim();

        private static string Truncate(string value) =>
            value.Length <= 160 ? value : value.Substring(0, 160) + "…";
    }
}
