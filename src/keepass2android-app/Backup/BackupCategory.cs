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

namespace keepass2android.Backup
{
    /// <summary>
    /// One independently selectable part of a backup. Each top-level category becomes a single
    /// <c>&lt;id&gt;.json</c> entry inside the export ZIP; the id is also what the automation contract
    /// accepts in its <c>items</c> extra, so these ids are a wire format — do not rename them lightly.
    /// <para>
    /// A category with a <see cref="ParentId"/> is a sub-option: it is listed under its parent and is
    /// independently selectable (the contract's optional third <c>parent-id</c> field).
    /// </para>
    /// </summary>
    public sealed class BackupCategory
    {
        public string Id;
        public string ParentId;
        public int LabelRes;

        /// <summary>Preference XML screens whose declared <c>android:key</c>s belong to this category.</summary>
        public int[] PrefXml = new int[0];

        /// <summary>Key prefixes owned by this category (the 白い熊 UI page writes its own keys).</summary>
        public string[] KeyPrefixes = new string[0];

        /// <summary>Settable values written from code rather than declared in a preference XML.</summary>
        public string[] ExtraKeys = new string[0];

        /// <summary>True for the payload category that carries files rather than a prefs dump.</summary>
        public bool IsFiles;

        /// <summary>
        /// Whether this category starts <b>ticked</b> — in the app's own panel and in the automation
        /// contract's optional fourth <c>on|off</c> field alike, so 保存復元's item picker and the
        /// Export/Import sheet open on the same answer. <c>true</c> unless a category is derived,
        /// disposable or regenerated on use; the field defaults to <c>true</c>, so every category that
        /// does not say otherwise is unchanged.
        /// </summary>
        public bool DefaultSelected = true;

        /// <summary>
        /// Every category of this app, in the order they appear in the panel and in the ZIP. The split
        /// follows the app's own settings structure — one category per settings screen — plus the two
        /// 白い熊 UI page groups (colours, fonts) and the code-managed password-generator profiles.
        /// </summary>
        public static readonly BackupCategory[] All =
        {
            new BackupCategory { Id = "appearance",         LabelRes = Resource.String.backup_cat_appearance,
                                 KeyPrefixes = new[] { "theme_color_" } },
            new BackupCategory { Id = "fonts",              LabelRes = Resource.String.backup_cat_fonts,
                                 KeyPrefixes = new[] { "font_family_", "font_weight_", "font_size_" } },
            new BackupCategory { Id = "fonts.files",        ParentId = "fonts", LabelRes = Resource.String.backup_cat_fonts_files,
                                 IsFiles = true },
            new BackupCategory { Id = "display",            LabelRes = Resource.String.backup_cat_display,
                                 PrefXml = new[] { Resource.Xml.pref_app_display } },
            new BackupCategory { Id = "security",           LabelRes = Resource.String.backup_cat_security,
                                 PrefXml = new[] { Resource.Xml.pref_app_security },
                                 ExtraKeys = new[] { "no_secure_display_check" } },
            new BackupCategory { Id = "quick_unlock",       LabelRes = Resource.String.backup_cat_quick_unlock,
                                 PrefXml = new[] { Resource.Xml.pref_app_quick_unlock } },
            new BackupCategory { Id = "password_access",    LabelRes = Resource.String.backup_cat_password_access,
                                 PrefXml = new[] { Resource.Xml.pref_app_password_access,
                                                   Resource.Xml.pref_app_password_access_autofill,
                                                   Resource.Xml.pref_app_password_access_autofill_totp,
                                                   Resource.Xml.pref_app_password_access_keyboard_switch },
                                 ExtraKeys = new[] { "AutoFillDisabledQueries", "AutoFillTrustedLinks" } },
            new BackupCategory { Id = "file_handling",      LabelRes = Resource.String.backup_cat_file_handling,
                                 PrefXml = new[] { Resource.Xml.pref_app_file_handling } },
            new BackupCategory { Id = "tray_totp",          LabelRes = Resource.String.backup_cat_tray_totp,
                                 PrefXml = new[] { Resource.Xml.pref_app_traytotp } },
            new BackupCategory { Id = "password_generator", LabelRes = Resource.String.backup_cat_password_generator,
                                 ExtraKeys = new[] { "password_generator_profiles" } },
            // Unticked by default: the debug log is derived, disposable and regenerated on use.
            new BackupCategory { Id = "debug",              LabelRes = Resource.String.backup_cat_debug,
                                 PrefXml = new[] { Resource.Xml.pref_app_debug },
                                 DefaultSelected = false },
        };

        public static BackupCategory ById(string id) => All.FirstOrDefault(c => c.Id == id);

        public static bool IsKnownId(string id) => ById(id) != null;

        public static IEnumerable<BackupCategory> Children(string parentId) =>
            All.Where(c => c.ParentId == parentId);

        /// <summary>Ids of every category, parents before their children (the contract's list order).</summary>
        public static string[] AllIds => All.Select(c => c.Id).ToArray();

        /// <summary>
        /// The default set — every category flagged <see cref="DefaultSelected"/>. This is what an
        /// automation request with no <c>items</c> extra exports, and what both pickers open on.
        /// </summary>
        public static string[] DefaultIds => All.Where(c => c.DefaultSelected).Select(c => c.Id).ToArray();

        // ---- what may leave the app ------------------------------------------------------------------

        /// <summary>
        /// Preference keys that must NEVER be exported, whatever else matches. This is a password
        /// manager: the biometric unlock stores the Keystore-wrapped master password under
        /// <c>kp2a_ioc_&lt;ioc-hex&gt;</c> (plus <c>_iv</c> / <c>_mode</c> companions), and
        /// <c>KP2A.PasswordAct.AuxFileIoc*</c> holds serialized <c>IOConnectionInfo</c>s whose text can
        /// carry a remote-storage user name and password. None of that belongs in a plain ZIP sitting in
        /// a shared backup folder — not even in ciphertext.
        /// <para>
        /// The export is allow-listed anyway (only the keys a category claims are written), so this list
        /// is the second lock rather than the only one, and it is applied on import too so a doctored
        /// ZIP cannot inject a credential key back into the app.
        /// </para>
        /// </summary>
        private static readonly string[] NeverExport =
        {
            "kp2a_ioc_",
            "KP2A.PasswordAct.AuxFileIoc",
        };

        public static bool IsForbiddenKey(string key) =>
            key == null || NeverExport.Any(p => key.StartsWith(p, StringComparison.Ordinal));

        /// <summary>
        /// The preference keys this category owns: everything its settings screens declare, its key
        /// prefixes, and its code-managed extras — minus anything on <see cref="NeverExport"/>.
        /// </summary>
        public ISet<string> ResolveKeys(Context ctx)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (int xml in PrefXml)
                CollectXmlKeys(ctx, xml, keys);
            foreach (string k in ExtraKeys)
                keys.Add(k);
            keys.RemoveWhere(IsForbiddenKey);
            return keys;
        }

        /// <summary>True when <paramref name="key"/> belongs to this category (prefixes included).</summary>
        public bool Owns(Context ctx, string key, ISet<string> resolved) =>
            !IsForbiddenKey(key) &&
            (resolved.Contains(key) || KeyPrefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal)));

        /// <summary>
        /// Read the <c>android:key</c> of every preference declared in a settings screen. Scanning the
        /// XML rather than hard-coding a list means a key upstream adds in a future release is picked up
        /// by the next rebase without anyone remembering to update this file.
        /// </summary>
        private static void CollectXmlKeys(Context ctx, int xmlRes, ISet<string> into)
        {
            const string androidNs = "http://schemas.android.com/apk/res/android";
            try
            {
                using (var parser = ctx.Resources.GetXml(xmlRes))
                {
                    while (parser.Read())
                    {
                        if (parser.NodeType != System.Xml.XmlNodeType.Element)
                            continue;
                        string value = parser.GetAttribute("key", androidNs);
                        if (string.IsNullOrEmpty(value))
                            continue;
                        // Compiled XML hands back "@<resource-id>" for an @string/… reference.
                        if (value[0] == '@' && int.TryParse(value.Substring(1), out int id) && id != 0)
                        {
                            try { value = ctx.Resources.GetString(id); }
                            catch (Exception) { continue; }
                        }
                        if (!string.IsNullOrEmpty(value))
                            into.Add(value);
                    }
                }
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Backup: could not scan preference screen " + xmlRes + ": " + e);
            }
        }
    }
}
