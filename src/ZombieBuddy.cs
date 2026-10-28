using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PZLauncher;

/// <summary>
/// ★ZombieBuddy (Steam Workshop 3619862853 / ベータ 3812624292) の検出 / 導入 / 除去。
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
    /// <summary>★ベータ版 Workshop (検証用。リリース版と同時購読があり得る)。</summary>
    public const string WorkshopIdBeta = "3812624292";
    public const string DllName = "zbNative.dll";
    public const string JarName = "ZombieBuddy.jar";

    /// <summary>★導入ソースの種別。設定 (AppConfig.ZbSource) にも同じ値を記録する。</summary>
    public const string KindRelease = "release";
    public const string KindBeta = "beta";
    public const string KindManual = "manual";

    /// <summary>★導入ソース候補 1 件分。</summary>
    public sealed class Source
    {
        /// <summary>"release" | "beta" | "manual"。</summary>
        public string Kind;
        /// <summary>zbNative.dll / ZombieBuddy.jar を含む libs ディレクトリ。</summary>
        public string Libs;
    }

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
        /// <summary>導入ソース候補の最初の libs。空は未発見 (表示用の互換フィールド)。</summary>
        public string SourceLibs = string.Empty;
        /// <summary>★SHA 比較に使ったソース種別。記録が空だった場合は一致した
        ///   ソースから推定した値入りで返る (呼び出し側で保存する)。</summary>
        public string ResolvedKind = string.Empty;
        /// <summary>game dir の jar と「記録された導入ソース」の jar の SHA-256 が
        ///   不一致 (Workshop 更新未反映または game dir 側が改変/破損)。
        ///   記録ソースが見つからない (購読解除等) 場合は警告しない。</summary>
        public bool UpdateAvailable;

        /// <summary>dll + jar が揃っている (起動可能)。</summary>
        public bool Installed => Dll && Jar;
        /// <summary>どちらか片方でもある (片方欠けは要修復)。</summary>
        public bool Present => Dll || Jar;
    }

    public static State Detect(string gameDir, string recordedKind)
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
            var sources = FindSources();
            st.SourceLibs = sources.Count > 0 ? sources[0].Libs : string.Empty;
            // ★SHA チェックは「記録された導入ソース」の jar だけを対象にする。
            //   リリース/ベータ両方を購読していると jar の内容が違うので、
            //   非対象ソースと比較すると恒常的な偽不一致になる。記録ソースが
            //   見つからない (購読解除等) 場合は警告しない。
            //   .jar.new がある場合は zbNative が次回起動時に置換するので警告しない。
            if (st.Dll && st.Jar && !st.JarNew)
            {
                Source src = null;
                if (!string.IsNullOrEmpty(recordedKind))
                    src = sources.Find(s => s.Kind == recordedKind);
                if (src == null)
                {
                    // ★記録なし (旧設定からの移行) → game dir の jar と一致する
                    //   ソースを採用する。一致がなければ先頭を警告対象にする。
                    var gameSha = Javamod.Sha256(Path.Combine(gameDir, JarName));
                    foreach (var s in sources)
                    {
                        var sj = Path.Combine(s.Libs, JarName);
                        if (File.Exists(sj) && Javamod.Sha256(sj) == gameSha) { src = s; break; }
                    }
                    src ??= sources.Count > 0 ? sources[0] : null;
                }
                if (src != null)
                {
                    st.ResolvedKind = src.Kind;
                    var srcJar = Path.Combine(src.Libs, JarName);
                    if (File.Exists(srcJar))
                        st.UpdateAvailable = Javamod.Sha256(Path.Combine(gameDir, JarName)) != Javamod.Sha256(srcJar);
                }
            }
        }
        catch { }
        return st;
    }

    /// <summary>
    /// ★導入ソース候補 (zbNative.dll を含む libs ディレクトリ) を全て探す。
    ///   種別は 1) リリース版 Workshop <see cref="WorkshopId"/>、
    ///   2) ベータ版 Workshop <see cref="WorkshopIdBeta"/>、
    ///   3) 手動インストール (%USERPROFILE%\Zomboid\mods\ZombieBuddy)。
    ///   同一種別は最初に見つかった Steam ライブラリのものだけを返す。
    ///   実際の Workshop 構造は mods\ZombieBuddy\libs 配下 (ルート直下 libs も
    ///   後方互換で試す)。
    /// </summary>
    public static List<Source> FindSources()
    {
        var list = new List<Source>();
        var seen = new HashSet<string>();
        void Add(string kind, string libs)
        {
            if (seen.Contains(kind)) return;
            if (File.Exists(Path.Combine(libs, DllName)))
            {
                list.Add(new Source { Kind = kind, Libs = libs });
                seen.Add(kind);
            }
        }
        try
        {
            foreach (var apps in SteamFinder.GetLibrarySteamappsDirs())
            {
                var ws = Path.Combine(apps, "workshop", "content", SteamFinder.PZAppId);
                Add(KindRelease, Path.Combine(ws, WorkshopId, "mods", "ZombieBuddy", "libs"));
                Add(KindRelease, Path.Combine(ws, WorkshopId, "libs"));
                Add(KindBeta, Path.Combine(ws, WorkshopIdBeta, "mods", "ZombieBuddy", "libs"));
                Add(KindBeta, Path.Combine(ws, WorkshopIdBeta, "libs"));
            }
        }
        catch { }
        try
        {
            var mods = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Zomboid", "mods", "ZombieBuddy", "libs");
            Add(KindManual, mods);
        }
        catch { }
        return list;
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
    /// ★game dir から ZB 関連ファイル (dll / jar / jar.new / jar.bak) を削除する。
    ///   .jar.bak は ZB の SelfUpdater が置換成功時に残すバックアップ。
    ///   これはファイル配置の取り消しのみで、公式 installer が json / Steam 起動
    ///   オプション / .bat に書き込んだパッチは消えない (呼び出し側で警告すること)。
    ///   戻り値はエラーメッセージ (成功時は空)。
    /// </summary>
    public static string Uninstall(string gameDir)
    {
        try
        {
            foreach (var name in new[] { DllName, JarName, JarName + ".new", JarName + ".bak" })
            {
                var p = Path.Combine(gameDir, name);
                if (File.Exists(p)) File.Delete(p);
            }
            return string.Empty;
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>
    /// ★クライアント側の有効 mod リストから ZombieBuddy を取り除く。
    ///
    /// <para>ZB を有効化したまま ZB ファイルだけ除去すると、ゲーム起動時に
    ///   エラーで落ちる (ZB 側の既知問題) ための暫定対策。対象は
    ///   1) <c>%USERPROFILE%\Zomboid\Lua\pz_modlist_settings.cfg</c> の全セクション
    ///     (<c>key:a;b;c;</c> 形式の行から ZombieBuddy トークンを除去)、
    ///   2) <c>%USERPROFILE%\Zomboid\Saves\*\*\mods.txt</c> の <c>mod = ZombieBuddy,</c> 行。
    ///   Server\servertest.ini (専用サーバー / ホスト用) は意図的に触らない。
    ///   再導入時に自動では戻さない (ゲーム内 mod マネージャで手動で有効化)。</para>
    ///
    /// <para>戻り値は変更したファイルのフルパス (変更がなければ空)。</para>
    /// </summary>
    public static List<string> RemoveFromModLists()
    {
        var changed = new List<string>();
        try
        {
            var zomboid = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Zomboid");
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            // 1) mod リスト プリセット (pz_modlist_settings.cfg)。
            var cfg = Path.Combine(zomboid, "Lua", "pz_modlist_settings.cfg");
            if (File.Exists(cfg))
            {
                var lines = File.ReadAllLines(cfg);
                bool mod = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].IndexOf("ZombieBuddy", StringComparison.Ordinal) < 0) continue;
                    var c = lines[i].IndexOf(':');
                    if (c < 0) continue;
                    // 末尾の ';' に対応する空要素もそのまま残す (形式維持)。
                    var parts = lines[i].Substring(c + 1).Split(';');
                    var kept = new List<string>(parts.Length);
                    foreach (var p in parts) if (p != "ZombieBuddy") kept.Add(p);
                    lines[i] = lines[i].Substring(0, c + 1) + string.Join(";", kept);
                    mod = true;
                }
                if (mod) { File.WriteAllLines(cfg, lines, utf8); changed.Add(cfg); }
            }
            // 2) セーブ毎の mods.txt。
            var saves = Path.Combine(zomboid, "Saves");
            if (Directory.Exists(saves))
            {
                foreach (var mt in Directory.EnumerateFiles(saves, "mods.txt", SearchOption.AllDirectories))
                {
                    try
                    {
                        var lines = File.ReadAllLines(mt);
                        var kept = new List<string>(lines.Length);
                        bool mod = false;
                        foreach (var l in lines)
                        {
                            if (IsZbModLine(l)) { mod = true; continue; }
                            kept.Add(l);
                        }
                        if (mod) { File.WriteAllLines(mt, kept, utf8); changed.Add(mt); }
                    }
                    catch { }
                }
            }
        }
        catch { }
        return changed;
    }

    /// <summary>★mods.txt の <c>mod = ZombieBuddy,</c> 行判定 (空白揺れを吸収)。</summary>
    private static bool IsZbModLine(string line)
    {
        var t = (line ?? string.Empty).Trim();
        if (!t.StartsWith("mod", StringComparison.Ordinal)) return false;
        t = t.Substring(3).TrimStart();
        if (t.StartsWith("=")) t = t.Substring(1).Trim();
        if (t.EndsWith(",")) t = t.Substring(0, t.Length - 1).Trim();
        return t == "ZombieBuddy";
    }
}
