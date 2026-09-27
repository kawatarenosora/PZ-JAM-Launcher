using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PZLauncher;

internal sealed class AgentsForm : Form
{
    private readonly AppConfig _config;
    private readonly Action<string> _log;
    private CheckedListBox _clb;
    private ComboBox _cmbGate;
    private Label _lblRev;
    private List<AgentEntry> _rows = new();

    public AgentsForm(AppConfig config, Action<string> log)
    {
        _config = config;
        _log = log;
        Text = L.T("agents.title", "Manage Agents");
        Size = new Size(620, 440);
        StartPosition = FormStartPosition.CenterParent;
        _rows = config.Agents.Select(a => new AgentEntry
        {
            Enabled = a.Enabled, JarPath = a.JarPath, Gate = string.IsNullOrEmpty(a.Gate) ? "lenient" : a.Gate,
            Sha256 = a.Sha256,
        }).ToList();
        BuildUi();
        RefreshList();
    }

    private void BuildUi()
    {
        _clb = new CheckedListBox
        {
            Dock = DockStyle.Top, Height = 260, CheckOnClick = true,
        };
        _clb.SelectedIndexChanged += (_, _) => ShowSelected();
        Controls.Add(_clb);

        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 36, FlowDirection = FlowDirection.LeftToRight,
        };
        var btnAdd = new Button { Text = L.T("agents.add", "Add..."), AutoSize = true };
        btnAdd.Click += (_, _) => AddRow();
        var btnDel = new Button { Text = L.T("agents.delete", "Delete"), AutoSize = true };
        btnDel.Click += (_, _) => DelRow();
        var btnUp = new Button { Text = "▲", AutoSize = true };
        btnUp.Click += (_, _) => MoveRow(-1);
        var btnDown = new Button { Text = "▼", AutoSize = true };
        btnDown.Click += (_, _) => MoveRow(1);
        _cmbGate = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        _cmbGate.Items.AddRange(new object[] { "strict", "lenient", "none" });
        _cmbGate.SelectedIndexChanged += (_, _) => ChangeGate();
        _lblRev = new Label { Text = "rev: -", AutoSize = true };
        row.Controls.Add(btnAdd);
        row.Controls.Add(btnDel);
        row.Controls.Add(btnUp);
        row.Controls.Add(btnDown);
        row.Controls.Add(new Label { Text = L.T("agents.gate", "gate:"), AutoSize = true });
        row.Controls.Add(_cmbGate);
        row.Controls.Add(_lblRev);
        Controls.Add(row);

        var btnSave = new Button { Text = L.T("agents.saveclose", "Save and close"), Dock = DockStyle.Bottom, Height = 34 };
        btnSave.Click += (_, _) => { ApplyChecks(); _config.Save(); Close(); };
        Controls.Add(btnSave);
    }

    private void RefreshList()
    {
        _clb.Items.Clear();
        foreach (var a in _rows)
        {
            var rev = Javamod.ReadAgentRevision(a.JarPath);
            var idx = _clb.Items.Add($"{(a.Enabled ? "[x]" : "[ ]")} {Path.GetFileName(a.JarPath)} [{a.Gate}] rev:{(string.IsNullOrEmpty(rev) ? "?" : rev)}");
            _clb.SetItemChecked(idx, a.Enabled);
        }
        ShowSelected();
    }

    private void ShowSelected()
    {
        if (_clb.SelectedIndex < 0 || _clb.SelectedIndex >= _rows.Count)
        {
            _lblRev.Text = "rev: -";
            return;
        }
        var a = _rows[_clb.SelectedIndex];
        var rev = Javamod.ReadAgentRevision(a.JarPath);
        var sha = Javamod.Sha256(a.JarPath);
        _lblRev.Text = $"rev: {(string.IsNullOrEmpty(rev) ? "?" : rev)} sha:{(sha.Length >= 8 ? sha.Substring(0, 8) : "?")}";
        _cmbGate.SelectedItem = new[] { "strict", "lenient", "none" }.Contains(a.Gate) ? a.Gate : "lenient";
    }

    private void AddRow()
    {
        using var dlg = new OpenFileDialog { Filter = L.T("agents.filter", "agent jar|*.jar"), Title = L.T("agents.choosetitle", "Select agent jar") };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        _rows.Add(new AgentEntry { Enabled = true, JarPath = dlg.FileName, Gate = "lenient" });
        _log(L.T("log.agents.added", "Agent added: ") + dlg.FileName);
        RefreshList();
    }

    private void DelRow()
    {
        if (_clb.SelectedIndex < 0 || _clb.SelectedIndex >= _rows.Count) return;
        _log(L.T("log.agents.removed", "Agent removed: ") + _rows[_clb.SelectedIndex].JarPath);
        _rows.RemoveAt(_clb.SelectedIndex);
        RefreshList();
    }

    private void MoveRow(int dir)
    {
        var i = _clb.SelectedIndex;
        if (i < 0 || i >= _rows.Count) return;
        var j = i + dir;
        if (j < 0 || j >= _rows.Count) return;
        for (var k = 0; k < _rows.Count && k < _clb.Items.Count; k++)
            _rows[k].Enabled = _clb.GetItemChecked(k);
        var tmp = _rows[i];
        _rows[i] = _rows[j];
        _rows[j] = tmp;
        RefreshList();
        _clb.SelectedIndex = j;
    }

    private void ChangeGate()
    {
        if (_clb.SelectedIndex < 0 || _clb.SelectedIndex >= _rows.Count) return;
        if (_cmbGate.SelectedItem == null) return;
        for (var i = 0; i < _rows.Count && i < _clb.Items.Count; i++)
            _rows[i].Enabled = _clb.GetItemChecked(i);
        _rows[_clb.SelectedIndex].Gate = _cmbGate.SelectedItem.ToString() ?? "lenient";
        RefreshList();
        _clb.SelectedIndex = Math.Min(_clb.SelectedIndex, _rows.Count - 1);
    }

    private void ApplyChecks()
    {
        for (var i = 0; i < _rows.Count && i < _clb.Items.Count; i++)
            _rows[i].Enabled = _clb.GetItemChecked(i);
        foreach (var a in _rows) a.Sha256 = Javamod.Sha256(a.JarPath);
        _config.Agents = _rows;
    }
}
