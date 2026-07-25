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

using Android.Content;

namespace keepass2android.Backup
{
    /// <summary>
    /// Device-local settings of the Export / Import page — currently just the backup folder.
    /// <para>
    /// Deliberately kept in its OWN SharedPreferences file rather than the app's default prefs:
    /// <see cref="Kp2aBackup"/> exports the default prefs, so a path that describes THIS phone
    /// would otherwise travel inside every backup ZIP and be restored onto another device.
    /// The automation token (<see cref="AutomationAuth"/>) is held out of the export the same way.
    /// </para>
    /// </summary>
    public static class BackupConfig
    {
        private const string PrefsFile = "kagiango_backup";
        private const string KeyExportDir = "export_dir";

        private static ISharedPreferences Prefs(Context ctx) =>
            ctx.ApplicationContext.GetSharedPreferences(PrefsFile, FileCreationMode.Private);

        /// <summary>The configured backup folder, or null when 白い熊 has not set one yet.</summary>
        public static string GetExportDir(Context ctx)
        {
            string dir = Prefs(ctx).GetString(KeyExportDir, null);
            return string.IsNullOrWhiteSpace(dir) ? null : dir.Trim();
        }

        public static bool HasExportDir(Context ctx) => GetExportDir(ctx) != null;

        public static void SetExportDir(Context ctx, string path) =>
            Prefs(ctx).Edit().PutString(KeyExportDir, path == null ? "" : path.Trim()).Apply();
    }
}
