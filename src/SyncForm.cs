using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PZLauncher;

internal sealed class SyncForm : Form
{
    private readonly AppConfig _config;
    private readonly Action<string> _log;
    private TextBox _txtStaging;
    private ComboBox _cmbTemplate;
    private CheckedListBox _clb;
    private TextBox _txtWid;
    private List<StagedJar> _jars = new();
    private string _libsDir = string.Empty;
    private bool _loading;

    public SyncForm(AppConfig config, Action<string> log)
    {
        _config = config;
        _log = log;
        Text = L.T("sync.title", "Manage javamods");
        Size = new Size(640, 520);
        StartPosition = FormStartPosition.CenterParent;
        BuildUi();
        RefreshAll();
    }

    private string Staging => _txtStaging.Text.Trim();
    private string TemplateMod => _cmbTemplate.SelectedItem?.ToString() ?? string.Empty;

    private void BuildUi()
    {
        var p = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(8),
        };
        Controls.Add(p);

        p.Controls.Add(new Label { Text = L.T("sync.staging", "javamod staging folder"), AutoSize = true });
        var rowS = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _txtStaging = new TextBox { Width = 440 };
        _txtStaging.Text = _config.JavamodDir;
        var btnSB = new Button { Text = L.T("left.browse", "Browse..."), AutoSize = true };
        btnSB.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = L.T("sync.staging.desc", "Select the javamod staging folder") };
            if (dlg.ShowDialog() == DialogResult.OK) { _txtStaging.Text = dlg.SelectedPath; RefreshAll(); }
        };
        rowS.Controls.Add(_txtStaging);
        rowS.Controls.Add(btnSB);
        p.Controls.Add(rowS);

        p.Controls.Add(new Label { Text = L.T("sync.template", "Template mod (in user mods)"), AutoSize = true });
        _cmbTemplate = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 440 };
        _cmbTemplate.SelectedIndexChanged += (_, _) => { if (!_loading) RefreshAll(); };
        p.Controls.Add(_cmbTemplate);

        p.Controls.Add(new Label { Text = L.T("sync.list", "JAR list (checked = enabled)"), AutoSize = true });
        _clb = new CheckedListBox { Width = 580, Height = 200, CheckOnClick = true };
        _clb.SelectedIndexChanged += (_, _) => ShowSelectedWid();
        p.Controls.Add(_clb);

        var rowW = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        rowW.Controls.Add(new Label { Text = "WorkshopID", AutoSize = true });
        _txtWid = new TextBox { Width = 200 };
        var btnWid = new Button { Text = L.T("sync.setid", "Set ID"), AutoSize = true };
        btnWid.Click += (_, _) => SetWid();
        rowW.Controls.Add(_txtWid);
        rowW.Controls.Add(btnWid);
        p.Controls.Add(rowW);

        var rowB = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        var btnSave = new Button { Text = L.T("sync.saveclose", "Save and close"), AutoSize = true, Width = 130 };
        btnSave.Click += (_, _) => { if (ApplySync()) Close(); };
        var btnUpd = new Button { Text = L.T("sync.updatecheck", "Check updates..."), AutoSize = true };
        btnUpd.Click += (_, _) => OpenUpdate();
        var btnDel = new Button { Text = L.T("sync.delete", "Delete..."), AutoSize = true };
        btnDel.Click += (_, _) => OpenDelete();
        rowB.Controls.Add(btnSave);
        rowB.Controls.Add(btnUpd);
        rowB.Controls.Add(btnDel);
        p.Controls.Add(rowB);
    }

    private void RefreshAll()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            var mods = Javamod.ListInstalledModNames();
            _cmbTemplate.Items.Clear();
            foreach (var m in mods) _cmbTemplate.Items.Add(m);
            var cur = _config.TemplateMod;
            if (!string.IsNullOrEmpty(cur) && mods.Contains(cur)) _cmbTemplate.SelectedItem = cur;
            else if (mods.Count > 0) _cmbTemplate.SelectedIndex = 0;
            RefreshJars();
        }
        finally { _loading = false; }
    }

    private string TemplateDir() =>
        string.IsNullOrEmpty(TemplateMod) ? string.Empty : Path.Combine(Javamod.UserModsDir, TemplateMod);

    private void RefreshJars()
    {
        _libsDir = string.Empty;
        _clb.Items.Clear();
        _jars.Clear();
        if (string.IsNullOrEmpty(TemplateMod) || !Directory.Exists(TemplateDir())) return;
        var gv = Javamod.ReadGameVersion(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Zomboid"));
        var mi = Javamod.FindModInfo(TemplateDir(), gv);
        if (string.IsNullOrEmpty(mi)) { _log(L.T("log.sync.nomodinfo", "No mod.info in template.")); return; }
        var jjf = Javamod.ReadModInfoValue(mi, "javaJarFile");
        _libsDir = Javamod.ResolveLibsDir(TemplateDir(), mi, jjf);
        _jars = Javamod.ListStaged(Staging, _libsDir);
        foreach (var j in _jars)
        {
            var tag = string.IsNullOrEmpty(j.WorkshopId)
                ? L.T("sync.manual", "manual")
                : "WS:" + j.WorkshopId;
            if (j.UpdateAvailable) tag += L.T("sync.needsupdate", " [update]");
            var idx = _clb.Items.Add($"{j.Name} ({tag})");
            _clb.SetItemChecked(idx, j.Enabled);
        }
    }

    private void ShowSelectedWid()
    {
        if (_clb.SelectedIndex < 0 || _clb.SelectedIndex >= _jars.Count) return;
        _txtWid.Text = _jars[_clb.SelectedIndex].WorkshopId;
    }

    private void SetWid()
    {
        if (_clb.SelectedIndex < 0 || _clb.SelectedIndex >= _jars.Count) return;
        var j = _jars[_clb.SelectedIndex];
        j.WorkshopId = _txtWid.Text.Trim();
        Javamod.SaveSidecar(j.StagingPath, j.WorkshopId);
        _log(L.T("log.sync.idset", "ID set: ") + $"{j.Name} = {j.WorkshopId}");
        RefreshJars();
    }

    private bool ApplySync()
    {
        if (string.IsNullOrEmpty(_libsDir)) { _log(L.T("log.sync.notemplate", "Template not resolved.")); return false; }
        try
        {
            Directory.CreateDirectory(_libsDir);
            var gv = Javamod.ReadGameVersion(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Zomboid"));
            for (var i = 0; i < _jars.Count && i < _clb.Items.Count; i++)
            {
                var j = _jars[i];
                var on = _clb.GetItemChecked(i);
                var dest = Path.Combine(_libsDir, j.Name);
                if (on)
                {
                    File.Copy(j.StagingPath, dest, true);
                    if (File.Exists(dest + ".off")) File.Delete(dest + ".off");
                    if (string.IsNullOrEmpty(j.WorkshopId))
                    {
                        var sc = Javamod.LoadSidecar(j.StagingPath + ".pzsrc.json");
                        if (sc != null) j.WorkshopId = sc.WorkshopId;
                    }
                    if (!string.IsNullOrEmpty(j.WorkshopId))
                        Javamod.SaveSidecar(j.StagingPath, j.WorkshopId);
                    _log(string.Format(L.T("log.sync.enabled", "Enabled: {0} (game {1})."), j.Name, gv));
                }
                else
                {
                    if (File.Exists(dest))
                    {
                        if (File.Exists(dest + ".off")) File.Delete(dest + ".off");
                        File.Move(dest, dest + ".off");
                    }
                    _log(string.Format(L.T("log.sync.disabled", "Disabled: {0}."), j.Name));
                }
            }
            _config.JavamodDir = Staging;
            _config.TemplateMod = TemplateMod;
            _config.Save();
            return true;
        }
        catch (Exception ex) { _log(L.T("log.sync.fail", "Sync failed: ") + ex.Message); return false; }
    }

    private void OpenUpdate()
    {
        using var f = new BatchForm(L.T("batch.update", "Update"), _jars.Where(j => j.UpdateAvailable || CheckUpdate(j)).ToList(),
            L.T("batch.update.run", "Update"), targets =>
            {
                foreach (var j in targets)
                {
                    var dest = Path.Combine(_libsDir, j.Name);
                    var src = j.UpdateSource;
                    if (string.IsNullOrEmpty(src)) continue;
                    if (File.Exists(dest)) File.Copy(dest, dest + ".bak", true);
                    File.Copy(src, dest, true);
                    _log(string.Format(L.T("log.sync.updated", "Updated: {0}."), j.Name));
                }
            }, _log);
        f.ShowDialog(this);
        RefreshJars();
    }

    private bool CheckUpdate(StagedJar j)
    {
        if (string.IsNullOrEmpty(j.WorkshopId)) return false;
        var steamDir = SteamFinder.GetSteamDir();
        if (string.IsNullOrEmpty(steamDir)) return false;
        var src = Javamod.FindWorkshopJar(Javamod.SteamWorkshopContent(steamDir), j.WorkshopId, j.Name);
        if (string.IsNullOrEmpty(src)) return false;
        j.UpdateSource = src;
        var a = Javamod.Sha256(j.StagingPath);
        var b = Javamod.Sha256(src);
        j.UpdateAvailable = !string.IsNullOrEmpty(a) && a != b;
        return j.UpdateAvailable;
    }

    private void OpenDelete()
    {
        using var f = new BatchForm(L.T("batch.delete", "Delete"), _jars.ToList(), L.T("batch.delete.run", "Delete"), targets =>
        {
            foreach (var j in targets)
            {
                try
                {
                    var dest = Path.Combine(_libsDir, j.Name);
                    foreach (var p in new[] { j.StagingPath, j.StagingPath + ".pzsrc.json", dest, dest + ".off", dest + ".bak" })
                        if (File.Exists(p)) File.Delete(p);
                    _log(string.Format(L.T("log.sync.deleted", "Deleted: {0}."), j.Name));
                }
                catch (Exception ex) { _log(string.Format(L.T("log.sync.deletefail", "Delete failed {0}: {1}."), j.Name, ex.Message)); }
            }
        }, _log);
        f.ShowDialog(this);
        RefreshJars();
    }
}

internal sealed class BatchForm : Form
{
    public BatchForm(string title, List<StagedJar> jars, string action,
        Action<List<StagedJar>> run, Action<string> log)
    {
        Text = title;
        Size = new Size(560, 420);
        StartPosition = FormStartPosition.CenterParent;
        var clb = new CheckedListBox
        {
            Dock = DockStyle.Fill, CheckOnClick = true,
        };
        foreach (var j in jars)
        {
            var tag = string.IsNullOrEmpty(j.WorkshopId)
                ? L.T("batch.nosource", "no source")
                : "WS:" + j.WorkshopId;
            clb.Items.Add($"{j.Name} ({tag})", true);
        }
        var btn = new Button { Text = action, Dock = DockStyle.Bottom, Height = 34 };
        btn.Click += (_, _) =>
        {
            var targets = jars.Where((_, i) => clb.GetItemChecked(i)).ToList();
            if (targets.Count == 0) { Close(); return; }
            var msg = L.T("batch.confirm.list", "Targets:") + "\n"
                + string.Join("\n", targets.Select(t => t.Name)) + "\n"
                + string.Format(L.T("batch.confirm.ask", "OK to {0}?"), action);
            if (MessageBox.Show(this, msg,
                    L.T("batch.confirm.title", "Confirm"),
                    MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            run(targets);
            Close();
        };
        Controls.Add(clb);
        Controls.Add(btn);
    }
}
