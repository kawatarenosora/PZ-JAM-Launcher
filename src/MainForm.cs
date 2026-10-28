using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualBasic;

namespace PZLauncher;

internal sealed class MainForm : Form
{
    private readonly AppConfig _config = AppConfig.Load();

    private SplitContainer _split;
    private TextBox _txtGamePath;
    private ListBox _lstProfiles;
    private string _currentProfile = string.Empty;
    private Button _btnLaunch;
    private Process _gameProc;
    private System.Windows.Forms.Timer _watch;
    private CheckBox _chkSafe;
    private Label _lblAgents;
    private Label _lblZb;
    private Button _btnZbInstall;
    private Button _btnZbUninstall;
    private ComboBox _cmbRender;
    private bool _suspendRender;
    private Label _lblVk;
    private NumericUpDown _txtXms;
    private NumericUpDown _txtXmx;
    private TextBox _txtExtra;
    private Label _lblVersion;
    private TextBox _txtLog;
    private ComboBox _cmbLang;

    public MainForm()
    {
        // ★版数はアセンブリ情報から自動で付ける。lang 側の app.title には版数を
        //   書かない (更新のたびに古い版数が残る事故をなくすため)。
        var baseName = L.T("app.title", "PZ JAM Launcher");
        var ver = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
        Text = $"{baseName} v{(ver == null ? "?" : ver.ToString(3))}";
        Size = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;
        BuildUi();
        if (string.IsNullOrEmpty(_config.GamePath))
        {
            var cands = SteamFinder.FindGameCandidates();
            if (cands.Count > 0) _config.GamePath = cands[0];
        }
        _txtGamePath.Text = _config.GamePath;
        _chkSafe.Checked = _config.SafeMode;
        RefreshAgentSummary();
        RefreshZbRow();
        _txtExtra.Text = _config.Extra;
        _txtXms.Value = ClampGb(_config.XmsGb);
        _txtXmx.Value = ClampGb(_config.XmxGb);
        RefreshVersion();
        RefreshProfiles();
        RefreshLangCombo();
        RefreshVkState();
        Log($"Language: {L.Code} ({L.Count} keys) [{L.LangDir}]");
        Shown += (_, _) => { _split.SplitterDistance = ClientSize.Width / 3; };
        _watch = new System.Windows.Forms.Timer { Interval = 2000 };
        _watch.Tick += (_, _) => RefreshLaunchButton();
        _watch.Start();
        RefreshLaunchButton();
    }

    private void Log(string s)
    {
        _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}\r\n");
    }

    private void BuildUi()
    {
        _split = new SplitContainer { Dock = DockStyle.Fill };
        Controls.Add(_split);

        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(6),
        };
        _split.Panel1.Controls.Add(left);

        left.Controls.Add(new Label { Text = L.T("left.lang", "Language"), AutoSize = true });
        _cmbLang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
        _cmbLang.SelectedIndexChanged += (_, _) => ChangeLang();
        left.Controls.Add(_cmbLang);
        left.Controls.Add(new Label
        {
            Text = L.T("left.restart.note", "Applies after restart."),
            AutoSize = true,
            MaximumSize = new Size(230, 0),
            ForeColor = Color.DarkRed,
            Font = new Font(DefaultFont.FontFamily, 10, FontStyle.Bold),
        });

        left.Controls.Add(new Label { Text = L.T("left.gamepath", "Game folder"), AutoSize = true });
        _txtGamePath = new TextBox { Width = 230 };
        left.Controls.Add(_txtGamePath);
        var rowPath = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        var btnDetect = new Button { Text = L.T("left.autodetect", "Auto-detect"), AutoSize = true };
        btnDetect.Click += (_, _) => DetectGamePath();
        var btnBrowse = new Button { Text = L.T("left.browse", "Browse..."), AutoSize = true };
        btnBrowse.Click += (_, _) => BrowseGamePath();
        rowPath.Controls.Add(btnDetect);
        rowPath.Controls.Add(btnBrowse);
        left.Controls.Add(rowPath);

        _lblVersion = new Label { Text = L.T("left.version.unknown", "Version: -"), AutoSize = true, MaximumSize = new Size(240, 0) };
        left.Controls.Add(_lblVersion);
        var btnUpdate = new Button { Text = L.T("left.updatecheck", "Check update"), AutoSize = true };
        btnUpdate.Click += async (_, _) => await CheckUpdateAsync();
        left.Controls.Add(btnUpdate);
        var btnSteam = new Button { Text = L.T("left.steam", "Open Steam"), AutoSize = true };
        btnSteam.Click += (_, _) => OpenSteam();
        left.Controls.Add(btnSteam);

        left.Controls.Add(new Label { Text = L.T("left.profiles", "Profiles"), AutoSize = true });
        _lstProfiles = new ListBox { Width = 230, Height = 150 };
        _lstProfiles.SelectedIndexChanged += (_, _) => LoadSelectedProfile();
        left.Controls.Add(_lstProfiles);
        var rowProf = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        var btnProfNew = new Button { Text = L.T("left.new", "New"), AutoSize = true };
        btnProfNew.Click += (_, _) => NewProfile();
        var btnProfRen = new Button { Text = L.T("left.rename", "Rename"), AutoSize = true };
        btnProfRen.Click += (_, _) => RenameProfile();
        var btnProfDel = new Button { Text = L.T("left.delete", "Delete"), AutoSize = true };
        btnProfDel.Click += (_, _) => DeleteProfile();
        rowProf.Controls.Add(btnProfNew);
        rowProf.Controls.Add(btnProfRen);
        rowProf.Controls.Add(btnProfDel);
        left.Controls.Add(rowProf);

        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(8),
        };
        _split.Panel2.Controls.Add(right);

        var rowVk = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        // ★3値切替: オフ (ネイティブGL) / VK (Zink→Vulkan) / D3D12 (GL over D3D12)。
        _cmbRender = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        _cmbRender.Items.AddRange(new object[]
        {
            L.T("render.off", "Off"), L.T("render.zink", "VK (Zink)"), L.T("render.d3d12", "D3D12"),
        });
        _cmbRender.SelectedIndexChanged += (_, _) =>
        {
            if (_suspendRender) return;
            SetRender(_cmbRender.SelectedIndex);
        };
        _lblVk = new Label { Text = "?", AutoSize = true };
        rowVk.Controls.Add(new Label { Text = L.T("right.render", "Renderer"), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
        rowVk.Controls.Add(_cmbRender);
        rowVk.Controls.Add(_lblVk);
        right.Controls.Add(rowVk);

        var rowFsr = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        var btnMagStart = new Button { Text = L.T("right.magstart", "Start Magpie"), AutoSize = true };
        btnMagStart.Click += (_, _) => MagpieStart();
        var btnMagKill = new Button { Text = L.T("right.magkill", "Kill Magpie"), AutoSize = true };
        btnMagKill.Click += (_, _) => MagpieKill();
        rowFsr.Controls.Add(new Label { Text = L.T("right.fsr", "FSR:"), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
        rowFsr.Controls.Add(btnMagStart);
        rowFsr.Controls.Add(btnMagKill);
        right.Controls.Add(rowFsr);

        var rowAgent = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        var btnAgents = new Button { Text = L.T("right.agent.manage", "Manage Agents..."), AutoSize = true };
        btnAgents.Click += (_, _) => { using var f = new AgentsForm(_config, Log); f.ShowDialog(this); RefreshAgentSummary(); };
        _lblAgents = new Label { Text = "Agent: -", AutoSize = true };
        rowAgent.Controls.Add(new Label { Text = L.T("right.agent", "Agent:"), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
        rowAgent.Controls.Add(btnAgents);
        rowAgent.Controls.Add(_lblAgents);
        right.Controls.Add(rowAgent);

        // ★ZombieBuddy (Workshop 3619862853) 行。未導入で導入ソースがあれば
        //   Install、導入済みなら Uninstall を出す (導入/除去がそのまま
        //   有効/無効を兼ねる。トグルは置かない)。
        //   ソースもファイルも無ければ何もしない (表示のみ)。
        var rowZb = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _lblZb = new Label { Text = "?", AutoSize = true };
        _btnZbInstall = new Button { Text = L.T("zb.install", "Install"), AutoSize = true };
        _btnZbInstall.Click += (_, _) => ZbInstall();
        _btnZbUninstall = new Button { Text = L.T("zb.uninstall", "Uninstall"), AutoSize = true };
        _btnZbUninstall.Click += (_, _) => ZbUninstall();
        rowZb.Controls.Add(new Label { Text = L.T("right.zb", "ZombieBuddy:"), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
        rowZb.Controls.Add(_lblZb);
        rowZb.Controls.Add(_btnZbInstall);
        rowZb.Controls.Add(_btnZbUninstall);
        right.Controls.Add(rowZb);

        var rowMem = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        rowMem.Controls.Add(new Label { Text = "-Xms", AutoSize = true });
        _txtXms = new NumericUpDown { Width = 50, Minimum = 3, Maximum = 64, Value = ClampGb(_config.XmsGb) };
        rowMem.Controls.Add(_txtXms);
        rowMem.Controls.Add(new Label { Text = "g, -Xmx", AutoSize = true });
        _txtXmx = new NumericUpDown { Width = 50, Minimum = 3, Maximum = 64, Value = ClampGb(_config.XmxGb) };
        rowMem.Controls.Add(_txtXmx);
        rowMem.Controls.Add(new Label { Text = "g", AutoSize = true });
        right.Controls.Add(rowMem);

        var btnManual = new Button { Text = L.T("right.jvmcustom", "Custom JVM..."), AutoSize = true };
        btnManual.Click += (_, _) => OpenManualEditor();
        right.Controls.Add(btnManual);
        _chkSafe = new CheckBox { Text = L.T("right.safe", "Safe mode (disable custom content)"), AutoSize = true };
        right.Controls.Add(_chkSafe);
        right.Controls.Add(new Label { Text = L.T("right.extra", "Extra args"), AutoSize = true });
        _txtExtra = new TextBox { Width = 540 };
        right.Controls.Add(_txtExtra);

        var rowAct = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        var btnApply = new Button { Text = L.T("right.apply", "Record preset"), AutoSize = true };
        btnApply.Click += (_, _) => ApplyProfile();
        var btnLaunch = new Button { Text = L.T("right.launch", "Launch"), AutoSize = true, Width = 140, Height = 36 };
        btnLaunch.Click += (_, _) => LaunchOrKill();
        _btnLaunch = btnLaunch;
        var btnJm = new Button { Text = L.T("right.javamod", "Manage javamods..."), AutoSize = true };
        btnJm.Click += (_, _) => { using var f = new SyncForm(_config, Log); f.ShowDialog(this); };
        rowAct.Controls.Add(btnApply);
        rowAct.Controls.Add(btnLaunch);
        right.Controls.Add(rowAct);
        right.Controls.Add(new Label
        {
            Text = L.T("right.freeze.note1", "Force-kill is recommended only when frozen."),
            AutoSize = true,
            ForeColor = Color.DarkRed,
        });
        right.Controls.Add(new Label
        {
            Text = L.T("right.freeze.note2", "Killing mid-play may corrupt data if autosave fails."),
            AutoSize = true,
            MaximumSize = new Size(540, 0),
            ForeColor = Color.DarkRed,
        });

        _txtLog = new TextBox { Width = 540, Height = 140, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        right.Controls.Add(_txtLog);
    }

    private static string LangDisplay(string code) => code switch
    {
        "ja" => "日本語",
        "en" => "English",
        "zh-hans" => "简体中文",
        "ru" => "Русский",
        "pt-br" => "Português (Brasil)",
        "de" => "Deutsch",
        "es" => "Español",
        "fr" => "Français",
        _ => code,
    };

    private void RefreshLangCombo()
    {
        _cmbLang.Items.Clear();
        foreach (var c in L.Available()) _cmbLang.Items.Add(LangDisplay(c));
        var cur = L.Available().IndexOf(L.Code);
        _cmbLang.SelectedIndex = Math.Max(0, cur);
    }

    private void ChangeLang()
    {
        if (_cmbLang.SelectedIndex < 0) return;
        var avail = L.Available();
        if (_cmbLang.SelectedIndex >= avail.Count) return;
        var code = avail[_cmbLang.SelectedIndex];
        if (code == L.Code) return;
        _config.Lang = code;
        _config.Save();
        Log(L.T("log.lang.restart", "Language saved. Restart to apply."));
    }

    private string[] MesaFiles(string gameDir) => new[]
    {
        Path.Combine(gameDir, "opengl32.dll"),
        Path.Combine(gameDir, "jre64", "bin", "opengl32.dll"),
    };

    private int GetVkState()
    {
        try
        {
            var files = MesaFiles(_txtGamePath.Text.Trim());
            bool anyDll = files.Any(File.Exists);
            bool anyOff = files.Any(f => File.Exists(f + ".off"));
            if (anyDll && !anyOff) return 1;
            if (!anyDll && anyOff) return 0;
            if (!anyDll && !anyOff) return -1;
            return -1;
        }
        catch { return -1; }
    }

    private void RefreshVkState()
    {
        // ★実効モードに合わせてコンボと表示を同期。選択済み値が同じなら
        //   SelectedIndex を触らない (イベント自噴火の防止)。
        var mode = ResolvedRender();
        if (_cmbRender != null)
        {
            var idx = mode == "off" ? 0 : mode == "d3d12" ? 2 : 1;
            _suspendRender = true;
            try { if (_cmbRender.SelectedIndex != idx) _cmbRender.SelectedIndex = idx; }
            finally { _suspendRender = false; }
        }
        if (_lblVk == null) return;
        switch (mode)
        {
            case "zink":
                _lblVk.Text = L.T("render.zink", "VK (Zink)");
                _lblVk.ForeColor = Color.Green;
                break;
            case "d3d12":
                _lblVk.Text = L.T("render.d3d12", "D3D12");
                _lblVk.ForeColor = Color.Green;
                break;
            case "off":
                _lblVk.Text = L.T("render.off", "Off");
                _lblVk.ForeColor = Color.Red;
                break;
            default:
                _lblVk.Text = L.T("vk.state.unknown", "unknown");
                _lblVk.ForeColor = Color.Black;
                break;
        }
    }

    private void VkOn()
    {
        var gd = _txtGamePath.Text.Trim();
        foreach (var f in MesaFiles(gd))
        {
            try
            {
                if (File.Exists(f + ".off") && !File.Exists(f))
                    File.Move(f + ".off", f);
            }
            catch (Exception ex) { Log($"Mesa restore failed {Path.GetFileName(f)}: {ex.Message}"); }
        }
        Log(L.T("log.mesa.vkon", "VK ON: Mesa restored."));
        RefreshVkState();
    }

    private void VkOff()
    {
        var gd = _txtGamePath.Text.Trim();
        foreach (var f in MesaFiles(gd))
        {
            try
            {
                if (File.Exists(f) && !File.Exists(f + ".off"))
                    File.Move(f, f + ".off");
            }
            catch (Exception ex) { Log($"Mesa stash failed {Path.GetFileName(f)}: {ex.Message}"); }
        }
        Log(L.T("log.mesa.vkoff", "VK OFF: Mesa stashed (native GL)."));
        RefreshVkState();
    }

    /// <summary>
    /// ★実効モード。ディスク (Mesa の有無) が正。在席時だけ設定値の
    ///   d3d12 / zink を採用し、それ以外はオフ・不明になる。
    /// 返す値はそのまま GALLIUM_DRIVER の値として効く ("zink" | "d3d12")。
    /// </summary>
    private string ResolvedRender()
    {
        switch (GetVkState())
        {
            case 1: return _config.Render == "d3d12" ? "d3d12" : "zink";
            case 0: return "off";
            default: return "unknown";
        }
    }

    private void SetRender(int idx) => SetRender(idx == 2 ? "d3d12" : idx == 1 ? "zink" : "off");

    /// <summary>★モード切替。Mesa の復帰/退避 + 設定保存をひとまとめにする。</summary>
    private void SetRender(string mode)
    {
        switch (mode)
        {
            case "zink":
                VkOn();
                break;
            case "d3d12":
                // ★VkOn の復帰ログに続き、ドライバだけを明示する。
                VkOn();
                Log(L.T("log.mesa.d3d12", "D3D12: Mesa restored (GL over D3D12)."));
                break;
            default:
                mode = "off";
                VkOff();
                break;
        }
        _config.Render = mode;
        SaveSettings();
        RefreshVkState();
    }

    private Process FindPzProcess()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("ProjectZomboid64"))
                return p;
            foreach (var p in Process.GetProcessesByName("java"))
            {
                try
                {
                    var t = p.MainWindowTitle ?? string.Empty;
                    if (t.IndexOf("Zomboid", StringComparison.OrdinalIgnoreCase) >= 0) return p;
                }
                catch { p.Dispose(); }
            }
        }
        catch { }
        return null;
    }

    private bool IsGameRunning()
    {
        try
        {
            if (_gameProc != null && !_gameProc.HasExited) return true;
        }
        catch { _gameProc = null; }
        var found = FindPzProcess();
        if (found != null)
        {
            try { _gameProc?.Dispose(); } catch { }
            _gameProc = found;
            return true;
        }
        return false;
    }

    private void RefreshLaunchButton()
    {
        bool running = false;
        try { running = IsGameRunning(); } catch { }
        if (_cmbRender != null) _cmbRender.Enabled = !running;
        if (_btnZbInstall != null) _btnZbInstall.Enabled = !running;
        if (_btnZbUninstall != null) _btnZbUninstall.Enabled = !running;
        if (_btnLaunch == null) return;
        if (string.IsNullOrEmpty(_currentProfile) || !_config.Profiles.ContainsKey(_currentProfile))
        {
            _btnLaunch.Text = L.T("right.launch", "Launch");
            _btnLaunch.Enabled = false;
            return;
        }
        _btnLaunch.Text = running ? L.T("right.kill", "Kill") : L.T("right.launch", "Launch");
        _btnLaunch.Enabled = true;
    }

    private void LaunchOrKill()
    {
        try
        {
            if (IsGameRunning() && _gameProc != null)
            {
                _gameProc.Kill();
                _gameProc.WaitForExit(5000);
                Log(L.T("log.killed", "Game force-killed."));
                _gameProc = null;
                RefreshLaunchButton();
                return;
            }
        }
        catch (Exception ex) { Log(L.T("log.killfail", "Kill failed: ") + ex.Message); }
        Launch();
        RefreshLaunchButton();
    }

    private void DetectGamePath()
    {
        var cands = SteamFinder.FindGameCandidates();
        if (cands.Count == 0) { Log(L.T("log.nogame", "Game not found. Specify manually.")); return; }
        _txtGamePath.Text = cands[0];
        if (cands.Count > 1) Log(L.T("log.multigame", "Multiple found. Using first: ") + cands[0]);
        RefreshVersion();
        RefreshZbRow();
        Log(L.T("log.gamepath", "Game folder: ") + cands[0]);
    }

    private void BrowseGamePath()
    {
        using var dlg = new FolderBrowserDialog { Description = L.T("dlg.gamefolder", "Select the ProjectZomboid folder") };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            _txtGamePath.Text = dlg.SelectedPath;
            RefreshVersion();
            RefreshZbRow();
        }
    }

    private void RefreshVersion()
    {
        var (build, date) = SteamFinder.ReadAppManifest(_txtGamePath.Text.Trim());
        _lblVersion.Text = string.IsNullOrEmpty(build)
            ? L.T("left.version.unknown", "Version: -")
            : string.Format(L.T("left.version", "Version: build {0} ({1})"), build, date);
    }

    private string ResolveMagpie(bool silent)
    {
        var exe = Preflight.FindMagpie(_config.MagpieExe);
        if (string.IsNullOrEmpty(exe))
        {
            using var dlg = new OpenFileDialog { Filter = "Magpie.exe|Magpie.exe", Title = L.T("dlg.magpie", "Select Magpie.exe") };
            if (dlg.ShowDialog() != DialogResult.OK) return string.Empty;
            exe = dlg.FileName;
            _config.MagpieExe = exe;
            _config.Save();
        }
        return exe;
    }

    private void MagpieStart()
    {
        var exe = ResolveMagpie(false);
        if (string.IsNullOrEmpty(exe)) { Log(L.T("log.magpie.missing", "[FSR] Magpie not found, nothing to do.")); return; }
        if (Preflight.IsMagpieRunning()) { Log(L.T("log.magpie.running", "[FSR] Magpie is already running.")); return; }
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            System.Threading.Thread.Sleep(3000);
            Log(Preflight.IsMagpieRunning()
                ? L.T("log.magpie.started", "[FSR] Magpie started.")
                : L.T("log.magpie.startfail", "[FSR] Magpie failed to start."));
        }
        catch (Exception ex) { Log(L.T("log.magpie.startfail", "[FSR] Magpie failed to start.") + " " + ex.Message); }
    }

    private void MagpieKill()
    {
        var exe = Preflight.FindMagpie(_config.MagpieExe);
        if (string.IsNullOrEmpty(exe) && !Preflight.IsMagpieRunning())
        {
            Log(L.T("log.magpie.missing", "[FSR] Magpie not found, nothing to do."));
            return;
        }
        if (!Preflight.IsMagpieRunning()) { Log(L.T("log.magpie.notrunning", "[FSR] Magpie is not running.")); return; }
        Log(Preflight.KillMagpie()
            ? L.T("log.magpie.killed", "[FSR] Magpie killed.")
            : L.T("log.magpie.killfail", "[FSR] Failed to kill Magpie."));
    }

    private static int ClampGb(int v) => v < 3 ? 3 : (v > 64 ? 64 : v);

    private void RefreshProfiles()
    {
        _lstProfiles.Items.Clear();
        foreach (var k in _config.Profiles.Keys.OrderBy(x => x)) _lstProfiles.Items.Add(k);
    }

    private void LoadSelectedProfile()
    {
        if (_lstProfiles.SelectedItem == null) return;
        _currentProfile = _lstProfiles.SelectedItem.ToString() ?? string.Empty;
        if (!_config.Profiles.TryGetValue(_currentProfile, out var p)) return;
        // ★旧プロファイル (Render 空) は Vk bool から補完する。
        SetRender(string.IsNullOrEmpty(p.Render) ? (p.Vk ? "zink" : "off") : p.Render.Trim().ToLowerInvariant());
        // ★ZombieBuddy は導入状態がそのまま有効/無効を兼ねるため
        //   プロファイル別の設定は持たない。
        RefreshZbRow();
        _chkSafe.Checked = p.SafeMode;
        _config.Agents = p.Agents.Select(a => new AgentEntry
        {
            Enabled = a.Enabled, JarPath = a.JarPath, Gate = a.Gate, Sha256 = a.Sha256,
            ApprovedSha = a.ApprovedSha, PendingSha = a.PendingSha, Args = a.Args ?? string.Empty,
        }).ToList();
        RefreshAgentSummary();
        // ★復元直後に前回値との一致/不一致を出す。初回承認ダイアログを
        //   「前回からの変化」と区別できるようにするため。
        ReportProfileAgentStatus(p);
        _txtExtra.Text = p.Extra;
        _txtXms.Value = ClampGb(p.XmsGb);
        _txtXmx.Value = ClampGb(p.XmxGb);
        _config.ManualText = p.ManualText;
        Log(L.T("log.profile.load", "Profile loaded: ") + _currentProfile);
        RefreshLaunchButton();
    }

    private void NewProfile()
    {
        var name = Interaction.InputBox(L.T("dlg.profilename", "Profile name"), "PZ JAM Launcher", "Default");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (_config.Profiles.ContainsKey(name))
        {
            Log(L.T("log.profile.exists", "Profile already exists: ") + name);
            return;
        }
        _config.Profiles[name] = new LaunchProfile
        {
            Vk = GetVkState() == 1,
            // ★描画モードもプロファイルに記録する (unknown は off 正規化)。
            Render = GetVkState() == 1 ? (_config.Render == "d3d12" ? "d3d12" : "zink") : "off",
            SafeMode = _chkSafe.Checked,
            Extra = _txtExtra.Text.Trim(),
            ManualText = _config.ManualText,
            XmsGb = (int)_txtXms.Value,
            XmxGb = (int)_txtXmx.Value,
            Agents = _config.Agents.Select(a => new AgentEntry
            {
                Enabled = a.Enabled, JarPath = a.JarPath, Gate = a.Gate,
            ApprovedSha = a.ApprovedSha, PendingSha = a.PendingSha, Args = a.Args ?? string.Empty,
            }).ToList(),
        };
        _config.Save();
        RefreshProfiles();
        _lstProfiles.SelectedItem = name;
    }

    private void DeleteProfile()
    {
        if (_lstProfiles.SelectedItem == null) return;
        var name = _lstProfiles.SelectedItem.ToString() ?? string.Empty;
        _config.Profiles.Remove(name);
        _currentProfile = string.Empty;
        _config.Save();
        RefreshProfiles();
        RefreshLaunchButton();
    }

    private void RenameProfile()
    {
        if (_lstProfiles.SelectedItem == null) return;
        var old = _lstProfiles.SelectedItem.ToString() ?? string.Empty;
        if (!_config.Profiles.TryGetValue(old, out var p)) return;
        var name = Interaction.InputBox(L.T("dlg.profilerename", "New profile name"), "PZ JAM Launcher", old);
        if (string.IsNullOrWhiteSpace(name) || name == old) return;
        if (_config.Profiles.ContainsKey(name))
        {
            Log(L.T("log.profile.exists", "Profile already exists: ") + name);
            return;
        }
        _config.Profiles.Remove(old);
        _config.Profiles[name] = p;
        _currentProfile = name;
        _config.Save();
        RefreshProfiles();
        _lstProfiles.SelectedItem = name;
        Log(L.T("log.profile.renamed", "Profile renamed: ") + $"{old} -> {name}");
    }

    private void ApplyProfile()
    {
        if (string.IsNullOrEmpty(_currentProfile))
        {
            Log(L.T("log.profile.noselect", "No profile selected."));
            return;
        }
        _config.Profiles[_currentProfile] = new LaunchProfile
        {
            Vk = GetVkState() == 1,
            // ★描画モードもプロファイルに記録する (unknown は off 正規化)。
            Render = GetVkState() == 1 ? (_config.Render == "d3d12" ? "d3d12" : "zink") : "off",
            SafeMode = _chkSafe.Checked,
            Extra = _txtExtra.Text.Trim(),
            ManualText = _config.ManualText,
            XmsGb = (int)_txtXms.Value,
            XmxGb = (int)_txtXmx.Value,
            Agents = _config.Agents.Select(a => new AgentEntry
            {
                Enabled = a.Enabled, JarPath = a.JarPath, Gate = a.Gate,
            ApprovedSha = a.ApprovedSha, PendingSha = a.PendingSha, Args = a.Args ?? string.Empty,
            }).ToList(),
        };
        _config.Save();
        Log(L.T("log.profile.saved", "Recorded: ") + _currentProfile);
    }

    private void RefreshAgentSummary()
    {
        try
        {
            var en = _config.Agents.Count(a => a.Enabled);
            _lblAgents.Text = string.Format(
                L.T("right.agents.summary", "{0} agents, {1} enabled"),
                _config.Agents.Count, en);
        }
        catch { _lblAgents.Text = "-"; }
    }

    /// <summary>★ZombieBuddy 検出 (記録ソース付き) + 記録の自己修復。
    ///   旧設定で記録が空の場合、Detect が jar の SHA 一致からソースを
    ///   推定して ResolvedKind に入れて返すので、ここで保存しておく。</summary>
    private ZombieBuddy.State ZbDetect(string gd)
    {
        var st = ZombieBuddy.Detect(gd, _config.ZbSource);
        if (string.IsNullOrEmpty(_config.ZbSource) && !string.IsNullOrEmpty(st.ResolvedKind))
        {
            _config.ZbSource = st.ResolvedKind;
            _config.Save();
        }
        return st;
    }

    /// <summary>★ZombieBuddy 行の表示とボタン出し分け。
    ///   導入/除去がそのまま有効/無効を兼ねるためトグルは置かない。
    ///   導入済みで記録ソースの jar と SHA が不一致なら「更新あり」(橙) を出す。</summary>
    private void RefreshZbRow()
    {
        if (_lblZb == null || _btnZbInstall == null) return;
        try
        {
            var gd = _txtGamePath.Text.Trim();
            var st = ZbDetect(gd);
            bool running = false;
            try { running = IsGameRunning(); } catch { }
            _btnZbInstall.Visible = false;
            _btnZbUninstall.Visible = false;
            if (st.Installed)
            {
                if (st.UpdateAvailable)
                {
                    // ★Workshop ソースの方が新しい (または game dir 側が改変された)。
                    //   Install ボタンで再コピー = 更新になる。
                    _lblZb.Text = L.T("zb.update", "update available");
                    _lblZb.ForeColor = Color.DarkOrange;
                    _btnZbInstall.Visible = true;
                }
                else
                {
                    _lblZb.Text = L.T("zb.installed", "installed");
                    _lblZb.ForeColor = Color.Green;
                }
                _btnZbUninstall.Visible = true;
            }
            else if (st.Present)
            {
                // ★片方欠け。修復 (再導入) と除去の両方を出す。
                _lblZb.Text = L.T("zb.broken", "incomplete");
                _lblZb.ForeColor = Color.DarkOrange;
                _btnZbInstall.Visible = true;
                _btnZbUninstall.Visible = true;
            }
            else if (!string.IsNullOrEmpty(st.SourceLibs))
            {
                // ★未導入だが Workshop content / mods にソースがある → 代行導入を出す。
                _lblZb.Text = L.T("zb.notinstalled", "not installed");
                _lblZb.ForeColor = Color.Black;
                _btnZbInstall.Visible = true;
            }
            else
            {
                // ★ソース無し。何もしない (表示のみ)。
                _lblZb.Text = L.T("zb.notinstalled", "not installed");
                _lblZb.ForeColor = Color.Black;
            }
            _btnZbInstall.Enabled = !running;
            _btnZbUninstall.Enabled = !running;
        }
        catch { _lblZb.Text = "?"; }
    }

    private void ZbInstall()
    {
        var gd = _txtGamePath.Text.Trim();
        if (string.IsNullOrEmpty(gd) || !Directory.Exists(gd)) { Log(L.T("log.nogame", "Game not found. Specify manually.")); return; }
        var sources = ZombieBuddy.FindSources();
        if (sources.Count == 0) { Log(L.T("log.zb.nosource", "ZombieBuddy install source not found (subscribe to the Workshop mod, or extract it to Zomboid\\mods\\ZombieBuddy).")); return; }
        var src = sources[0];
        if (sources.Count > 1)
        {
            // ★リリース/ベータ/手動が複数ある → 選ばせ、選択を記録する。
            var kind = ChooseZbSource(sources);
            if (kind == null) return;
            src = sources.Find(s => s.Kind == kind);
        }
        var err = ZombieBuddy.Install(gd, src.Libs);
        if (!string.IsNullOrEmpty(err)) { Log(L.T("log.zb.installfail", "ZombieBuddy install failed: ") + err); return; }
        // ★導入ソースを記録。起動時の SHA 更新チェックはこのソースだけが対象。
        _config.ZbSource = src.Kind;
        _config.Save();
        Log(L.T("log.zb.installed", "ZombieBuddy installed from: ") + src.Libs);
        RefreshZbRow();
    }

    /// <summary>★導入ソース選択ダイアログ (候補が複数の時のみ出す)。
    ///   戻り値は選択された種別、キャンセルは null。</summary>
    private string ChooseZbSource(List<ZombieBuddy.Source> sources)
    {
        string SourceLabel(ZombieBuddy.Source s) => s.Kind switch
        {
            ZombieBuddy.KindRelease => L.T("zb.source.release", "Release (Workshop 3619862853)"),
            ZombieBuddy.KindBeta => L.T("zb.source.beta", "Beta (Workshop 3812624292)"),
            _ => L.T("zb.source.manual", "Manual (Zomboid\\mods\\ZombieBuddy)"),
        };
        using var dlg = new Form
        {
            Text = "ZombieBuddy",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(440, 40 + sources.Count * 28 + 42),
        };
        var top = 12;
        var lbl = new Label
        {
            Text = L.T("zb.source.msg", "Multiple ZombieBuddy sources found. Which one do you want to install?"),
            Location = new Point(12, top),
            AutoSize = true,
            MaximumSize = new Size(416, 0),
        };
        dlg.Controls.Add(lbl);
        top += lbl.PreferredHeight + 8;
        var radios = new List<RadioButton>();
        foreach (var s in sources)
        {
            var rb = new RadioButton
            {
                Text = SourceLabel(s),
                Location = new Point(16, top),
                AutoSize = true,
                Checked = radios.Count == 0,
            };
            radios.Add(rb);
            dlg.Controls.Add(rb);
            top += rb.GetPreferredSize(Size.Empty).Height + 4;
        }
        top += 4;
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(440 - 190, top), Size = new Size(84, 28) };
        var cancel = new Button { Text = L.T("dlg.cancel", "Cancel"), DialogResult = DialogResult.Cancel, Location = new Point(440 - 96, top), Size = new Size(84, 28) };
        dlg.Controls.Add(ok);
        dlg.Controls.Add(cancel);
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        if (dlg.ShowDialog(this) != DialogResult.OK) return null;
        foreach (var rb in radios)
            if (rb.Checked)
                return sources[radios.IndexOf(rb)].Kind;
        return null;
    }

    private void ZbUninstall()
    {
        var gd = _txtGamePath.Text.Trim();
        var st = ZbDetect(gd);
        // ★json / bat にパッチが残っていると、ランチャー外の起動で
        //   zbNative.dll 探しに失敗して JVM が即死する。先に警告する。
        var ext = new List<string>();
        if (st.JsonPatched) ext.Add("ProjectZomboid64.json");
        if (st.BatPatched) ext.Add("ProjectZomboid64.bat");
        var msg = L.T("zb.uninstall.confirm", "Remove ZombieBuddy files from the game folder?");
        if (ext.Count > 0)
            msg += "\n\n" + string.Format(L.T("zb.uninstall.extwarn",
                "Note: -agentlib:zbNative remains in {0}. Launching outside this launcher will fail until it is removed (use the official installer's uninstall)."),
                string.Join(", ", ext));
        var r = MessageBox.Show(this, msg, "ZombieBuddy", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (r != DialogResult.Yes) return;
        var err = ZombieBuddy.Uninstall(gd);
        if (!string.IsNullOrEmpty(err)) { Log(L.T("log.zb.uninstallfail", "ZombieBuddy uninstall failed: ") + err); return; }
        Log(L.T("log.zb.uninstalled", "ZombieBuddy files removed."));
        // ★暫定対策: ZB が有効 mod リストに残っていると、ZB 無しの起動で
        //   ゲームがエラーで落ちる (ZB 側の既知問題)。クライアント側の
        //   mod リストから ZB を取り除いておく。再導入時に自動では
        //   戻さない (ゲーム内の mod マネージャで手動で有効化する)。
        var ml = ZombieBuddy.RemoveFromModLists();
        if (ml.Count > 0)
            Log(string.Format(L.T("log.zb.modlist", "Removed ZombieBuddy from mod list(s): {0}"),
                string.Join(", ", ml)));
        if (ext.Count > 0)
            Log(string.Format(L.T("zb.uninstall.extwarn",
                "Note: -agentlib:zbNative remains in {0}. Launching outside this launcher will fail until it is removed (use the official installer's uninstall)."),
                string.Join(", ", ext)));
        RefreshZbRow();
    }

    private static string ResolveAgentAbs(string gameDir, string raw)
    {
        try
        {
            return Path.IsPathRooted(raw ?? string.Empty)
                ? Path.GetFullPath(raw)
                : Path.GetFullPath(Path.Combine(gameDir, raw ?? string.Empty));
        }
        catch { return raw ?? string.Empty; }
    }

    private static void MigrateAgentSha(AppConfig cfg)
    {
        try
        {
            foreach (var a in cfg.Agents)
                if (string.IsNullOrEmpty(a.ApprovedSha) && !string.IsNullOrEmpty(a.Sha256))
                    a.ApprovedSha = a.Sha256;
        }
        catch { }
    }

    private static string NormalizeAgentPath(string p)
    {
        try
        {
            var t = p.Trim().Trim('"');
            if (t.StartsWith("-javaagent:", StringComparison.OrdinalIgnoreCase))
                t = t.Substring("-javaagent:".Length);
            // ★-javaagent:jar=引数 の「=引数」を落とす。落とさないと
            //   Path.GetFullPath が "jar=-Dfoo=bar" をファイル名として扱い、
            //   重複判定が必ず外れて同じ agent が二重に起動される。
            var eq = t.IndexOf('=');
            if (eq >= 0) t = t.Substring(0, eq);
            t = t.Trim().Trim('"');
            return Path.GetFullPath(t).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
        }
        catch { return p.Trim().ToLowerInvariant(); }
    }

    /// <summary>
    /// ★起動フローで確定した承認状態 (ApprovedSha/PendingSha) を、選択中の
    ///   プロファイルの退避に書き戻す。これが無いと LoadSelectedProfile が
    ///   古い退避値で _config.Agents を差し替え、承認済みのはずの jar が
    ///   「新規」扱いに戻る。退避は相対パスのまま残ることがあるので、
    ///   一致判定は GamePath 基準の絶対パス正規化で行う。
    /// </summary>
    private void SyncAgentShaToProfile()
    {
        try
        {
            if (string.IsNullOrEmpty(_currentProfile) ||
                !_config.Profiles.TryGetValue(_currentProfile, out var p)) return;
            var gameDir = _txtGamePath.Text.Trim();
            foreach (var pa in p.Agents)
            {
                var key = NormalizeAgentPath(ResolveAgentAbs(gameDir, pa.JarPath));
                var now = _config.Agents.FirstOrDefault(
                    b => NormalizeAgentPath(ResolveAgentAbs(gameDir, b.JarPath)) == key);
                if (now == null) continue;
                pa.ApprovedSha = now.ApprovedSha;
                pa.PendingSha = now.PendingSha;
            }
        }
        catch { }
    }

    /// <summary>
    /// ★プロファイル復元直後に、保存されている前回値と現在の jar の
    ///   一致/不一致をログに出す。起動時の承認フローと同じ判定規準で、
    ///   通常の一致は静か、変化と未承認だけが出る。
    /// </summary>
    private void ReportProfileAgentStatus(LaunchProfile p)
    {
        try
        {
            var gameDir = _txtGamePath.Text.Trim();
            foreach (var a in p.Agents.Where(x => x.Enabled))
            {
                var tag = Path.GetFileName(a.JarPath);
                var cur = Javamod.Sha256(ResolveAgentAbs(gameDir, a.JarPath));
                if (string.IsNullOrEmpty(cur))
                {
                    Log(string.Format(L.T("log.profile.agent.missing", "Agent file not found: {0}."), tag));
                    continue;
                }
                var shortCur = cur.Length >= 8 ? cur.Substring(0, 8) : "?";
                if (string.IsNullOrEmpty(a.ApprovedSha) && string.IsNullOrEmpty(a.PendingSha))
                {
                    Log(string.Format(L.T("log.profile.agent.new", "Agent not approved yet (asked at launch): {0}."), tag));
                    continue;
                }
                if (cur == a.ApprovedSha)
                {
                    var shortApr = a.ApprovedSha.Length >= 8 ? a.ApprovedSha.Substring(0, 8) : "?";
                    Log(string.Format(L.T("log.profile.agent.match", "Agent matches approval: {0} (sha {1})."), tag, shortApr));
                    continue;
                }
                if (!string.IsNullOrEmpty(a.PendingSha) && cur == a.PendingSha)
                {
                    Log(string.Format(L.T("log.profile.agent.pending", "Agent matches recorded update (asked at launch): {0}."), tag));
                    continue;
                }
                var shortOld = a.ApprovedSha.Length >= 8 ? a.ApprovedSha.Substring(0, 8) : "?";
                Log(string.Format(L.T("log.profile.agent.changed", "Agent changed since approval: {0} (sha {1} -> {2})."),
                    tag, shortOld, shortCur));
            }
        }
        catch { }
    }

    private void OpenManualEditor()
    {
        using var dlg = new Form
        {
            Text = L.T("dlg.jvmcustom", "Custom JVM"),
            Size = new Size(560, 420),
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
        };
        var txt = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10),
            Text = _config.ManualText,
        };
        var btn = new Button { Text = L.T("dlg.save", "Save"), Width = 120, Height = 30 };
        btn.Click += (_, _) =>
        {
            _config.ManualText = txt.Text;
            if (!string.IsNullOrEmpty(_currentProfile) &&
                _config.Profiles.TryGetValue(_currentProfile, out var pr))
                pr.ManualText = txt.Text;
            _config.Save();
            Log(L.T("log.manual.saved", "Custom JVM settings saved.")
                + (string.IsNullOrEmpty(_currentProfile) ? "" : $" ({_currentProfile})"));
            dlg.Close();
        };
        var btnDef = new Button { Text = L.T("dlg.default", "Defaults"), Width = 120, Height = 30 };
        btnDef.Click += (_, _) => { txt.Text = DefaultJvmCustomText; };
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 34 };
        btnDef.Dock = DockStyle.Left;
        btn.Dock = DockStyle.Right;
        bar.Controls.Add(btnDef);
        bar.Controls.Add(btn);
        dlg.Controls.Add(txt);
        dlg.Controls.Add(bar);
        dlg.ShowDialog(this);
    }

    private const string DefaultJvmCustomText =
        "-Dzomboid.znetlog=1\r\n" +
        "-Djava.library.path=win64/.\r\n" +
        "-XX:-CreateCoredumpOnCrash\r\n" +
        "-XX:-OmitStackTraceInFastThrow\r\n" +
        "-XX:+UseZGC";

    private async Task CheckUpdateAsync()
    {
        Log(L.T("log.update.checking", "Checking for updates..."));
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var json = await http.GetStringAsync($"https://api.steamcmd.net/v1/info/{SteamFinder.PZAppId}");
            var m = Regex.Match(json, @"""public""\s*:\s*\{[^}]*?""buildid""\s*:\s*""?(\d+)");
            var (local, _) = SteamFinder.ReadAppManifest(_txtGamePath.Text.Trim());
            if (!m.Success) { Log(L.T("log.update.fail", "Failed to get latest build.")); return; }
            Log(m.Groups[1].Value == local && !string.IsNullOrEmpty(local)
                ? string.Format(L.T("log.update.latest", "Up to date (build {0})."), local)
                : string.Format(L.T("log.update.available", "Update available: local={0} latest={1}."),
                    local, m.Groups[1].Value));
        }
        catch (Exception ex) { Log(L.T("log.update.fail", "Failed to get latest build.") + " " + ex.Message); }
    }

    private void OpenSteam()
    {
        try
        {
            var dir = SteamFinder.GetSteamDir();
            var exe = string.IsNullOrEmpty(dir) ? string.Empty : Path.Combine(dir, "steam.exe");
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("steam://open/main") { UseShellExecute = true });
            Log(L.T("log.steam.opened", "Steam opened."));
        }
        catch (Exception ex) { Log(L.T("log.steam.fail", "Failed to open Steam: ") + ex.Message); }
    }

    private void SaveSettings()
    {
        _config.GamePath = _txtGamePath.Text.Trim();
        // ★実効モードを設定に写す (unknown は off 正規化)。旧 Vk も併記して互換維持。
        var render = ResolvedRender();
        _config.Render = render == "unknown" ? "off" : render;
        _config.Vk = render == "zink" || render == "d3d12";
        _config.SafeMode = _chkSafe.Checked;
        _config.Extra = _txtExtra.Text.Trim();
        _config.XmsGb = (int)_txtXms.Value;
        _config.XmxGb = (int)_txtXmx.Value;
        _config.Save();
    }

    private void Launch()
    {
        var gameDir = _txtGamePath.Text.Trim();
        if (!Directory.Exists(gameDir)) { Log(L.T("log.gamepath.invalid", "Invalid game folder.")); return; }
        var log = new List<string>();
        try
        {
            // ★実効モード: オフ (ネイティブGL) / zink (Vulkan) / d3d12 (GL over D3D12)。
            var render = ResolvedRender();
            if (render == "zink" || render == "d3d12")
            {
                var r = Preflight.CheckVk(gameDir, render == "zink");
                log.AddRange(r.Messages);
                if (!r.Ok) { FlushLog(log); return; }
            }
            else if (render == "unknown")
            {
                log.Add(L.T("log.vk.unknown", "Mesa files not found (state unknown). Launching native GL."));
            }
            var psi = new ProcessStartInfo(Path.Combine(gameDir, "jre64", "bin", "java.exe"))
            {
                WorkingDirectory = gameDir,
                UseShellExecute = false,
            };
            if (render != "off" && render != "unknown")
            {
                // ★値がそのままドライバ名として効く ("zink" | "d3d12")。
                psi.Environment["GALLIUM_DRIVER"] = render;
            }
            else
            {
                psi.Environment.Remove("GALLIUM_DRIVER");
            }
            // ★_JAVA_OPTIONS はユーザーの環境変数がゲームへ漏れ込むのを遮断する
            //   意味でも空で固定する。ZombieBuddy は下記の通り VM オプションとして
            //   直接渡す (zbNative.c は -- を見ない。公式 doc の「-- 必須」は PZ
            //   小型ランチャー / Steam 起動オプション経由の話で、java.exe 直起動の
            //   ここでは渡すと Unrecognized option で落ちる。実測済み)。
            psi.Environment["_JAVA_OPTIONS"] = string.Empty;
            var zbState = ZbDetect(gameDir);

            string mainClass = "zombie.gameStates.MainScreenState";
            string classpath = "./;projectzomboid.jar";
            psi.ArgumentList.Add("-Djava.awt.headless=true");
            psi.ArgumentList.Add("--enable-native-access=ALL-UNNAMED");
            psi.ArgumentList.Add("--add-exports=java.base/jdk.internal.misc=ALL-UNNAMED");
            psi.ArgumentList.Add("-Dzomboid.steam=1");
            if (zbState.Installed)
            {
                // ★導入状態 = 有効。プロファイル別の on/off は廃止。
                psi.ArgumentList.Add("-agentlib:zbNative");
                log.Add(L.T("log.zb.on", "ZombieBuddy: -agentlib:zbNative applied."));
                // ★SHA チェック (警告のみ)。game dir の jar と Workshop ソースが
                //   不一致 = Workshop が更新されたが ZB の自己更新が未処理の可能性。
                //   自動置換はしない (ユーザーが Install ボタンで更新する)。
                if (zbState.UpdateAvailable)
                    log.Add(L.T("log.zb.update",
                        "ZombieBuddy update available (SHA-256 differs from the Workshop source). Use Install to update."));
                // ★ランチャー外 (Steam 直接 / bat) でも ZB が有効な場合の表示。
                var zbExt = new List<string>();
                if (zbState.JsonPatched) zbExt.Add("ProjectZomboid64.json");
                if (zbState.BatPatched) zbExt.Add("ProjectZomboid64.bat");
                if (zbExt.Count > 0)
                    log.Add(string.Format(L.T("log.zb.external", "ZombieBuddy is also active outside this launcher ({0} patched)."), string.Join(", ", zbExt)));
            }
            else if (zbState.Present)
            {
                log.Add(string.Format(L.T("log.zb.broken", "ZombieBuddy files incomplete (dll={0} jar={1}). Launching without it."),
                    zbState.Dll ? "yes" : "no", zbState.Jar ? "yes" : "no"));
            }
            var xms = (int)_txtXms.Value;
            var xmx = (int)_txtXmx.Value;
            if (xms > xmx) xms = xmx;
            if (_chkSafe.Checked)
            {
                psi.ArgumentList.Add("-Dzomboid.znetlog=1");
                psi.ArgumentList.Add("-XX:-CreateCoredumpOnCrash");
                psi.ArgumentList.Add("-XX:-OmitStackTraceInFastThrow");
                psi.ArgumentList.Add("-XX:+UseZGC");
                psi.ArgumentList.Add($"-Xms{xms}g");
                psi.ArgumentList.Add($"-Xmx{xmx}g");
                log.Add(string.Format(L.T("log.jvm.safe", "JVM: safe mode (custom disabled, -Xms{0}g -Xmx{1}g)."), xms, xmx));
            }
            else
            {
                psi.ArgumentList.Add($"-Xms{xms}g");
                psi.ArgumentList.Add($"-Xmx{xmx}g");
                log.Add(string.Format(L.T("log.jvm.custom", "JVM: custom (-Xms{0}g -Xmx{1}g)."), xms, xmx));
            }
            var manualTokens = _config.ManualText
                .Split(new[] { '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Concat(_txtExtra.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .ToList();
            if (_chkSafe.Checked)
            {
                log.Add(L.T("log.agent.safemode", "Agents excluded (safe mode)."));
            }
            else
            {
                MigrateAgentSha(_config);
                var grev = Javamod.ReadGameRevision(gameDir);
                var enabled = _config.Agents.Where(a => a.Enabled).ToList();
                foreach (var ag in enabled)
                    ag.JarPath = ResolveAgentAbs(gameDir, ag.JarPath);
                var missing = enabled.Where(a => string.IsNullOrEmpty(a.JarPath) || !File.Exists(a.JarPath)).ToList();
                if (missing.Count > 0)
                {
                    MessageBox.Show(this,
                        L.T("sha.missing.msg", "Not found. Removing from list:") + "\n" +
                        string.Join("\n", missing.Select(a => Path.GetFileName(a.JarPath))),
                        L.T("sha.missing.title", "Agent missing"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    foreach (var m in missing) _config.Agents.Remove(m);
                    _config.Save();
                    RefreshAgentSummary();
                    log.Add(L.T("log.agent.missing.abort", "Launch aborted (missing agents removed)."));
                    FlushLog(log);
                    return;
                }
                var firstSeen = new List<AgentEntry>();
                var changed = new List<(AgentEntry Ag, string Cur)>();
                var pendingOk = new List<AgentEntry>();
                foreach (var ag in enabled)
                {
                    var cur = Javamod.Sha256(ag.JarPath);
                    if (!string.IsNullOrEmpty(ag.ApprovedSha) && cur == ag.ApprovedSha)
                    {
                        ag.PendingSha = string.Empty;
                    }
                    else if (!string.IsNullOrEmpty(ag.PendingSha) && cur == ag.PendingSha && cur != ag.ApprovedSha)
                        pendingOk.Add(ag);
                    else if (string.IsNullOrEmpty(ag.ApprovedSha) && string.IsNullOrEmpty(ag.PendingSha))
                        firstSeen.Add(ag);
                    else if (!string.IsNullOrEmpty(ag.ApprovedSha) && cur != ag.ApprovedSha)
                    {
                        ag.PendingSha = cur;
                        changed.Add((ag, cur));
                    }
                }
                // ★確定した承認状態をプロファイル退避に反映してから保存する。
                //   これが無いと選択中プロファイルの退避が古いまま残る。
                SyncAgentShaToProfile();
                _config.Save();
                if (firstSeen.Count > 0)
                {
                    var r = MessageBox.Show(this,
                        L.T("sha.firstseen.msg", "New agents. Approve?") + "\n" +
                        string.Join("\n", firstSeen.Select(a => Path.GetFileName(a.JarPath))),
                        L.T("sha.firstseen.title", "Approve new agents"),
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                    if (r != DialogResult.OK)
                    {
                        log.Add(L.T("log.sha.firstseen.cancel", "Launch aborted (new agents not approved)."));
                        FlushLog(log);
                        return;
                    }
                    foreach (var ag in firstSeen)
                    {
                        ag.ApprovedSha = Javamod.Sha256(ag.JarPath);
                        ag.PendingSha = string.Empty;
                    }
                    SyncAgentShaToProfile();
                    _config.Save();
                    log.Add(L.T("log.sha.firstseen.ok", "New agents approved."));
                }
                if (changed.Count > 0)
                {
                    MessageBox.Show(this,
                        L.T("sha.changed.msg", "Updated:") + "\n" +
                        string.Join("\n", changed.Select(c => Path.GetFileName(c.Ag.JarPath))),
                        L.T("sha.changed.title", "Agents updated"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _config.Save();
                    log.Add(L.T("log.sha.changed.abort", "Launch aborted (agents updated). Approve on next launch."));
                    FlushLog(log);
                    return;
                }
                if (pendingOk.Count > 0)
                {
                    var r = MessageBox.Show(this,
                        L.T("sha.pending.msg", "Approve recorded updates?") + "\n" +
                        string.Join("\n", pendingOk.Select(a => Path.GetFileName(a.JarPath))),
                        L.T("sha.pending.title", "Approve updates"),
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                    if (r != DialogResult.OK)
                    {
                        log.Add(L.T("log.sha.pending.cancel", "Launch aborted (updates not approved)."));
                        FlushLog(log);
                        return;
                    }
                    foreach (var ag in pendingOk)
                    {
                        ag.ApprovedSha = ag.PendingSha;
                        ag.PendingSha = string.Empty;
                    }
                    SyncAgentShaToProfile();
                    _config.Save();
                    log.Add(L.T("log.sha.pending.ok", "Updates approved."));
                }
                foreach (var ag in enabled)
                {
                    var abs = ag.JarPath;
                    var tag = Path.GetFileName(abs);
                    var gate = (ag.Gate ?? "lenient").ToLowerInvariant();
                    var sha = Javamod.Sha256(abs);
                    var arev = Javamod.ReadAgentRevision(abs);
                    bool revMissing = string.IsNullOrEmpty(arev) || string.IsNullOrEmpty(grev);
                    bool mismatch = !revMissing &&
                        !arev.Equals(grev, StringComparison.OrdinalIgnoreCase);
                    if (gate == "strict" && (revMissing || mismatch))
                    {
                        log.Add(revMissing
                            ? string.Format(L.T("log.agent.unconfirmed", "Agent excluded (rev unknown) [{0}]: {1}."), gate, tag)
                            : string.Format(L.T("log.agent.mismatch", "Agent excluded (mismatch) [{0}]: agent={1} game={2} {3}."), gate, arev, grev, tag));
                        continue;
                    }
                    if (gate == "lenient" && mismatch)
                        log.Add(string.Format(L.T("log.agent.lenient", "Agent warning (continuing) [{0}]: agent={1} game={2} {3}."), gate, arev, grev, tag));
                    var norm = NormalizeAgentPath(abs);
                    bool dup = manualTokens.Any(t =>
                        t.StartsWith("-javaagent", StringComparison.OrdinalIgnoreCase) &&
                        NormalizeAgentPath(t) == norm);
                    if (dup) { log.Add(string.Format(L.T("log.agent.dup", "Agent suppressed (already in manual args): {0}."), tag)); continue; }
                    // ★「=引数」形式は、agent 自身が args 文字列も読む時に効く。
                    //   「=」の後ろは JVM 引数ではなく <args> なので、
                    //   agent 側の System.getProperty には届かない。空なら付けない。
                    var agentArgs = string.IsNullOrWhiteSpace(ag.Args) ? string.Empty : "=" + ag.Args.Trim();
                    psi.ArgumentList.Add("-javaagent:" + abs + agentArgs);
                    log.Add(string.Format(L.T("log.agent.applied", "Agent applied [{0}] jar={1} sha={2} rev={3}."),
                        gate, tag, sha.Length >= 8 ? sha.Substring(0, 8) : "?", string.IsNullOrEmpty(arev) ? "?" : arev));
                }
                if (!_config.Agents.Any(a => a.Enabled)) log.Add(L.T("log.agent.off", "No agents enabled."));
            }
            if (!_chkSafe.Checked)
                foreach (var a in _config.ManualText.Split(new[] { '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                    psi.ArgumentList.Add(a);
            psi.ArgumentList.Add("-Djava.library.path=./win64/;./");
            psi.ArgumentList.Add("-cp");
            psi.ArgumentList.Add(classpath);
            psi.ArgumentList.Add(mainClass);
            foreach (var a in _txtExtra.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                psi.ArgumentList.Add(a);
            try
            {
                _gameProc?.Dispose();
                _gameProc = Process.Start(psi);
            }
            catch
            {
                _gameProc = null;
                throw;
            }
            SaveSettings();
            log.Add(L.T("log.launched", "Launched (renderer=") + render + ")");
        }
        catch (Exception ex) { log.Add(L.T("log.launchfail", "Launch failed: ") + ex.Message); }
        FlushLog(log);
    }

    private void FlushLog(List<string> log)
    {
        foreach (var s in log) Log(s);
    }
}
