using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PZLauncher;

internal static class SteamFinder
{
    public const string PZAppId = "108600";

    public static string GetSteamDir()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            var v = key?.GetValue("InstallPath") as string;
            if (!string.IsNullOrEmpty(v) && Directory.Exists(v)) return v;
        }
        catch { }
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
            var v = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrEmpty(v) && Directory.Exists(v)) return v;
        }
        catch { }
        return string.Empty;
    }

    public static List<string> FindGameCandidates()
    {
        var result = new List<string>();
        var steamDir = GetSteamDir();
        if (string.IsNullOrEmpty(steamDir)) return result;

        var libs = new List<string> { Path.Combine(steamDir, "steamapps") };
        var foldersFile = Path.Combine(steamDir, "steamapps", "libraryfolders.vdf");
        if (File.Exists(foldersFile))
        {
            try
            {
                var text = File.ReadAllText(foldersFile);
                foreach (Match m in Regex.Matches(text, @"""path""\s+""((?:[^""\\]|\\.)*)"""))
                {
                    var p = m.Groups[1].Value.Replace(@"\\", @"\");
                    var apps = Path.Combine(p, "steamapps");
                    if (Directory.Exists(apps) && !libs.Contains(apps)) libs.Add(apps);
                }
            }
            catch { }
        }

        foreach (var apps in libs)
        {
            try
            {
                var dir = Path.Combine(apps, "common", "ProjectZomboid");
                if (File.Exists(Path.Combine(dir, "ProjectZomboid64.exe"))) result.Add(dir);
            }
            catch { }
        }
        return result;
    }

    public static (string BuildId, string LastUpdated) ReadAppManifest(string gameDir)
    {
        try
        {
            var apps = Path.GetDirectoryName(Path.GetDirectoryName(gameDir));
            if (apps == null) return (string.Empty, string.Empty);
            var manifest = Path.Combine(apps, $"appmanifest_{PZAppId}.acf");
            if (!File.Exists(manifest)) return (string.Empty, string.Empty);
            var text = File.ReadAllText(manifest);
            var build = Regex.Match(text, @"""buildid""\s+""(\d+)""");
            var upd = Regex.Match(text, @"""LastUpdated""\s+""(\d+)""");
            string date = string.Empty;
            if (upd.Success && long.TryParse(upd.Groups[1].Value, out var sec))
                date = DateTimeOffset.FromUnixTimeSeconds(sec).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
            return (build.Success ? build.Groups[1].Value : string.Empty, date);
        }
        catch { return (string.Empty, string.Empty); }
    }
}
