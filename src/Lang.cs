using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PZLauncher;

internal static class L
{
    private static Dictionary<string, string> _dict = new();
    public static string Code { get; private set; } = "en";
    public static int Count => _dict.Count;

    public static string LangDir =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lang");

    public static void Load(string code)
    {
        Code = string.IsNullOrEmpty(code) ? "en" : code;
        _dict.Clear();
        try
        {
            var path = Path.Combine(LangDir, Code + ".json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var d = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (d != null) _dict = d;
            }
        }
        catch { }
    }

    public static string T(string key, string en) =>
        _dict.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : en;

    public static List<string> Available()
    {
        var list = new List<string> { "en" };
        try
        {
            if (Directory.Exists(LangDir))
                foreach (var f in Directory.GetFiles(LangDir, "*.json"))
                {
                    var c = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    if (!list.Contains(c)) list.Add(c);
                }
        }
        catch { }
        return list;
    }
}
