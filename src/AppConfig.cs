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
    /// <summary>
    /// ★描画モード。"zink" (GL→Vulkan) / "d3d12" (GL→D3D12 (Dozen)) / "off" (ネイティブGL)。
    ///   空は旧 Vk bool 扱いで、Load 時に補完する。
    /// </summary>
    public string Render { get; set; } = string.Empty;
    /// <summary>
    /// ★ZombieBuddy。"on" (起動時に -agentlib:zbNative を付与) / "off" (付けない)。
    ///   空は旧動作 (zbNative.dll+jar が揃っていれば on)。
    /// </summary>
    public string Zb { get; set; } = string.Empty;
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
                // ★旧 Vk bool のみの設定を 3値モードへ補完する。
                if (string.IsNullOrEmpty(cfg.Render))
                    cfg.Render = cfg.Vk ? "zink" : "off";
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
    /// <summary>★"zink" | "d3d12" | "off"。空は旧 Vk bool 扱い (適用時に補完)。</summary>
    public string Render { get; set; } = string.Empty;
    /// <summary>★"on" | "off"。空は旧動作 (ZB ファイル在席時 on)。</summary>
    public string Zb { get; set; } = string.Empty;
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

    /// <summary>
    /// agent に渡す引数。<b>リポジトリの既知の罠に直結する。</b>
    ///
    /// <p>JVM は <c>-javaagent:jar=引数</c> の形式を取る。<c>=</c> の後ろは
    /// <c>&lt;args&gt;</c> であって JVM 引数ではないので、agent 側の
    /// <c>System.getProperty</c> には届かない。agent 側が args 文字列も
    /// なっているのはそのためで、ここに書かなければ agent の
    /// <c>-Dxxx=yyy</c> が全部 no-op になる。
    ///
    /// <p>空文字なら従来通り <c>-javaagent:jar</c> だけで起動する。
    /// </summary>
    public string Args { get; set; } = string.Empty;
}
