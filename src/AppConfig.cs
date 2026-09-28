using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PZLauncher;

internal sealed class AppConfig
{
    public string GamePath { get; set; } = string.Empty;
    public string MagpieExe { get; set; } = string.Empty;
    public bool Vk { get; set; } = true;
    public string Extra { get; set; } = string.Empty;
    public string ManualText { get; set; } = string.Empty;
    public bool SafeMode { get; set; }
    public int XmsGb { get; set; } = 3;
    public int XmxGb { get; set; } = 3;
    public List<AgentEntry> Agents { get; set; } = new();
    public string JavamodDir { get; set; } = string.Empty;
    public string TemplateMod { get; set; } = string.Empty;
    public string Lang { get; set; } = "en";
    public Dictionary<string, LaunchProfile> Profiles { get; set; } = new();

    public static string ConfigPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PZLauncher.settings.json");
    public static AppConfig Load()
    {
        var cfg = new AppConfig();
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                cfg = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                if (cfg.Agents.Count == 0)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        string jar = string.Empty;
                        bool en = false;
                        if (root.TryGetProperty("AgentJar", out var j)) jar = j.GetString() ?? string.Empty;
                        if (root.TryGetProperty("AgentEnabled", out var e) &&
                            (e.ValueKind == JsonValueKind.True || e.ValueKind == JsonValueKind.False))
                            en = e.GetBoolean();
                        if (!string.IsNullOrEmpty(jar))
                            cfg.Agents.Add(new AgentEntry { Enabled = en, JarPath = jar, Gate = "strict" });
                    }
                    catch { }
                }
            }
        }
        catch { }
        return cfg;
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }
}

internal sealed class LaunchProfile
{
    public bool Vk { get; set; } = true;
    public string Extra { get; set; } = string.Empty;
    public string ManualText { get; set; } = string.Empty;
    public bool SafeMode { get; set; }
    public int XmsGb { get; set; } = 3;
    public int XmxGb { get; set; } = 3;
    public List<AgentEntry> Agents { get; set; } = new();
}

internal sealed class AgentEntry
{
    public bool Enabled { get; set; } = true;
    public string JarPath { get; set; } = string.Empty;
    public string Gate { get; set; } = "lenient";
    public string Sha256 { get; set; } = string.Empty;
    public string ApprovedSha { get; set; } = string.Empty;
    public string PendingSha { get; set; } = string.Empty;
}
