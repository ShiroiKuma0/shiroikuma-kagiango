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
    /// The external-automation gate for <see cref="StateExportReceiver"/>: a master switch plus a shared
    /// secret every automation broadcast must carry — the sister-app model (kxkb's <c>AutomationAuth</c>,
    /// renrakusaki's <c>Config</c>, 自由作業盤's <c>AutomationAuth</c>).
    /// <para>
    /// Device-local by design: this is its own SharedPreferences file, and the backup engine
    /// (<see cref="Kp2aBackup"/>) exports an allow-list of the app's DEFAULT prefs only — so the token
    /// never travels inside an export ZIP and never leaves the phone.
    /// </para>
    /// <para>
    /// Nothing is reachable until 白い熊 turns <see cref="Enabled"/> on (default <c>false</c>). The switch
    /// and the token are checked separately so "disabled" and "bad token" stay distinct, debuggable errors.
    /// </para>
    /// </summary>
    public static class AutomationAuth
    {
        private const string PrefsFile = "kagiango_automation";
        private const string KeyEnabled = "automation_enabled";
        private const string KeyToken = "automation_token";

        private static ISharedPreferences Prefs(Context ctx) =>
            ctx.ApplicationContext.GetSharedPreferences(PrefsFile, FileCreationMode.Private);

        public static bool IsEnabled(Context ctx) => Prefs(ctx).GetBoolean(KeyEnabled, false);

        public static void SetEnabled(Context ctx, bool value) =>
            Prefs(ctx).Edit().PutBoolean(KeyEnabled, value).Apply();

        /// <summary>
        /// The shared secret — 24 random bytes, hex-encoded. Generated lazily on first read so the
        /// settings row always shows a value, even before the switch is turned on.
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
        /// (<see cref="MessageDigest.IsEqual"/>) so a wrong token leaks nothing through timing.
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
