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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using AndroidX.Core.App;

namespace keepass2android.Backup
{
    /// <summary>
    /// Where a data-door export or import actually runs (sister-app contract v2, §2a).
    ///
    /// <para><b>Why a foreground service and not the provider call.</b> The call returns in milliseconds;
    /// this may not. A binder call holds the caller — 応用管理 is drawing a list, and a long synchronous
    /// call would freeze its UI, report no progress and refuse cancellation — and a backgrounded app
    /// writing for minutes is frozen mid-stream on 白い熊's phone, which yields a truncated archive
    /// underneath a success reply: the worst possible failure, indistinguishable from a good backup until
    /// the day it is restored.</para>
    ///
    /// <para><b>The descriptor</b> was already duplicated by <see cref="AutomationProvider"/> before it got
    /// here, because the original belongs to the binder transaction and is closed the moment
    /// <c>call()</c> returns. This runner owns the copy and closes it in a <c>finally</c>: leaking one
    /// would hold the caller's file open, and a caller cannot checksum or encrypt a file that is still
    /// open.</para>
    /// </summary>
    [Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
    public class AutomationDataService : Service
    {
        private const string ChannelId = "AutomationDataChannel";
        private const int NotificationId = 9714;
        private const string ExtraJob = "job";
        /// <summary>Only the notification title depends on this; <see cref="AutomationJob.Importing"/>
        /// is what the work itself reads. It travels in the Intent because the notification must go up
        /// before the job is looked up at all — see <see cref="OnStartCommand"/>.</summary>
        private const string ExtraImporting = "importing";
        private const long ProgressThrottleMs = 500;

        /// <summary>
        /// How long a handed-over job may sit unclaimed before its descriptor is closed. Generous, because
        /// a cold process start on a busy phone is not fast; finite, because the alternative is holding the
        /// caller's file open for the life of the process.
        /// </summary>
        private const int PickupTimeoutMs = 60000;

        /// <summary>
        /// The jobs handed over in memory. See <see cref="AutomationJob"/> for why a
        /// <see cref="ParcelFileDescriptor"/> must not travel in an Intent extra.
        /// </summary>
        private static readonly ConcurrentDictionary<string, AutomationJob> Pending =
            new ConcurrentDictionary<string, AutomationJob>(StringComparer.Ordinal);

        /// <summary>
        /// Start the job. Normally that means a foreground service; if the platform refuses to let us start
        /// one from the background — Android 12+ does, in states it does not exempt, and this app is
        /// deliberately called while stopped — the job is run on a plain thread instead rather than
        /// dropped. A 鍵暗号 archive is settings and font files, kilobytes that finish in well under a
        /// second, so the service is insurance rather than a requirement; refusing to work at all because
        /// the insurance was unavailable would fail exactly the clean-phone restore this contract exists
        /// for.
        /// </summary>
        public static void Start(Context ctx, AutomationJob job)
        {
            Pending[job.JobId] = job;
            var intent = new Intent(ctx, typeof(AutomationDataService));
            intent.PutExtra(ExtraJob, job.JobId);
            intent.PutExtra(ExtraImporting, job.Importing);
            try
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                    ctx.StartForegroundService(intent);
                else
                    ctx.StartService(intent);
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Automation door: no foreground service (" + e.Message + "), running in-process");
                if (!Pending.TryRemove(job.JobId, out var orphan))
                    return;
                try
                {
                    RunInBackground(ctx.ApplicationContext, orphan);
                }
                catch (Exception inner)
                {
                    // Nothing is going to run this job now, so the caller's descriptor must not be left
                    // open: it cannot checksum or encrypt a file we are still holding, and a caller that
                    // is never answered waits out its whole timeout.
                    Kp2aLog.Log("Automation door: cannot run the job at all: " + inner);
                    Discard(ctx.ApplicationContext, orphan, "ERROR:cannot start");
                }
                return;
            }

            // The start was accepted — but an Intent that is never delivered would strand the descriptor
            // in the handover map for the life of the process. Give the service a generous window to
            // collect it, then close it rather than hold the caller's file open forever.
            WatchForPickup(ctx.ApplicationContext, job.JobId);
        }

        /// <summary>
        /// Close a job nothing will run: the descriptor first, then one terminal answer so the caller
        /// stops waiting. Silent about the reply channel being absent — that case is already logged.
        /// </summary>
        private static void Discard(Context ctx, AutomationJob job, string reason)
        {
            try { job.Fd?.Close(); }
            catch (Exception) { /* nothing further to do about it */ }
            job.Fd = null;
            AutomationJobs.Finish(job.JobId);
            SendReply(ctx, job, reason);
        }

        private static void WatchForPickup(Context ctx, string jobId)
        {
            var watchdog = new Thread(() =>
            {
                Thread.Sleep(PickupTimeoutMs);
                if (Pending.TryRemove(jobId, out var stranded))
                {
                    Kp2aLog.Log("Automation door: job " + jobId + " was never collected by the service");
                    Discard(ctx, stranded, "ERROR:service never started");
                }
            });
            watchdog.IsBackground = true;
            watchdog.Start();
        }

        public override IBinder OnBind(Intent intent) => null;

        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            // The notification goes up FIRST, before this method decides anything at all. Once
            // startForegroundService() has been called the platform REQUIRES a startForeground() from us
            // whatever we then do, and kills the process with ForegroundServiceDidNotStartInTimeException
            // otherwise — so an early return without one would mean a caller retrying with a STALE job id
            // kills the very app it was trying to back up, instead of being harmlessly ignored.
            try
            {
                CreateNotificationChannel();
                StartForeground(NotificationId,
                    BuildNotification(intent != null && intent.GetBooleanExtra(ExtraImporting, false)));
            }
            catch (Exception e)
            {
                // Refused rather than skipped: the work is small enough to finish without it.
                Kp2aLog.Log("Automation door: startForeground refused: " + e.Message);
            }

            string jobId = intent?.GetStringExtra(ExtraJob);
            if (jobId == null || !Pending.TryRemove(jobId, out var job))
            {
                // A stale or duplicate delivery, or one the pickup watchdog already reaped. A silent
                // no-op now that the notification obligation above has been met.
                return StopNow(startId);
            }

            // From here the job is OUT of the handover map, so this method owns the descriptor: every
            // path out of it either hands the job to the runner or closes it.
            try
            {
                new Thread(() =>
                {
                    try { Run(this, job); }
                    finally { StopNow(startId); }
                }).Start();
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Automation door: could not run the job: " + e);
                Discard(ApplicationContext, job, "ERROR:cannot start");
                return StopNow(startId);
            }

            return StartCommandResult.NotSticky;
        }

        private StartCommandResult StopNow(int startId)
        {
            try { StopForeground(StopForegroundFlags.Remove); }
            catch (Exception) { /* never got one up */ }
            StopSelf(startId);
            return StartCommandResult.NotSticky;
        }

        private static void RunInBackground(Context ctx, AutomationJob job)
        {
            new Thread(() => Run(ctx, job)).Start();
        }

        // ---- the job -------------------------------------------------------------------------------

        private static void Run(Context ctx, AutomationJob job)
        {
            int replied = 0;
            Action<string> reply = result =>
            {
                // Exactly one terminal answer per job, whatever path got here — a synchronous failure and
                // an asynchronous success must never both fire. The same guard the broadcast contract has
                // carried since the first sister app.
                if (Interlocked.Exchange(ref replied, 1) != 0)
                    return;
                AutomationJobs.Finish(job.JobId);
                SendReply(ctx, job, result);
            };

            try
            {
                if (job.Importing)
                    RunImport(ctx, job, reply);
                else
                    RunExport(ctx, job, reply);
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Automation door: job failed: " + e);
                reply("ERROR:" + (e.Message ?? e.GetType().Name).Replace('\n', ' ').Trim());
            }
            finally
            {
                // A leaked descriptor holds the caller's file open, and the caller cannot checksum or
                // encrypt a file that is still open.
                try { job.Fd?.Close(); }
                catch (Exception) { /* already closed by the stream wrapper */ }
                job.Fd = null;
            }
        }

        private static void RunExport(Context ctx, AutomationJob job, Action<string> reply)
        {
            var selected = Resolve(job.Items);
            if (selected == null)
            {
                reply("ERROR:unknown category in items: " + job.Items);
                return;
            }

            long lastProgress = 0;
            long written;
            BackupResult result;
            // The descriptor is closed by leaving this block, and the reply is sent only AFTER that: the
            // caller cannot checksum or encrypt a file we are still holding open, and the reply is its
            // signal that it may.
            using (var counting = new CountingStream(
                       new OutputStreamInvoker(new ParcelFileDescriptor.AutoCloseOutputStream(job.Fd))))
            {
                result = Kp2aBackup.Export(ctx, selected.Select(c => c.Id).ToList(), counting,
                    (done, total, label) =>
                    {
                        if (string.IsNullOrEmpty(job.ProgressAction))
                            return;
                        long now = Java.Lang.JavaSystem.CurrentTimeMillis();
                        if (done < total && now - lastProgress < ProgressThrottleMs)
                            return;
                        lastProgress = now;
                        // `item` is the category id the panel highlights — Kp2aBackup walks the categories
                        // in BackupCategory.All order, which is the order `selected` is built in.
                        string item = done >= 1 && done <= selected.Count ? selected[done - 1].Id : null;
                        SendProgress(ctx, job, item,
                            string.Format(ctx.GetString(Resource.String.backup_progress), done, total, label),
                            done, total, ctx.GetString(Resource.String.backup_progress_unit));
                    },
                    () => AutomationJobs.IsCancelled(job.JobId));

                counting.Flush();
                written = counting.Written;
            }

            if (result.Cancelled || AutomationJobs.IsCancelled(job.JobId))
            {
                reply("ERROR:cancelled");
                return;
            }
            reply("OK:" + written + "|" + result.CategoryCount + " categories");
        }

        /// <summary>
        /// Read the whole archive before touching anything: a partial read that failed halfway would
        /// otherwise import half an archive, and a half-restored app is worse than one that refused.
        /// (It is also what <see cref="System.IO.Compression.ZipArchive"/> needs — a descriptor is not
        /// seekable.)
        /// <para>
        /// Every category the archive actually carries is applied: <see cref="Kp2aBackup.Import"/> skips
        /// the ones it does not find, and each is merged key by key through the same allow-list the export
        /// uses, so a doctored archive cannot inject a credential key back into a password manager.
        /// </para>
        /// </summary>
        private static void RunImport(Context ctx, AutomationJob job, Action<string> reply)
        {
            // Spooled to a file rather than a byte[]: the archive is caller-supplied and its size is not
            // ours to assume, and holding one in memory to import it is how an app dies on the restore it
            // was installed to perform.
            string spool = System.IO.Path.Combine(ctx.CacheDir.AbsolutePath,
                                                  "automation-import-" + job.JobId + ".zip");
            try
            {
                long size;
                using (var input = new InputStreamInvoker(new ParcelFileDescriptor.AutoCloseInputStream(job.Fd)))
                using (var target = File.Create(spool))
                {
                    input.CopyTo(target);
                    size = target.Length;
                }
                if (size == 0)
                {
                    reply("ERROR:empty archive");
                    return;
                }

                BackupResult result;
                using (var archive = File.OpenRead(spool))
                    result = Kp2aBackup.Import(ctx, BackupCategory.AllIds, archive);

                if (result.CategoryCount == 0)
                {
                    reply("ERROR:archive carries no categories");
                    return;
                }
                // The caller force-stops us straight after this, deliberately: a running process writes
                // its cached SharedPreferences back out at orderly shutdown and would silently undo the
                // import that just happened. That guarantee lives on 応用管理's side so forty-two apps
                // need not each remember it.
                reply("OK:" + result.CategoryCount + " restored");
            }
            finally
            {
                // Never leave a caller's archive sitting in our cache — it is their data, not ours.
                try { if (File.Exists(spool)) File.Delete(spool); }
                catch (Exception e) { Kp2aLog.Log("Automation door: could not remove " + spool + ": " + e); }
            }
        }

        /// <summary>
        /// The categories to export. Absent or empty <c>items</c> means <b>our default set</b> — what we
        /// report as <c>on</c> from LIST_CATEGORIES — not everything. Null when an id is not ours.
        /// </summary>
        private static List<BackupCategory> Resolve(string items)
        {
            if (string.IsNullOrWhiteSpace(items))
                return BackupCategory.All.Where(c => c.DefaultSelected).ToList();

            var wanted = items.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            var found = wanted.Select(BackupCategory.ById).Where(c => c != null).ToList();
            if (found.Count != wanted.Count)
                return null;
            // Kept in BackupCategory.All order, which is the order Kp2aBackup writes them in.
            return BackupCategory.All.Where(c => found.Contains(c)).ToList();
        }

        // ---- the reply / progress channel ----------------------------------------------------------

        private static void SendReply(Context ctx, AutomationJob job, string result)
        {
            if (string.IsNullOrEmpty(job.ReplyAction) || string.IsNullOrEmpty(job.ReplyPackage))
            {
                Kp2aLog.Log("Automation door: finished with no reply channel: " + result);
                return;
            }
            try
            {
                var intent = new Intent(job.ReplyAction);
                intent.SetPackage(job.ReplyPackage);
                // Without this a caller that has been backgrounded never hears the answer — and on a clean
                // phone the caller may not have been launched at all.
                intent.AddFlags(ActivityFlags.IncludeStoppedPackages);
                intent.PutExtra(AutomationProvider.KeyJobId, job.JobId);
                intent.PutExtra("reply_id", job.JobId);
                intent.PutExtra(AutomationProvider.KeyResult, result);
                ctx.SendBroadcast(intent);
                Kp2aLog.Log("Automation door: replied " + result);
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Automation door: reply failed: " + e);
            }
        }

        private static void SendProgress(Context ctx, AutomationJob job, string item, string text,
            long current, long total, string unit)
        {
            try
            {
                var intent = new Intent(job.ProgressAction);
                intent.SetPackage(job.ReplyPackage);
                intent.AddFlags(ActivityFlags.IncludeStoppedPackages);
                intent.PutExtra(AutomationProvider.KeyJobId, job.JobId);
                intent.PutExtra("reply_id", job.JobId);
                intent.PutExtra("app", ctx.GetString(AppNames.AppNameResource));
                if (!string.IsNullOrEmpty(item))
                    intent.PutExtra("item", item);
                intent.PutExtra("text", text);
                intent.PutExtra("current", current);
                intent.PutExtra("total", total);
                intent.PutExtra("unit", unit);
                ctx.SendBroadcast(intent);
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Automation door: progress failed: " + e);
            }
        }

        // ---- notification --------------------------------------------------------------------------

        private Notification BuildNotification(bool importing)
        {
            return new NotificationCompat.Builder(this, ChannelId)
                .SetSmallIcon(Resource.Drawable.ic_launcher_gray)
                .SetPriority(NotificationCompat.PriorityLow)
                .SetSilent(true)
                .SetOngoing(true)
                .SetContentTitle(GetString(importing
                    ? Resource.String.automation_data_importing
                    : Resource.String.automation_data_exporting))
                .Build();
        }

        private void CreateNotificationChannel()
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.O)
                return;
            var channel = new NotificationChannel(ChannelId,
                GetString(Resource.String.automation_data_channel_name), NotificationImportance.Low);
            channel.EnableLights(false);
            channel.EnableVibration(false);
            channel.SetSound(null, null);
            channel.SetShowBadge(false);
            ((NotificationManager)GetSystemService(NotificationService))?.CreateNotificationChannel(channel);
        }

        /// <summary>
        /// Counts what it forwards. The caller owns the file and we may not be able to see it at all — it
        /// can be an anonymous pipe, or a descriptor into a directory this app cannot list — so the byte
        /// count is taken as it goes rather than stat'ed afterwards.
        /// </summary>
        private sealed class CountingStream : Stream
        {
            private readonly Stream _inner;

            public CountingStream(Stream inner) { _inner = inner; }

            public long Written { get; private set; }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => Written;

            public override long Position
            {
                get => Written;
                set => throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                _inner.Write(buffer, offset, count);
                Written += count;
            }

            public override void WriteByte(byte value)
            {
                _inner.WriteByte(value);
                Written++;
            }

            public override void Flush() => _inner.Flush();

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    _inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
