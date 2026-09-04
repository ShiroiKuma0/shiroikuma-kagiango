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
using System.Text;
using Android.Content;
using Java.Security;

namespace keepass2android.Backup
{
    /// <summary>
    /// The external-automation gate for <see cref="StateExportReceiver"/> and
    /// <see cref="AutomationProvider"/>: a master switch that ships <b>on</b>, and a shared secret that is
    /// only asked for when 白い熊 asks for it (sister-app contract v2, 2026-09-04).
    /// <para>
    /// <b>Why the defaults inverted.</b> v1 shipped every app closed: the switch was off and a caller also
    /// had to present a 48-character secret pasted from this app's settings into the caller's. A pasted
    /// secret cannot survive a wipe, and the case this family now exists to serve is 応用管理 restoring
    /// apps <i>and their data</i> onto a clean phone, where nothing has been configured and nobody has
    /// pasted anything. A gate that only works once the phone is already set up is no gate for setting the
    /// phone up. The identity check that replaces it lives in <see cref="AutomationCallers"/>, on the one
    /// surface that can actually see who is calling.
    /// </para>
    /// <para>
    /// Device-local by design: this is its own SharedPreferences file, and the backup engine
    /// (<see cref="Kp2aBackup"/>) exports an allow-list of the app's DEFAULT prefs only — so the token
    /// never travels inside an export ZIP and never leaves the phone.
    /// </para>
    /// </summary>
    public static class AutomationAuth
    {
        private const string PrefsFile = "kagiango_automation";
        private const string KeyEnabled = "automation_enabled";
        private const string KeyRequireToken = "automation_require_token";
        private const string KeyToken = "automation_token";

        private static ISharedPreferences Prefs(Context ctx) =>
            ctx.ApplicationContext.GetSharedPreferences(PrefsFile, FileCreationMode.Private);

        /// <summary>
        /// The master switch — <b>default on</b>. It stays a switch rather than being removed because it is
        /// the only way to close this one app off, and a feature that can be turned on but never off is one
        /// 白い熊 cannot retreat from.
        /// </summary>
        public static bool IsEnabled(Context ctx) => Prefs(ctx).GetBoolean(KeyEnabled, true);

        public static void SetEnabled(Context ctx, bool value) =>
            Prefs(ctx).Edit().PutBoolean(KeyEnabled, value).Apply();

        /// <summary>
        /// Whether a caller must also present <see cref="Token"/> — <b>default off</b>. Off means any sister
        /// app may drive the automation; the data door checks the caller's package, uid and signing
        /// certificate either way.
        /// </summary>
        public static bool RequiresToken(Context ctx) => Prefs(ctx).GetBoolean(KeyRequireToken, false);

        public static void SetRequiresToken(Context ctx, bool value) =>
            Prefs(ctx).Edit().PutBoolean(KeyRequireToken, value).Apply();

        /// <summary>
        /// The one gate, in one place: <c>null</c> means proceed, anything else is the exact
        /// <c>ERROR:</c> line to answer with.
        /// <para>
        /// Written once rather than at each entry point because two checks spelled out per caller is how
        /// "disabled" and "bad token" drift apart across forty-two apps — and they must stay distinct,
        /// since they debug differently.
        /// </para>
        /// <para>
        /// <b>A token handed to an app that does not require one is IGNORED, never refused.</b> Tokens live
        /// in task arguments and workspace variables that outlive the setting they were pasted for, and a
        /// caller still sending one — because it was configured last year, or because another app on the
        /// batch does want one — must be served. Refusing it would turn "白い熊 turned a switch off" into
        /// "half the batch mysteriously fails", which is exactly the friction the switch exists to remove.
        /// </para>
        /// </summary>
        public static string Refuse(Context ctx, string candidate)
        {
            if (!IsEnabled(ctx))
                return "ERROR:automation disabled";
            if (RequiresToken(ctx) && !IsTokenValid(ctx, candidate))
                return "ERROR:bad token";
            return null;
        }

        /// <summary>
        /// The shared secret — 24 random bytes, hex-encoded. Generated lazily on first read so the
        /// settings row always shows a value, even before the token is being asked for.
        /// </summary>
        public static string Token(Context ctx)
        {
            string stored = Prefs(ctx).GetString(KeyToken, null);
            return string.IsNullOrEmpty(stored) ? RegenerateToken(ctx) : stored;
        }

        public static string RegenerateToken(Context ctx)
        {
            var bytes = new byte[24];
            new SecureRandom().NextBytes(bytes);
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
                sb.Append(b.ToString("x2"));
            string token = sb.ToString();
            Prefs(ctx).Edit().PutString(KeyToken, token).Apply();
            return token;
        }

        /// <summary>Abbreviated form for the settings row — <c>80922d8c…4c49a87c</c>.</summary>
        public static string Abbreviate(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length <= 20)
                return token ?? "";
            return token.Substring(0, 8) + "…" + token.Substring(token.Length - 8);
        }

        /// <summary>
        /// True when the caller's token matches the stored secret, compared in constant time
        /// (<see cref="MessageDigest.IsEqual"/>) so a wrong token leaks nothing through timing. Kept for
        /// the case where the token IS required — the compare costs nothing and the habit is worth having.
        /// </summary>
        public static bool IsTokenValid(Context ctx, string candidate)
        {
            if (string.IsNullOrEmpty(candidate))
                return false;
            return MessageDigest.IsEqual(Encoding.UTF8.GetBytes(candidate),
                                         Encoding.UTF8.GetBytes(Token(ctx)));
        }
    }
}
