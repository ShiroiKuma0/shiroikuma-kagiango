// This file is part of Keepass2Android, Copyright 2025 Philipp Crocoll.
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

using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Graphics;
using Android.OS;
using Android.Preferences;
using Android.Runtime;
using Android.Text;
using Android.Text.Method;
using Android.Text.Util;
using Android.Util;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using AndroidX.Core.Content;
using Google.Android.Material.Dialog;
using keepass2android;

namespace keepass2android
{
  public static class ChangeLog
  {
    public static void ShowChangeLog(Context ctx, Action onDismiss)
    {
      MaterialAlertDialogBuilder builder = new MaterialAlertDialogBuilder(ctx);
      builder.SetTitle(ctx.GetString(Resource.String.ChangeLog_title));
      List<string> changeLog = new List<string>{
                // Fork (白い熊 鍵暗号): our changes, newest first. All fork work sits on top of
                // upstream 1.15-r2, so the chronological merge places these above Version 1.15.
                BuildForkChangelogString("1.15-r2+13", "2026-07-24", new[]{
                    "Black-yellow title row and status bar on the start, database-selection and QuickUnlock screens"}),
                BuildForkChangelogString("1.15-r2+12", "2026-07-24", new[]{
                    "Fingerprint: the unlock screen's automatic prompt now uses the themed fork dialog (it was bypassing it and going straight to the system prompt)"}),
                BuildForkChangelogString("1.15-r2+11", "2026-07-24", new[]{
                    "Change log: side borders no longer covered by the text pane",
                    "Fingerprint dialog: try the themed prompt on Huawei despite the legacy API quirks, with silent fallback to the system prompt"}),
                BuildForkChangelogString("1.15-r2+10", "2026-07-24", new[]{
                    "Black-yellow change log — fork changes merged chronologically with the upstream log",
                    "Black-yellow fingerprint unlock dialog (app-drawn prompt instead of the system one)"}),
                BuildForkChangelogString("1.15-r2+9", "2026-07-24", new[]{
                    "Black-yellow settings pages and entry view; new colour/font slots on the 白い熊 鍵暗号 UI page"}),
                BuildForkChangelogString("1.15-r2+3", "2026-06-08", new[]{
                    "Black-yellow launcher icon"}),
                BuildForkChangelogString("1.15-r2+2", "2026-06-05", new[]{
                    "白い熊 鍵暗号 UI page: per-element colours and fonts, app language; signature black-yellow palette across unlock screen, entry/group lists, title rows, list icons and buttons"}),
                BuildForkChangelogString("1.15-r2+1", "2026-06-05", new[]{
                    "Fork of keepass2android as 白い熊 鍵暗号 (shiroikuma.kagiango): installs alongside the original, offline (NoNet) flavor only"}),
                BuildChangelogString(ctx, new List<int>{Resource.Array.ChangeLog_1_15,
#if !NoNet
                    Resource.Array.ChangeLog_1_15_net
#endif
				}, "1.15"),
                BuildChangelogString(ctx, new List<int>
                {
                    Resource.Array.ChangeLog_1_14,
#if !NoNet
                    Resource.Array.ChangeLog_1_14_net
#endif
                }, "1.14"),


                BuildChangelogString(ctx, new List<int>{Resource.Array.ChangeLog_1_13}, "1.13"),

                BuildChangelogString(ctx, new List<int>{Resource.Array.ChangeLog_1_12
#if !NoNet
					,Resource.Array.ChangeLog_1_12_net
#endif
                }, "1.12"),
                BuildChangelogString(ctx, new List<int>{Resource.Array.ChangeLog_1_11
#if !NoNet
                    ,Resource.Array.ChangeLog_1_11_net
#endif
                }, "1.11"),
                BuildChangelogString(ctx, Resource.Array.ChangeLog_1_10, "1.10"),
                BuildChangelogString(ctx, Resource.Array.ChangeLog_1_09e, "1.09e"),
                BuildChangelogString(ctx, Resource.Array.ChangeLog_1_09d, "1.09d"),
                BuildChangelogString(ctx, Resource.Array.ChangeLog_1_09c, "1.09c"),
                BuildChangelogString(ctx, Resource.Array.ChangeLog_1_09b, "1.09b"),
                BuildChangelogString(ctx, Resource.Array.ChangeLog_1_09a, "1.09a"),
                BuildChangelogString(ctx, Resource.Array.ChangeLog_1_08d, "1.08d"),
                BuildChangelogString(ctx, Resource.Array.ChangeLog_1_08c, "1.08c"),
                BuildChangelogString(ctx, Resource.Array.ChangeLog_1_08b, "1.08b"),
                BuildChangelogString(ctx, Resource.Array.ChangeLog_1_08, "1.08"),
                ctx.GetString(Resource.String.ChangeLog_1_07b),
                ctx.GetString(Resource.String.ChangeLog_1_07),
                ctx.GetString(Resource.String.ChangeLog_1_06),
                ctx.GetString(Resource.String.ChangeLog_1_05),
                ctx.GetString(Resource.String.ChangeLog_1_04b),
                ctx.GetString(Resource.String.ChangeLog_1_04),
                ctx.GetString(Resource.String.ChangeLog_1_03),
                ctx.GetString(Resource.String.ChangeLog_1_02),
#if !NoNet
				ctx.GetString(Resource.String.ChangeLog_1_01g),
                ctx.GetString(Resource.String.ChangeLog_1_01d),
#endif
				ctx.GetString(Resource.String.ChangeLog_1_01),
                ctx.GetString(Resource.String.ChangeLog_1_0_0e),
                ctx.GetString(Resource.String.ChangeLog_1_0_0),
                ctx.GetString(Resource.String.ChangeLog_0_9_9c),
                ctx.GetString(Resource.String.ChangeLog_0_9_9),
                ctx.GetString(Resource.String.ChangeLog_0_9_8c),
                    ctx.GetString(Resource.String.ChangeLog_0_9_8b),
                    ctx.GetString(Resource.String.ChangeLog_0_9_8),
#if !NoNet
					//0.9.7b fixes were already included in 0.9.7 offline
					ctx.GetString(Resource.String.ChangeLog_0_9_7b),
#endif
					ctx.GetString(Resource.String.ChangeLog_0_9_7),
                    ctx.GetString(Resource.String.ChangeLog_0_9_6),
                    ctx.GetString(Resource.String.ChangeLog_0_9_5),
                    ctx.GetString(Resource.String.ChangeLog_0_9_4),
                    ctx.GetString(Resource.String.ChangeLog_0_9_3_r5),
                    ctx.GetString(Resource.String.ChangeLog_0_9_3),
                    ctx.GetString(Resource.String.ChangeLog_0_9_2),
                    ctx.GetString(Resource.String.ChangeLog_0_9_1),
                    ctx.GetString(Resource.String.ChangeLog_0_9),
                    ctx.GetString(Resource.String.ChangeLog_0_8_6),
                    ctx.GetString(Resource.String.ChangeLog_0_8_5),
                    ctx.GetString(Resource.String.ChangeLog_0_8_4),
                    ctx.GetString(Resource.String.ChangeLog_0_8_3),
                    ctx.GetString(Resource.String.ChangeLog_0_8_2),
                    ctx.GetString(Resource.String.ChangeLog_0_8_1),
                    ctx.GetString(Resource.String.ChangeLog_0_8),
                    ctx.GetString(Resource.String.ChangeLog_0_7),
                    ctx.GetString(Resource.String.ChangeLog)
                     };

      String version;
      try
      {
        PackageInfo packageInfo = ctx.PackageManager.GetPackageInfo(ctx.PackageName, 0);
        version = packageInfo.VersionName;

      }
      catch (PackageManager.NameNotFoundException)
      {
        version = "";
      }

      string warning = "";
      if (version.Contains("pre"))
      {
        warning = ctx.GetString(Resource.String.PreviewWarning);
      }

      builder.SetPositiveButton(Android.Resource.String.Ok, (dlgSender, dlgEvt) => { ((AndroidX.AppCompat.App.AlertDialog)dlgSender).Dismiss(); });
      builder.SetCancelable(false);

      WebView wv = new WebView(ctx);

      // Fork (白い熊 鍵暗号 UI): pad the WebView so its opaque background does not paint over
      // the themed dialog border on the left/right edges.
      var wvContainer = new FrameLayout(ctx);
      int sidePad = (int)(4 * ctx.Resources.DisplayMetrics.Density + 0.5f);
      wvContainer.SetPadding(sidePad, 0, sidePad, 0);
      wvContainer.AddView(wv);

      builder.SetView(wvContainer);
      Dialog dialog = builder.Create();
      dialog.DismissEvent += (sender, e) =>
      {
        onDismiss();
      };
      // Fork (白い熊 鍵暗号 UI): the WebView carries the themed page background; the dialog
      // surface, title and OK button follow the theme too.
      if (Theming.Kp2aTheme.TryColor(ctx, Theming.ThemeSlot.PageBackground, out var wvBg))
        wv.SetBackgroundColor(wvBg);
      else
        wv.SetBackgroundColor(Color.Transparent);
      wv.LoadDataWithBaseURL(null, GetLog(changeLog, warning, dialog.Context), "text/html", "UTF-8", null);

      dialog.Show();
      Theming.Kp2aTheme.ApplyAlertDialog(dialog);
    }

    private static string BuildChangelogString(Context ctx, int changeLogResId, string version)
    {
      return BuildChangelogString(ctx, new List<int>() { changeLogResId }, version);

    }

    // Fork (白い熊 鍵暗号): one fork-version block in the same format as the upstream entries.
    private static string BuildForkChangelogString(string version, string date, string[] items)
    {
      string result = "白い熊 鍵暗号 " + version + " (" + date + ")\n";
      foreach (var item in items)
        result += " * " + item + "\n";
      return result;
    }


    private static string BuildChangelogString(Context ctx, List<int> changeLogResIds, string version)
    {
      string result = "Version " + version + "\n";
      string previous = "";
      foreach (var changeLogResId in changeLogResIds)
      {
        foreach (var item in ctx.Resources.GetStringArray(changeLogResId))
        {
          if (item == previous) //there was some trouble with crowdin translations, remove duplicates
            continue;
          result += " * " + item + "\n";
          previous = item;
        }
      }

      return result;

    }

    private const string HtmlEnd = @"</body>
</html>";

    private static string GetLog(List<string> changeLog, string warning, Context ctx)
    {
      string secondaryColor = "31628D";
      string onSurfaceColor = "171D1E";
      if (((int)ctx.Resources.Configuration.UiMode & (int)UiMode.NightMask) == (int)UiMode.NightYes)
      {
        secondaryColor = "99CBFF";
        onSurfaceColor = "E1E4D6";
      }

      // Fork (白い熊 鍵暗号 UI): drive the changelog colours from the theme slots.
      string bodyCss = "";
      if (Theming.Kp2aTheme.TryColor(ctx, Theming.ThemeSlot.Accent, out var accentColor))
        secondaryColor = (accentColor.ToArgb() & 0xFFFFFF).ToString("X6");
      if (Theming.Kp2aTheme.TryColor(ctx, Theming.ThemeSlot.PageText, out var pageTextColor))
        onSurfaceColor = (pageTextColor.ToArgb() & 0xFFFFFF).ToString("X6");
      if (Theming.Kp2aTheme.TryColor(ctx, Theming.ThemeSlot.PageBackground, out var pageBgColor))
        bodyCss = "      body         { background-color:#" + (pageBgColor.ToArgb() & 0xFFFFFF).ToString("X6") + " }\n";

      string HtmlStart = @"<html>
  <head>
    <style type='text/css'>
" + bodyCss + @"      a            { color:#" + onSurfaceColor + @" }
      div.title    {
          color:#" + secondaryColor + @";
          font-size:1.2em;
          font-weight:bold;
          margin-top:1em;
          margin-bottom:0.5em;
          text-align:center }
      div.subtitle {
          color:#" + secondaryColor + @";
          font-size:0.8em;
          margin-bottom:1em;
          text-align:center }
      div.freetext { color:#" + onSurfaceColor + @" }
      div.list     { color:#" + onSurfaceColor + @" }
    </style>
  </head>
  <body>";


      StringBuilder sb = new StringBuilder(HtmlStart);
      if (!string.IsNullOrEmpty(warning))
      {
        sb.Append(warning);
      }
      bool inList = false;
      bool isFirst = true;
      foreach (string versionLog in changeLog)
      {
        string versionLog2 = versionLog;
        bool title = true;
        if (isFirst)
        {

          bool showDonateOption = true;
          ISharedPreferences prefs = PreferenceManager.GetDefaultSharedPreferences(ctx);
          if (prefs.GetBoolean(ctx.GetString(Resource.String.NoDonationReminder_key), false))
            showDonateOption = false;

          long usageCount = prefs.GetLong(ctx.GetString(Resource.String.UsageCount_key), 0);

          if (usageCount <= 5)
            showDonateOption = false;

          if (showDonateOption)
          {
            if (versionLog2.EndsWith("\n") == false)
              versionLog2 += "\n";
            string donateUrl = ctx.GetString(Resource.String.donate_url,
                new Java.Lang.Object[]{ctx.Resources.Configuration.Locale.Language,
                                ctx.PackageName
                });

            versionLog2 += " * <a href=\"" + donateUrl
                           + "\">" +
                           ctx.GetString(Resource.String.ChangeLog_keptDonate)
                           + "<a/>";
          }
          isFirst = false;
        }
        foreach (string line in versionLog2.Split('\n'))
        {
          string w = line.Trim();
          if (title)
          {
            if (inList)
            {
              sb.Append("</ul></div>\n");
              inList = false;
            }
            w = w.Replace("<b>", "");
            w = w.Replace("</b>", "");
            w = w.Replace("\\n", "");
            sb.Append("<div class='title'>"
                    + w.Trim() + "</div>\n");
            title = false;
          }
          else
          {
            w = w.Replace("\\n", "<br />");
            if ((w.StartsWith("*") || (w.StartsWith("•"))))
            {
              if (!inList)
              {
                sb.Append("<div class='list'><ul>\n");
                inList = true;
              }
              sb.Append("<li>");
              sb.Append(w.Substring(1).Trim());
              sb.Append("</li>\n");
            }
            else
            {
              if (inList)
              {
                sb.Append("</ul></div>\n");
                inList = false;
              }
              sb.Append(w);
            }
          }
        }
      }
      sb.Append(HtmlEnd);
      return sb.ToString();
    }
  }
}