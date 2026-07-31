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
    /// caller's checkbox picker: <c>id&lt;TAB&gt;label&lt;TAB&gt;parent&lt;TAB&gt;on|off</c> per line. The
    /// third field is the parent id on sub-options (this app's <c>fonts.files</c> sits under <c>fonts</c>)
    /// and empty otherwise; the fourth says whether the item starts ticked, so the caller's picker opens on
    /// <b>our</b> answer rather than guessing.</item>
    /// <item><c>&lt;pkg&gt;.action.CANCEL_EXPORT</c> — stop the export that is running. Extras: <c>token</c>
    /// (required) and an optional <c>reply_id</c> (absent = whatever is running, unambiguous because two at
    /// once are forbidden). It <b>sends no reply of its own</b> — fire-and-forget — and is a silent no-op
    /// when nothing is running or the export already finished, so it is safe to send at any time. The
    /// cancelled export unwinds at the next entry boundary, deletes its partial file, and answers its own
    /// original request with <c>ERROR:cancelled</c>.</item>
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
    [IntentFilter(new[] { StateExportReceiver.ActionExportState, StateExportReceiver.ActionListCategories,
                          StateExportReceiver.ActionCancelExport })]
    public class StateExportReceiver : BroadcastReceiver
    {
        // Built the same way as Intents.LockDatabase: the fork's applicationId is shiroikuma.<PackagePart>,
        // so these are exactly the contract's <pkg>.action.* strings.
        public const string ActionExportState = "shiroikuma." + AppNames.PackagePart + ".action.EXPORT_STATE";
        public const string ActionListCategories = "shiroikuma." + AppNames.PackagePart + ".action.LIST_CATEGORIES";
        public const string ActionCancelExport = "shiroikuma." + AppNames.PackagePart + ".action.CANCEL_EXPORT";

        private const long ProgressThrottleMs = 500;

        /// <summary>Guards the single terminal reply, so an async success and a sync error can never race.</summary>
        private int _replied;

        /// <summary>
        /// The export currently in flight, or null. <b>Static</b> on purpose: every broadcast lands on a
        /// FRESH receiver instance, so a CANCEL_EXPORT is never delivered to the object running the export —
        /// it has to find it here. At most one, since the contract forbids two exports at once.
        /// </summary>
        private static volatile ExportRun _running;

        /// <summary>
        /// One in-flight export as the cancel path needs to see it: which request it answers, and whether a
        /// stop has been asked for. The flag is only ever read between entries by the write loop.
        /// </summary>
        private sealed class ExportRun
        {
            public readonly string ReplyId;
            private volatile bool _cancelled;

            public ExportRun(string replyId) { ReplyId = replyId; }

            public bool Cancelled => _cancelled;

            public void Cancel() { _cancelled = true; }
        }

        public override void OnReceive(Context context, Intent intent)
        {
            string action = intent?.Action;
            if (action != ActionExportState && action != ActionListCategories && action != ActionCancelExport)
                return;

            Context app = context.ApplicationContext;

            // CANCEL_EXPORT answers nothing, so it needs neither a reply channel nor a worker thread: it
            // flips a flag and returns, which is also the promptest the running export can hear about it.
            if (action == ActionCancelExport)
            {
                CancelExport(app, intent);
                return;
            }

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

        /// <summary>
        /// <c>id&lt;TAB&gt;label&lt;TAB&gt;parent&lt;TAB&gt;on|off</c> per line. Both trailing fields are
        /// positional, so a top-level item still carries an <b>empty</b> third field to reach the fourth.
        /// The fourth is this app's own answer to "does this start ticked" — never the caller's guess.
        /// </summary>
        private static string CategoryLines(Context app)
        {
            var lines = new List<string>();
            foreach (var category in BackupCategory.All)
            {
                lines.Add(category.Id + "\t" + app.GetString(category.LabelRes) + "\t" +
                          (category.ParentId ?? "") + "\t" + (category.DefaultSelected ? "on" : "off"));
            }
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Stop the running export. Gated on the token alone, deliberately: the master switch may have been
        /// turned off after the export started, and refusing to stop it then is exactly the runaway this
        /// action exists to prevent — while a cancel with nothing running is harmless by construction.
        /// </summary>
        private static void CancelExport(Context app, Intent intent)
        {
            if (!AutomationAuth.IsTokenValid(app, intent.GetStringExtra("token")))
            {
                Kp2aLog.Log("Backup automation: CANCEL_EXPORT with a bad token, ignored");
                return;
            }

            // Safe to send at any time: nothing running, or a run that already finished, is a SILENT
            // no-op — not an error, not a reply, not a crash.
            var run = _running;
            if (run == null)
                return;

            string replyId = intent.GetStringExtra("reply_id");
            if (!string.IsNullOrEmpty(replyId) && replyId != run.ReplyId)
            {
                Kp2aLog.Log("Backup automation: CANCEL_EXPORT names another request, ignored");
                return;
            }

            run.Cancel();
            Kp2aLog.Log("Backup automation: cancel requested");
        }

        private void RunExport(Context app, Intent intent,
            string replyAction, string replyPackage, string replyId)
        {
            // items: absent/empty means our default set; every id must be one we actually export.
            string items = intent.GetStringExtra("items");
            List<string> categories;
            if (string.IsNullOrWhiteSpace(items))
            {
                // Absent still means "our default set" — which is now exactly the categories we answer
                // LIST_CATEGORIES with as `on`.
                categories = BackupCategory.DefaultIds.ToList();
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

            // Published where CANCEL_EXPORT can find it, and taken down again in the finally below.
            var run = new ExportRun(replyId);
            _running = run;

            BackupResult result;
            try
            {
                result = Kp2aBackup.ExportToDirectory(app, categories, dir, (done, total, label) =>
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
                }, () => run.Cancelled);
            }
            finally
            {
                // Only ever clear our own registration, so a later run's slot is never stolen.
                if (ReferenceEquals(_running, run))
                    _running = null;
            }

            if (result.Cancelled)
            {
                // The terminal reply for the ORIGINAL request, through the normal channel and under the
                // same one-reply guard. Sent even though nobody may still be listening: it is what proves
                // the run ended rather than carrying on unseen. The partial file is already gone.
                Reply(app, replyAction, replyPackage, replyId, "ERROR:cancelled");
                return;
            }

            Reply(app, replyAction, replyPackage, replyId,
                "OK:" + result.Path + "|" + result.Size + "|" + Kp2aBackup.HumanSize(result.Size) + "|" +
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
