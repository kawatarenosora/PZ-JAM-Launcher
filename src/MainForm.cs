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
    private Button _btnVkOn;
    private Button _btnVkOff;
    private Label _lblVk;
    private NumericUpDown _txtXms;
    private NumericUpDown _txtXmx;
    private TextBox _txtExtra;
    private Label _lblVersion;
    private TextBox _txtLog;
    private ComboBox _cmbLang;

    public MainForm()
    {
        Text = L.T("app.title", "PZ JAM Launcher (v1.0)");
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
        var btnProfDel = new Button { Text = L.T("left.delete", "Delete"), AutoSize = true };
        btnProfDel.Click += (_, _) => DeleteProfile();
        rowProf.Controls.Add(btnProfNew);
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
        _btnVkOn = new Button { Text = L.T("vk.on", "VK ON"), AutoSize = true };
        _btnVkOn.Click += (_, _) => VkOn();
        _btnVkOff = new Button { Text = L.T("vk.off", "VK OFF"), AutoSize = true };
        _btnVkOff.Click += (_, _) => VkOff();
        _lblVk = new Label { Text = "?", AutoSize = true };
        rowVk.Controls.Add(new Label { Text = L.T("right.vk", "VK (Zink)"), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
        rowVk.Controls.Add(_btnVkOn);
        rowVk.Controls.Add(_btnVkOff);
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
        if (_lblVk == null) return;
        switch (GetVkState())
        {
            case 1:
                _lblVk.Text = L.T("vk.state.on", "enabled");
                _lblVk.ForeColor = Color.Green;
                break;
            case 0:
                _lblVk.Text = L.T("vk.state.off", "disabled");
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

    private void ApplyVkState(bool want)
    {
        if (want) VkOn();
        else VkOff();
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
        if (_btnVkOn != null) _btnVkOn.Enabled = !running;
        if (_btnVkOff != null) _btnVkOff.Enabled = !running;
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
        Log(L.T("log.gamepath", "Game folder: ") + cands[0]);
    }

    private void BrowseGamePath()
    {
        using var dlg = new FolderBrowserDialog { Description = L.T("dlg.gamefolder", "Select the ProjectZomboid folder") };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            _txtGamePath.Text = dlg.SelectedPath;
            RefreshVersion();
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
        ApplyVkState(p.Vk);
        _chkSafe.Checked = p.SafeMode;
        _config.Agents = p.Agents.Select(a => new AgentEntry
        {
            Enabled = a.Enabled, JarPath = a.JarPath, Gate = a.Gate, Sha256 = a.Sha256,
        }).ToList();
        RefreshAgentSummary();
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
        _config.Profiles[name] = new LaunchProfile
        {
            Vk = GetVkState() == 1,
            SafeMode = _chkSafe.Checked,
            Extra = _txtExtra.Text.Trim(),
            ManualText = _config.ManualText,
            XmsGb = (int)_txtXms.Value,
            XmxGb = (int)_txtXmx.Value,
            Agents = _config.Agents.Select(a => new AgentEntry
            {
                Enabled = a.Enabled, JarPath = a.JarPath, Gate = a.Gate,
                Sha256 = Javamod.Sha256(a.JarPath),
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
            SafeMode = _chkSafe.Checked,
            Extra = _txtExtra.Text.Trim(),
            ManualText = _config.ManualText,
            XmsGb = (int)_txtXms.Value,
            XmxGb = (int)_txtXmx.Value,
            Agents = _config.Agents.Select(a => new AgentEntry
            {
                Enabled = a.Enabled, JarPath = a.JarPath, Gate = a.Gate,
                Sha256 = Javamod.Sha256(a.JarPath),
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

    private static string NormalizeAgentPath(string p)
    {
        try
        {
            var t = p.Trim().Trim('"');
            if (t.StartsWith("-javaagent:", StringComparison.OrdinalIgnoreCase))
                t = t.Substring("-javaagent:".Length);
            t = t.Trim().Trim('"');
            return Path.GetFullPath(t).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
        }
        catch { return p.Trim().ToLowerInvariant(); }
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
        _config.Vk = GetVkState() == 1;
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
            bool vkOn = GetVkState() == 1;
            if (vkOn)
            {
                var r = Preflight.CheckVk(gameDir);
                log.AddRange(r.Messages);
                if (!r.Ok) { FlushLog(log); return; }
            }
            else if (GetVkState() == -1)
            {
                log.Add(L.T("log.vk.unknown", "VK state unknown. Launching native."));
            }
            var psi = new ProcessStartInfo(Path.Combine(gameDir, "jre64", "bin", "java.exe"))
            {
                WorkingDirectory = gameDir,
                UseShellExecute = false,
            };
            if (vkOn)
            {
                psi.Environment["GALLIUM_DRIVER"] = "zink";
            }
            else
            {
                psi.Environment.Remove("GALLIUM_DRIVER");
            }
            psi.Environment["_JAVA_OPTIONS"] = File.Exists(Path.Combine(gameDir, "zbNative.dll")) ? "-agentlib:zbNative" : string.Empty;

            string mainClass = "zombie.gameStates.MainScreenState";
            string classpath = "./;projectzomboid.jar";
            psi.ArgumentList.Add("-Djava.awt.headless=true");
            psi.ArgumentList.Add("--enable-native-access=ALL-UNNAMED");
            psi.ArgumentList.Add("--add-exports=java.base/jdk.internal.misc=ALL-UNNAMED");
            psi.ArgumentList.Add("-Dzomboid.steam=1");
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
                var grev = Javamod.ReadGameRevision(gameDir);
                foreach (var ag in _config.Agents.Where(a => a.Enabled))
                {
                    var raw = ag.JarPath ?? string.Empty;
                    string abs;
                    try
                    {
                        abs = Path.IsPathRooted(raw)
                            ? Path.GetFullPath(raw)
                            : Path.GetFullPath(Path.Combine(gameDir, raw));
                    }
                    catch { abs = raw; }
                    var tag = Path.GetFileName(abs);
                    if (string.IsNullOrEmpty(raw) || !File.Exists(abs))
                    {
                        log.Add(string.Format(L.T("log.agent.missing", "Agent excluded (missing): {0}."), tag));
                        continue;
                    }
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
                    psi.ArgumentList.Add("-javaagent:" + abs);
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
            log.Add(L.T("log.launched", "Launched (VK=") + (vkOn ? "ON" : "OFF") + ")");
        }
        catch (Exception ex) { log.Add(L.T("log.launchfail", "Launch failed: ") + ex.Message); }
        FlushLog(log);
    }

    private void FlushLog(List<string> log)
    {
        foreach (var s in log) Log(s);
    }
}
