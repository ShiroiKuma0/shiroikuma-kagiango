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
using System.Text;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Java.Security;

namespace keepass2android.Backup
{
    /// <summary>
    /// Who is allowed through the data door (<see cref="AutomationProvider"/>), and how that is decided.
    /// Ported from 自由作業盤's <c>AutomationCallers.kt</c>; the pinned hashes are byte-identical to it.
    ///
    /// <para><b>Why not a token.</b> The token this replaces was a 48-character secret 白い熊 pasted from
    /// one app's settings into another's. It cannot survive a wipe, which is fatal for the case the family
    /// now exists to serve: 応用管理 restoring apps and their data onto a clean phone, where nothing is
    /// configured yet.</para>
    ///
    /// <para><b>Why not a <c>shiroikuma.*</c> prefix.</b> Because that is not an identity. What makes
    /// <see cref="ContentProvider.CallingPackage"/> worth anything is that a package name <b>cannot be
    /// taken while the real package is installed</b> — package names are not a namespace anyone owns, so
    /// any sideloaded app may call itself <c>shiroikuma.evil</c> and pass a prefix test. Since the caller
    /// supplies the file descriptor an export is written into, a prefix check would hand such an app the
    /// complete data of every sister app in turn: strictly weaker than the token it replaces.</para>
    ///
    /// <para><b>And in this app it matters more than in most.</b> 鍵暗号 is a password manager. Its export
    /// is deliberately allow-listed to settings and never carries the database, the master password or any
    /// stored credential (<see cref="BackupCategory.IsForbiddenKey"/>) — but the door itself still has to
    /// hold, because an <c>import</c> through it writes this app's preferences, and security settings are
    /// preferences.</para>
    /// </summary>
    public static class AutomationCallers
    {
        /// <summary>
        /// The apps allowed to drive this one's data door: 応用管理 backs up and restores, 自由作業盤 runs
        /// the 保存復元 batch. Nothing else has any business exporting another app's data, and an entry
        /// added here is a deliberate act.
        /// <para>
        /// Derive a pin with <c>apksigner verify --print-certs &lt;that app's signed release APK&gt;</c>.
        /// Every app in the family has its OWN keystore — there is no shared signing key to compare
        /// against, which is also why a <c>protectionLevel="signature"</c> permission was never an option.
        /// <b>If a caller's key is ever rotated its calls stop working and the fix is these constants</b> —
        /// that is the intended failure, because a signing key changing unnoticed is exactly what a pin is
        /// for.
        /// </para>
        /// </summary>
        private static readonly Dictionary<string, string> Callers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "shiroikuma.oyokanri",     "9c585f4d118cb97ff653f949a8872875548403b9083ce6b9baa2e8f0c55ac6cc" },
            { "shiroikuma.jiyusagyoban", "efd0d352192651593a92288ecdc64fc87262ec8648c24ed8f51a5587d46ac602" },
        };

        /// <summary>
        /// <c>null</c> when the caller is allowed through; otherwise the exact <c>ERROR:</c> line to answer
        /// with. A refusal that says only "no" is one nobody can debug from the other side of an IPC
        /// boundary — each of these is a different mistake with a different fix, and the caller shows them
        /// to 白い熊 verbatim.
        /// <para>Three things are checked, in order, and each exists because the one before it is not enough.</para>
        /// </summary>
        public static string Refuse(Context ctx, string declared)
        {
            // 1. An exact name — never a prefix. See the class remarks for why a prefix is not an identity.
            if (string.IsNullOrEmpty(declared))
                return "ERROR:caller unknown";
            if (!Callers.TryGetValue(declared, out string pin))
                return "ERROR:caller not permitted: " + declared;

            // 2. The kernel's answer, not the caller's. CallingPackage reflects the caller's DECLARED
            //    attribution, and packages sharing a uid are not distinguished by it; a uid cannot be
            //    borrowed.
            string[] real;
            try
            {
                real = ctx.PackageManager.GetPackagesForUid(Binder.CallingUid);
            }
            catch (Exception)
            {
                real = null;
            }
            if (real == null || !real.Contains(declared, StringComparer.Ordinal))
                return "ERROR:caller uid mismatch: " + declared;

            // 3. The signing certificate matches the pin. This closes the real gap, which is not
            //    restore-specific: whichever caller package is ABSENT from the device is a name anyone can
            //    take, and a clean phone is precisely a device where not everything is installed yet — the
            //    moment the assumption is weakest is the moment it is most needed.
            string signature = SigningSha256(ctx, declared);
            if (signature == null)
                return "ERROR:caller signature unreadable: " + declared;
            // Constant-time, like the token compare it replaces — the value is a public hash, but the habit
            // is worth keeping and costs nothing.
            if (!MessageDigest.IsEqual(Encoding.UTF8.GetBytes(signature), Encoding.UTF8.GetBytes(pin)))
                return "ERROR:caller signature mismatch: " + declared;

            return null;
        }

        /// <summary>
        /// The SHA-256 of the caller's current signing certificate, lower-case hex.
        /// <para>
        /// <c>SigningInfo</c> rather than the deprecated <c>Signatures</c>: a rotated key reports its whole
        /// history and we want the certificate actually in force. Both are read, because
        /// <c>GET_SIGNING_CERTIFICATES</c> is API 28 and this app's minSdk is 21 — on an older device the
        /// flag is accepted and <c>SigningInfo</c> comes back null, so WITHOUT the fallback the door would
        /// refuse every caller, a total failure that never appears on 白い熊's phone and would only surface
        /// on an older one. The deprecated array is the correct answer there, not a compromise: before key
        /// rotation existed, <c>signatures</c> WAS the signing certificate.
        /// </para>
        /// <para>
        /// Exactly one signer, or we decline to guess: "several signers, one of which matches" is a
        /// question about key rotation that nothing in this family needs to answer.
        /// </para>
        /// </summary>
        private static string SigningSha256(Context ctx, string packageName)
        {
            try
            {
                var pm = ctx.PackageManager;
                // Fully qualified: Java.Security.Signature (used for MessageDigest above) shares the name.
                IEnumerable<Android.Content.PM.Signature> certs;
                if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
                {
                    var info = pm.GetPackageInfo(packageName, PackageInfoFlags.SigningCertificates);
                    certs = info?.SigningInfo?.GetApkContentsSigners();
                }
                else
                {
#pragma warning disable CS0618
                    var info = pm.GetPackageInfo(packageName, PackageInfoFlags.Signatures);
                    certs = info?.Signatures;
#pragma warning restore CS0618
                }

                var only = certs?.ToList();
                if (only == null || only.Count != 1 || only[0] == null)
                    return null;

                byte[] digest = MessageDigest.GetInstance("SHA-256").Digest(only[0].ToByteArray());
                var sb = new StringBuilder(digest.Length * 2);
                foreach (byte b in digest)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
            catch (Exception e)
            {
                Kp2aLog.Log("Automation door: cannot read the signature of " + packageName + ": " + e);
                return null;
            }
        }
    }
}
