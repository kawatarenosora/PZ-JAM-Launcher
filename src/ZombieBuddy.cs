using System;
using System.IO;

namespace PZLauncher;

/// <summary>
/// ★ZombieBuddy (Steam Workshop 3619862853) の検出 / 導入 / 除去。
///
/// <para>ランチャーは jre64\bin\java.exe を直接起動するため、公式 installer が
///   パッチする ProjectZomboid64.json / Steam 起動オプション / ProjectZomboid64.bat
///   はランチャー経由の起動には効かない。ランチャーに必要なのは game dir 直下の
///   <c>zbNative.dll</c> + <c>ZombieBuddy.jar</c> の配置だけで、引数は
///   <c>-agentlib:zbNative</c> を VM オプションとして直接渡す。</para>
///
/// <para>注意: 公式 doc の「<c>--</c> 必須」は PZ 小型ランチャーやシェル側の
///   区切り子であって、java.exe 直起動では渡してはならない (同梱 JRE は
///   <c>Unrecognized option: --</c> で終了する。実測済み)。
///   zbNative.c は <c>--</c> を見ない — tail (= 以降) をそのまま
///   <c>ZombieBuddy.jar=tail</c> に連結して instrument.dll へ渡すだけ。</para>
/// </summary>
internal static class ZombieBuddy
{
    public const string WorkshopId = "3619862853";
    public const string DllName = "zbNative.dll";
    public const string JarName = "ZombieBuddy.jar";

    public sealed class State
    {
        /// <summary>gameDir\zbNative.dll がある。</summary>
        public bool Dll;
        /// <summary>gameDir\ZombieBuddy.jar がある。</summary>
        public bool Jar;
        /// <summary>gameDir\ZombieBuddy.jar.new がある (ZB 自身の更新予約ファイル)。</summary>
        public bool JarNew;
        /// <summary>ProjectZomboid64.json に -agentlib:zbNative が書き込まれている
        ///   (ランチャー外の Normal Launch でも ZB が有効)。</summary>
        public bool JsonPatched;
        /// <summary>ProjectZomboid64.bat の _JAVA_OPTIONS 行に -agentlib:zbNative が
        ///   書き込まれている (Alternate Launch でも ZB が有効)。</summary>
        public bool BatPatched;
        /// <summary>導入ソース (Workshop content または mods 配下の libs)。空は未発見。</summary>
        public string SourceLibs = string.Empty;

        /// <summary>dll + jar が揃っている (起動可能)。</summary>
        public bool Installed => Dll && Jar;
        /// <summary>どちらか片方でもある (片方欠けは要修復)。</summary>
        public bool Present => Dll || Jar;
    }

    public static State Detect(string gameDir)
    {
        var st = new State();
        try
        {
            st.Dll = File.Exists(Path.Combine(gameDir, DllName));
            st.Jar = File.Exists(Path.Combine(gameDir, JarName));
            st.JarNew = File.Exists(Path.Combine(gameDir, JarName + ".new"));
            var json = Path.Combine(gameDir, "ProjectZomboid64.json");
            if (File.Exists(json))
                st.JsonPatched = File.ReadAllText(json).IndexOf("-agentlib:zbNative", StringComparison.OrdinalIgnoreCase) >= 0;
            var bat = Path.Combine(gameDir, "ProjectZomboid64.bat");
            if (File.Exists(bat))
                st.BatPatched = File.ReadAllText(bat).IndexOf("-agentlib:zbNative", StringComparison.OrdinalIgnoreCase) >= 0;
            st.SourceLibs = FindSourceLibsDir();
        }
        catch { }
        return st;
    }

    /// <summary>
    /// ★導入ソース (zbNative.dll を含む libs ディレクトリ) を探す。
    ///   1) Steam Workshop content、2) 手動インストール (%USERPROFILE%\Zomboid\mods\ZombieBuddy)。
    /// </summary>
    public static string FindSourceLibsDir()
    {
        try
        {
            foreach (var apps in SteamFinder.GetLibrarySteamappsDirs())
            {
                var wsRoot = Path.Combine(apps, "workshop", "content", SteamFinder.PZAppId, WorkshopId);
                // 実際の Workshop 構造は mods\ZombieBuddy\libs 配下。
                var libs = Path.Combine(wsRoot, "mods", "ZombieBuddy", "libs");
                if (File.Exists(Path.Combine(libs, DllName))) return libs;
                // 旧想定 (Workshop ルート直下の libs) も後方互換で試す。
                libs = Path.Combine(wsRoot, "libs");
                if (File.Exists(Path.Combine(libs, DllName))) return libs;
            }
        }
        catch { }
        try
        {
            var mods = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Zomboid", "mods", "ZombieBuddy", "libs");
            if (File.Exists(Path.Combine(mods, DllName))) return mods;
        }
        catch { }
        return string.Empty;
    }

    /// <summary>
    /// ★libs から game dir へ dll + jar をコピーする (既存は上書き = 再導入/更新兼用)。
    ///   ゲーム稼働中はファイルがロックされるので呼ばないこと。
    ///   戻り値はエラーメッセージ (成功時は空)。
    /// </summary>
    public static string Install(string gameDir, string libsDir)
    {
        try
        {
            foreach (var name in new[] { DllName, JarName })
            {
                var src = Path.Combine(libsDir, name);
                if (!File.Exists(src)) return "source missing: " + src;
                File.Copy(src, Path.Combine(gameDir, name), overwrite: true);
            }
            return string.Empty;
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>
    /// ★game dir から ZB 関連ファイル (dll / jar / jar.new) を削除する。
    ///   これはファイル配置の取り消しのみで、公式 installer が json / Steam 起動
    ///   オプション / .bat に書き込んだパッチは消えない (呼び出し側で警告すること)。
    ///   戻り値はエラーメッセージ (成功時は空)。
    /// </summary>
    public static string Uninstall(string gameDir)
    {
        try
        {
            foreach (var name in new[] { DllName, JarName, JarName + ".new" })
            {
                var p = Path.Combine(gameDir, name);
                if (File.Exists(p)) File.Delete(p);
            }
            return string.Empty;
        }
        catch (Exception ex) { return ex.Message; }
    }
}
