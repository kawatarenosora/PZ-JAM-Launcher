using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PZLauncher;

internal sealed class CheckResult
{
    public bool Ok { get; set; }
    public List<string> Messages { get; set; } = new();
}

internal static class Preflight
{
    /// <summary>
    /// ★Mesa 導入チェック。<paramref name="zink"/> (VK (Zink)) のときだけ
    ///   Vulkan ランタイム (vulkan-1.dll) も要求する。D3D12 (Dozen) モードは
    ///   Vulkan 不要で、Mesa 側の DLL が揃っていればよい。
    /// </summary>
    public static CheckResult CheckVk(string gameDir, bool zink)
    {
        var r = new CheckResult { Ok = true };
        var tag = zink ? "[VK]" : "[D3D12]";
        var jb = Path.Combine(gameDir, "jre64", "bin");
        void Need(string p, string label)
        {
            if (!File.Exists(p)) { r.Ok = false; r.Messages.Add(tag + " " + label + L.T("check.vk.missing", " missing: ") + p); }
        }
        Need(Path.Combine(jb, "opengl32.dll"), "opengl32.dll(jre64)");
        Need(Path.Combine(jb, "libgallium_wgl.dll"), "libgallium_wgl.dll(jre64)");
        Need(Path.Combine(gameDir, "opengl32.dll"), "opengl32.dll(root)");
        if (zink) Need(Path.Combine(Environment.SystemDirectory, "vulkan-1.dll"), "vulkan-1.dll");
        if (r.Ok) r.Messages.Add(tag + " OK");
        return r;
    }

    public static string FindMagpie(string saved)
    {
        if (!string.IsNullOrEmpty(saved) && File.Exists(saved)) return saved;
        try
        {
            var psi = new ProcessStartInfo("where.exe", "Magpie.exe")
            {
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                var text = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                var first = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrEmpty(first) && File.Exists(first.Trim())) return first.Trim();
            }
        }
        catch { }
        return string.Empty;
    }

    public static bool IsMagpieRunning()
    {
        try { return Process.GetProcessesByName("Magpie").Length > 0; }
        catch { return false; }
    }

    public static bool KillMagpie()
    {
        try
        {
            var ps = Process.GetProcessesByName("Magpie");
            if (ps.Length == 0) return true;
            foreach (var p in ps)
            {
                try { p.Kill(); p.WaitForExit(5000); } catch { return false; }
                p.Dispose();
            }
            return Process.GetProcessesByName("Magpie").Length == 0;
        }
        catch { return false; }
    }
}
