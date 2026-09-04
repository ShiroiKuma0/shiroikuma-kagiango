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
using Android.Content;
using Android.Database;
using Android.OS;
using Org.Json;

namespace keepass2android.Backup
{
    /// <summary>
    /// The <b>data door</b> (sister-app contract v2, §2a): export this app's own state, and put it back,
    /// for a caller we can actually identify — so 応用管理 can back 鍵暗号 up <i>with its data</i> and
    /// restore it onto a wiped phone.
    ///
    /// <para><b>Why a provider and not another broadcast action.</b> A broadcast cannot tell you who sent
    /// it. v1's answer to that was the shared token, which cannot survive the very wipe this exists to
    /// recover from — and since the caller supplies the destination bytes are written into, "no idea who is
    /// asking" would mean any app on the phone could harvest every sister app's data. A provider gets the
    /// caller's identity from the framework (<see cref="AutomationCallers"/>). And a list needs a
    /// synchronous answer: 応用管理 draws a row per installed app before any export exists.</para>
    ///
    /// <para><b><c>import</c> exists ONLY here.</b> It never gets a broadcast action. An import overwrites
    /// this app's preferences, and <see cref="StateExportReceiver"/> is exported with no permission — an
    /// import there would let any app on the phone rewrite a password manager's security settings.</para>
    ///
    /// <para><b>What does NOT happen here: the payload.</b> <see cref="Call"/> validates, starts a
    /// foreground service and returns. Megabytes over minutes inside a binder call would block the caller,
    /// report no progress, refuse cancellation and die silently if this process were killed.</para>
    ///
    /// <para><b>Why a descriptor and not a path.</b> A backup is not a stable directory while it is being
    /// written: 応用管理 writes into a temporary path and renames on commit, and it encrypts and checksums
    /// <i>per file it knows about</i> — a file we dropped in ourselves would be renamed out from under us,
    /// would sit in plaintext inside an otherwise encrypted backup, and would be unverified rather than
    /// verified-and-failing. A descriptor is also a capability that expires when it is closed. It also
    /// means this path needs no <c>MANAGE_EXTERNAL_STORAGE</c>: that permission was only ever required
    /// because v1 handed apps an absolute path.</para>
    /// </summary>
    [ContentProvider(new[] { AutomationProvider.Authority }, Exported = true)]
    public class AutomationProvider : ContentProvider
    {
        /// <summary>
        /// <c>&lt;applicationId&gt;.automation</c>. Built from <see cref="AppNames.PackagePart"/> exactly as
        /// the fork's other derived names are, so it is <c>shiroikuma.kagiango.automation</c> here and
        /// cannot collide with upstream's.
        /// </summary>
        public const string Authority = "shiroikuma." + AppNames.PackagePart + ".automation";

        public const string MethodDescribe = "describe";
        public const string MethodExport = "export";
        public const string MethodImport = "import";
        public const string MethodCancel = "cancel";

        public const string KeyResult = "result";
        public const string KeyFd = "fd";
        public const string KeyToken = "token";
        public const string KeyJobId = "job_id";
        public const string KeyItems = "items";
        public const string KeyReplyAction = "reply_action";
        public const string KeyReplyPackage = "reply_package";
        public const string KeyProgressAction = "progress_action";

        /// <summary>
        /// The oldest archive this build can still read. Version skew has a direction: old data into a
        /// newer app is normally fine, because an app migrates its own storage; newer data into an older
        /// app is not. This is what lets a caller refuse the second case at discovery time, before
        /// anything is streamed.
        /// </summary>
        public const int MinFormatReadable = 1;

        public override bool OnCreate() => true;

        /// <summary>
        /// Every method answers a <see cref="Bundle"/> with <see cref="KeyResult"/> — <c>OK…</c> or
        /// <c>ERROR:…</c>, the same vocabulary the broadcast contract uses, so a caller has one grammar to
        /// parse rather than two.
        /// <para>
        /// <b>A refusal is returned, never thrown.</b> An exception across a binder reaches the caller as a
        /// <c>RuntimeException</c> carrying our stack trace, which tells 白い熊 nothing and tells a
        /// misbehaving caller rather more than it should.
        /// </para>
        /// </summary>
        public override Bundle Call(string method, string arg, Bundle extras)
        {
            Context ctx = Context?.ApplicationContext;
            if (ctx == null)
                return Answer("ERROR:not ready");

            try
            {
                // WHO, before WHAT: a caller we cannot identify gets the same answer whatever it asked for.
                string refusal = AutomationCallers.Refuse(ctx, CallingPackage);
                if (refusal != null)
                {
                    Kp2aLog.Log("Automation door: refused " + refusal);
                    return Answer(refusal);
                }

                // Then this app's own switches — a token is ignored unless this app asks for one.
                refusal = AutomationAuth.Refuse(ctx, GetString(extras, KeyToken));
                if (refusal != null)
                    return Answer(refusal);

                switch (method)
                {
                    case MethodDescribe:
                        return Answer("OK:" + Describe(ctx));
                    case MethodExport:
                        return Answer(Start(ctx, extras, importing: false));
                    case MethodImport:
                        return Answer(Start(ctx, extras, importing: true));
                    case MethodCancel:
                        AutomationJobs.Cancel(GetString(extras, KeyJobId));
                        return Answer("OK:cancelled");
                    default:
                        return Answer("ERROR:unknown method: " + method);
                }
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Automation door: " + e);
                return Answer("ERROR:" + (e.Message ?? e.GetType().Name).Replace('\n', ' ').Trim());
            }
        }

        /// <summary>
        /// What this app would export, answered without exporting anything — the header of §2a.
        /// <para>
        /// Returned from the call rather than written into the archive, deliberately: 応用管理 must draw a
        /// row before an export exists, and at restore must judge compatibility <b>before</b> streaming
        /// anything into an app that would reject it, which it cannot do if the header is buried inside an
        /// encrypted archive.
        /// </para>
        /// <para>
        /// <c>contains</c> ends with an explicit note that this is a settings backup. 鍵暗号 is a password
        /// manager and its export deliberately carries no database, no master password and no stored
        /// credential — a row in 応用管理 reading only "✓ backed up" would otherwise invite exactly the
        /// wrong conclusion.
        /// </para>
        /// </summary>
        private static string Describe(Context ctx)
        {
            var header = new JSONObject();
            header.Put("app_id", ctx.PackageName);
            header.Put("version_code", VersionCode(ctx));
            header.Put("version_name", Kp2aBackup.AppVersionName(ctx));
            header.Put("format", Kp2aBackup.FormatVersion);
            header.Put("min_format_readable", MinFormatReadable);
            // Our import only merges preferences and font files, so it is safe before the first launch.
            header.Put("requires_launch_first", false);

            var contains = new JSONArray();
            foreach (var category in BackupCategory.All.Where(c => c.DefaultSelected))
                contains.Put(ctx.GetString(category.LabelRes));
            contains.Put(ctx.GetString(Resource.String.backup_contains_note));
            header.Put("contains", contains);

            return header.ToString();
        }

        private static long VersionCode(Context ctx)
        {
            try
            {
                var info = ctx.PackageManager.GetPackageInfo(ctx.PackageName, 0);
                if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
                    return info.LongVersionCode;
#pragma warning disable CS0618
                return info.VersionCode;
#pragma warning restore CS0618
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// Hand the descriptor to the runner and get out of the way.
        /// <para>
        /// The descriptor is <b>duplicated</b> before it leaves this method: the one in <paramref
        /// name="extras"/> belongs to the binder transaction and is closed the moment <see cref="Call"/>
        /// returns, so a service reading it afterwards would find it shut. That is a bug you only see under
        /// load, which is why it is not left to the runner to remember.
        /// </para>
        /// </summary>
        private static string Start(Context ctx, Bundle extras, bool importing)
        {
            // Our settings live in credential-encrypted storage: before the first unlock there is genuinely
            // nothing to read and nowhere to write, and a half-empty archive would be worse than an honest
            // error.
            if (!StateExportReceiver.IsUserUnlocked(ctx))
                return "ERROR:device-locked";

            string items = GetString(extras, KeyItems);
            if (!importing && !string.IsNullOrWhiteSpace(items))
            {
                var unknown = items.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)
                                   .Where(id => !BackupCategory.IsKnownId(id)).ToList();
                if (unknown.Count > 0)
                    return "ERROR:unknown category in items: " + string.Join(",", unknown);
            }

            ParcelFileDescriptor fd;
#pragma warning disable CS0618
            fd = extras?.GetParcelable(KeyFd) as ParcelFileDescriptor;
#pragma warning restore CS0618
            if (fd == null)
                return "ERROR:no descriptor";

            ParcelFileDescriptor dup;
            try
            {
                dup = fd.Dup();
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Automation door: descriptor unusable: " + e);
                return "ERROR:descriptor unusable";
            }

            var job = new AutomationJob
            {
                JobId = AutomationJobs.Begin(),
                Fd = dup,
                Importing = importing,
                Items = items,
                ReplyAction = GetString(extras, KeyReplyAction),
                ReplyPackage = GetString(extras, KeyReplyPackage),
                ProgressAction = GetString(extras, KeyProgressAction),
            };
            AutomationDataService.Start(ctx, job);
            return "OK:" + job.JobId;
        }

        private static string GetString(Bundle extras, string key) =>
            extras == null ? null : extras.GetString(key);

        private static Bundle Answer(string result)
        {
            var bundle = new Bundle();
            bundle.PutString(KeyResult, result);
            return bundle;
        }

        // A provider that is only ever Call()ed still has to answer these. Refusing loudly beats returning
        // an empty cursor, which reads downstream as "there is no data" rather than "wrong door".
        public override ICursor Query(Android.Net.Uri uri, string[] projection, string selection,
            string[] selectionArgs, string sortOrder) =>
            throw new Java.Lang.UnsupportedOperationException("automation is call() only");

        public override string GetType(Android.Net.Uri uri) => null;

        public override Android.Net.Uri Insert(Android.Net.Uri uri, ContentValues values) =>
            throw new Java.Lang.UnsupportedOperationException("automation is call() only");

        public override int Delete(Android.Net.Uri uri, string selection, string[] selectionArgs) =>
            throw new Java.Lang.UnsupportedOperationException("automation is call() only");

        public override int Update(Android.Net.Uri uri, ContentValues values, string selection,
            string[] selectionArgs) =>
            throw new Java.Lang.UnsupportedOperationException("automation is call() only");
    }
}
