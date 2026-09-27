using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PZLauncher;

internal sealed class Sidecar
{
    public string WorkshopId { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
}

internal sealed class StagedJar
{
    public string Name { get; set; } = string.Empty;
    public string StagingPath { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string WorkshopId { get; set; } = string.Empty;
    public bool UpdateAvailable { get; set; }
    public string UpdateSource { get; set; } = string.Empty;
}

internal static class Javamod
{
    public static string UserModsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Zomboid", "mods");

    public static string SteamWorkshopContent(string steamDir) =>
        Path.Combine(steamDir, "steamapps", "workshop", "content", "108600");

    public static string ReadGameVersion(string userProfileZomboid)
    {
        try
        {
            var console = Path.Combine(userProfileZomboid, "console.txt");
            if (!File.Exists(console)) return string.Empty;
            var text = File.ReadAllText(console);
            var m = Regex.Match(text, @"version=(\d+\.\d+(?:\.\d+)?)");
            return m.Success ? m.Groups[1].Value : string.Empty;
        }
        catch { return string.Empty; }
    }

    public static List<string> ListInstalledModNames()
    {
        var result = new List<string>();
        try
        {
            var dir = UserModsDir;
            if (!Directory.Exists(dir)) return result;
            result.AddRange(Directory.GetDirectories(dir).Select(Path.GetFileName)
                .Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).OrderBy(x => x));
        }
        catch { }
        return result;
    }

    public static string FindModInfo(string modDir, string gameVersion)
    {
        try
        {
            var cands = new List<(Version Ver, string Path)>();
            foreach (var d in Directory.GetDirectories(modDir))
            {
                var name = Path.GetFileName(d);
                if (name.Equals("common", StringComparison.OrdinalIgnoreCase)) continue;
                if (Version.TryParse(NormalizeVer(name), out var v))
                {
                    var mi = Path.Combine(d, "mod.info");
                    if (File.Exists(mi)) cands.Add((v, mi));
                }
            }
            Version gv = new(0, 0);
            if (!string.IsNullOrEmpty(gameVersion)) Version.TryParse(NormalizeVer(gameVersion), out gv);
            var best = cands.Where(c => c.Ver <= gv).OrderByDescending(c => c.Ver).FirstOrDefault();
            if (best.Path != null) return best.Path;
            foreach (var f in new[] { Path.Combine(modDir, "common", "mod.info"), Path.Combine(modDir, "mod.info") })
                if (File.Exists(f)) return f;
        }
        catch { }
        return string.Empty;
    }

    private static string NormalizeVer(string s)
    {
        var parts = s.Split('.');
        while (parts.Length < 2) parts = parts.Concat(new[] { "0" }).ToArray();
        return string.Join(".", parts.Take(4));
    }

    public static string ReadModInfoValue(string modInfoPath, string key)
    {
        try
        {
            foreach (var line in File.ReadAllLines(modInfoPath))
            {
                var t = line.Trim();
                if (t.StartsWith(key + "=", StringComparison.Ordinal)) return t.Substring(key.Length + 1);
            }
        }
        catch { }
        return string.Empty;
    }

    public static string ResolveLibsDir(string modDir, string modInfoPath, string javaJarFile)
    {
        try
        {
            var baseDir = Path.GetDirectoryName(modInfoPath) ?? modDir;
            var rel = (javaJarFile ?? string.Empty).Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(rel)) rel = Path.Combine("media", "java", "client", "libs", "x.jar");
            return Path.GetDirectoryName(Path.Combine(baseDir, rel)) ?? baseDir;
        }
        catch { return modDir; }
    }

    public static List<StagedJar> ListStaged(string stagingDir, string templateLibsDir)
    {
        var list = new List<StagedJar>();
        try
        {
            if (!Directory.Exists(stagingDir)) return list;
            foreach (var f in Directory.GetFiles(stagingDir, "*.jar").OrderBy(Path.GetFileName))
            {
                var name = Path.GetFileName(f);
                var sc = LoadSidecar(f + ".pzsrc.json");
                var deployed = Path.Combine(templateLibsDir, name);
                list.Add(new StagedJar
                {
                    Name = name,
                    StagingPath = f,
                    Enabled = File.Exists(deployed),
                    WorkshopId = sc?.WorkshopId ?? string.Empty,
                });
            }
        }
        catch { }
        return list;
    }

    public static Sidecar LoadSidecar(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<Sidecar>(File.ReadAllText(path));
        }
        catch { return null; }
    }

    public static void SaveSidecar(string jarPath, string workshopId)
    {
        try
        {
            var sc = new Sidecar { WorkshopId = workshopId ?? string.Empty, File = Path.GetFileName(jarPath) };
            File.WriteAllText(jarPath + ".pzsrc.json",
                JsonSerializer.Serialize(sc, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static string Sha256(string path)
    {
        try
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", string.Empty);
        }
        catch { return string.Empty; }
    }

    public static string FindWorkshopJar(string contentDir, string workshopId, string jarName)
    {
        try
        {
            var root = Path.Combine(contentDir, workshopId, "mods");
            if (!Directory.Exists(root)) return string.Empty;
            foreach (var f in Directory.GetFiles(root, jarName, SearchOption.AllDirectories))
                return f;
            foreach (var f in Directory.GetFiles(root, "*.jar", SearchOption.AllDirectories))
                if (Path.GetFileName(f).Equals(jarName, StringComparison.OrdinalIgnoreCase)) return f;
        }
        catch { }
        return string.Empty;
    }

    public static string ReadZipTextEntry(string zipPath, Func<string, bool> match)
    {
        try
        {
            using var fs = File.OpenRead(zipPath);
            using var z = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read);
            foreach (var e in z.Entries)
            {
                if (!match(e.FullName)) continue;
                using var s = e.Open();
                using var r = new StreamReader(s);
                return r.ReadToEnd();
            }
        }
        catch { }
        return string.Empty;
    }

    public static string ReadGameRevision(string gameDir)
    {
        try
        {
            var jar = Path.Combine(gameDir, "projectzomboid.jar");
            var txt = ReadZipTextEntry(jar, n => n.EndsWith("GitVersion.class", StringComparison.Ordinal));
            if (!string.IsNullOrEmpty(txt))
            {
                var m = Regex.Match(txt, @"\b[0-9a-f]{7,40}\b");
                if (m.Success) return m.Value;
            }
        }
        catch { }
        try
        {
            var console = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Zomboid", "console.txt");
            if (File.Exists(console))
            {
                var m = Regex.Match(File.ReadAllText(console), @"revision=([0-9a-fA-F]+)");
                if (m.Success) return m.Groups[1].Value;
            }
        }
        catch { }
        return string.Empty;
    }

    public static string ReadAgentRevision(string agentJar)
    {
        try
        {
            var txt = ReadZipTextEntry(agentJar,
                n => n.EndsWith("build-info.properties", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(txt))
            {
                var m = Regex.Match(txt, @"(?m)^\s*revision\s*=\s*(.+?)\s*$");
                if (m.Success) return m.Groups[1].Value;
            }
        }
        catch { }
        return string.Empty;
    }
}
