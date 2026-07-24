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
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using AndroidX.Core.Hardware.Fingerprint;
using AndroidX.Fragment.App;
using Google.Android.Material.Dialog;
using Javax.Crypto;

namespace keepass2android.Theming
{
    /// <summary>
    /// An app-drawn fingerprint prompt in the theme colours. The system BiometricPrompt renders
    /// outside the app and ignores runtime theme overrides, so where the compat fingerprint API
    /// is usable we draw our own dialog; callers fall back to the system prompt otherwise.
    /// </summary>
    public class ShiroikumaFingerprintDialog : FingerprintManagerCompat.AuthenticationCallback
    {
        private readonly FragmentActivity _activity;
        private readonly IBiometricAuthCallback _callback;
        private readonly Action _fallback;
        private AndroidX.AppCompat.App.AlertDialog _dialog;
        private AndroidX.Core.OS.CancellationSignal _cancellationSignal;
        private TextView _status;
        private bool _stopped;
        private bool _hadSensorContact;
        private DateTime _startedUtc;

        // EMUI hides the app's logcat output entirely, so the fork fingerprint path also appends
        // its probe results to <external-files>/fp-probe.txt, readable via adb.
        private static Context _logContext;
        private static void LogInfo(string message)
        {
            Android.Util.Log.Info("KP2A", message);
            try
            {
                string dir = _logContext?.GetExternalFilesDir(null)?.AbsolutePath;
                if (dir != null)
                    System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "fp-probe.txt"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + "\n");
            }
            catch (Exception)
            {
                // never let diagnostics break authentication
            }
        }

        public static bool TryStart(FragmentActivity activity, Cipher cipher,
            IBiometricAuthCallback callback, Action fallback, out ShiroikumaFingerprintDialog dialog)
        {
            dialog = null;
            ShiroikumaFingerprintDialog d = null;
            _logContext = activity;
            try
            {
                var fpm = FingerprintManagerCompat.From(activity);
                bool hardware = fpm.IsHardwareDetected;
                bool enrolled = fpm.HasEnrolledFingerprints;
                LogInfo("FP: fork dialog probe: hardware=" + hardware + " enrolled=" + enrolled);
                if (!hardware)
                    return false;
                // Some Huawei builds report enrolled=false through the legacy API even with
                // fingerprints registered — attempt anyway; an immediate error auto-falls back.
                d = new ShiroikumaFingerprintDialog(activity, callback, fallback);
                d.Show(fpm, cipher);
                dialog = d;
                return true;
            }
            catch (Exception e)
            {
                LogInfo("FP: fork fingerprint dialog unavailable, falling back to BiometricPrompt: " + e);
                d?.Dismiss();
                dialog = null;
                return false;
            }
        }

        private ShiroikumaFingerprintDialog(FragmentActivity activity, IBiometricAuthCallback callback, Action fallback)
        {
            _activity = activity;
            _callback = callback;
            _fallback = fallback;
        }

        private void Show(FingerprintManagerCompat fpm, Cipher cipher)
        {
            Context ctx = _activity;

            var root = new LinearLayout(ctx) { Orientation = Orientation.Vertical };
            root.SetPadding(Dp(24), Dp(24), Dp(24), Dp(8));
            root.SetGravity(GravityFlags.CenterHorizontal);

            var title = new TextView(ctx) { Text = ctx.GetString(AppNames.AppNameResource) };
            title.SetTextSize(Android.Util.ComplexUnitType.Sp, 20);
            title.SetTypeface(title.Typeface, TypefaceStyle.Bold);
            root.AddView(title);

            var subtitle = new TextView(ctx) { Text = ctx.GetString(Resource.String.unlock_database_title) };
            subtitle.SetPadding(0, Dp(4), 0, 0);
            root.AddView(subtitle);

            var icon = new ImageView(ctx);
            icon.SetImageResource(Resource.Drawable.baseline_fingerprint_24);
            var iconParams = new LinearLayout.LayoutParams(Dp(64), Dp(64))
            {
                TopMargin = Dp(20),
                BottomMargin = Dp(8),
                Gravity = GravityFlags.CenterHorizontal
            };
            root.AddView(icon, iconParams);

            _status = new TextView(ctx) { Text = "", Gravity = GravityFlags.CenterHorizontal };
            _status.SetPadding(0, Dp(4), 0, 0);
            root.AddView(_status);

            Kp2aTheme.ApplyColorAndFont(title, ThemeSlot.PageText, ctx);
            Kp2aTheme.ApplyColorAndFont(subtitle, ThemeSlot.PageText, ctx);
            Kp2aTheme.ApplyColorAndFont(_status, ThemeSlot.PageText, ctx);
            if (Kp2aTheme.TryColor(ctx, ThemeSlot.Accent, out var accent))
                icon.SetColorFilter(accent);

            var builder = new MaterialAlertDialogBuilder(_activity);
            builder.SetView(root);
            builder.SetNegativeButton(Android.Resource.String.Cancel, (s, e) => Stop());
            builder.SetCancelable(false);
            _dialog = builder.Create();
            _dialog.Show();
            Kp2aTheme.ApplyAlertDialog(_dialog);

            _cancellationSignal = new AndroidX.Core.OS.CancellationSignal();
            _startedUtc = DateTime.UtcNow;
            fpm.Authenticate(new FingerprintManagerCompat.CryptoObject(cipher), 0, _cancellationSignal, this, null);
            LogInfo("FP: fork dialog listening");
        }

        public void Stop()
        {
            _stopped = true;
            try { _cancellationSignal?.Cancel(); }
            catch (Exception e) { Kp2aLog.Log("FP: cancel failed: " + e); }
            Dismiss();
        }

        private void Dismiss()
        {
            try
            {
                if (_dialog?.IsShowing == true)
                    _dialog.Dismiss();
            }
            catch (Exception e) { Kp2aLog.Log("FP: dismiss failed: " + e); }
        }

        public override void OnAuthenticationSucceeded(FingerprintManagerCompat.AuthenticationResult result)
        {
            if (_stopped)
                return;
            _stopped = true;
            Dismiss();
            _callback.OnBiometricAuthSucceeded();
        }

        public override void OnAuthenticationError(int errMsgId, Java.Lang.ICharSequence errString)
        {
            if (_stopped)
                return;
            _stopped = true;
            Dismiss();
            LogInfo("FP: fork dialog error " + errMsgId + ": " + errString);
            // An error before any sensor contact, right after starting, means the legacy API is
            // not usable on this device — silently hand over to the system BiometricPrompt.
            if (!_hadSensorContact && (DateTime.UtcNow - _startedUtc).TotalSeconds < 3 && _fallback != null)
            {
                LogInfo("FP: fork dialog auto-fallback to BiometricPrompt");
                _fallback();
                return;
            }
            _callback.OnBiometricError(errString?.ToString() ?? "");
        }

        public override void OnAuthenticationFailed()
        {
            if (_stopped)
                return;
            _hadSensorContact = true;
            string message = _activity.GetString(Resource.String.fingerprint_not_recognized);
            if (_status != null)
                _status.Text = message;
            _callback.OnBiometricAttemptFailed(message);
        }

        public override void OnAuthenticationHelp(int helpMsgId, Java.Lang.ICharSequence helpString)
        {
            if (_stopped)
                return;
            _hadSensorContact = true;
            if (_status != null)
                _status.Text = helpString?.ToString() ?? "";
        }

        private int Dp(int dp) =>
            (int)(dp * _activity.Resources.DisplayMetrics.Density + 0.5f);
    }
}
