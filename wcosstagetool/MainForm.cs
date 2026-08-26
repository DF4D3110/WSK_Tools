using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace WcosStageTool;

public class MainForm : Form
{
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private ToolStripButton _runBtn = null!;
    private bool _isRunning;

    private ToolStrip _topStrip = null!;
    private ToolStripComboBox _adkVersionStrip = null!;
    private ToolStripTextBox _adkRootStrip = null!;
    private ToolStripDropDownButton _fileMenu = null!;
    private ToolStripDropDownButton _langMenu = null!;
    private string _adkRoot = "";

    private Panel _navPanel = null!;
    private Panel _contentPanel = null!;
    private Button[] _navButtons = null!;
    private Label _navTitle = null!;
    private int _currentPage = -1;

    private TextBox _buildToolEdit = null!;
    private TextBox _buildOutDirEdit = null!;
    private TextBox _buildFfuNameEdit = null!;
    private TextBox _buildXmlEdit = null!;
    private TextBox _buildPkgEdit = null!;
    private ComboBox _buildCpuCombo = null!;

    private TextBox _patchToolEdit = null!;
    private TextBox _patchFfuEdit = null!;
    private ComboBox _patchCpuCombo = null!;
    private TextBox _patchDriverEdit = null!;

    private TextBox _updateToolEdit = null!;
    private TextBox _updateVhdEdit = null!;
    private TextBox _updateCabEdit = null!;

    private TextBox _bcdEdit = null!;
    private CheckBox _bcdDebug = null!;
    private CheckBox _bcdSerial = null!;
    private CheckBox _bcdPort = null!;
    private CheckBox _bcdBaud = null!;
    private CheckBox _bcdTestSign = null!;
    private CheckBox _bcdNoInt = null!;

    public MainForm()
    {
        Text = Lang.Get("Title") + " - WSK Tools v1.0.4";
        ClientSize = new Size(820, 540);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.White;

        InitTopBar();
        InitNavPanel();
        InitContentPanel();
        InitStatus();
        ApplyLanguage();
        ScanAdkVersions();
        SelectPage(0);
    }

    private void InitTopBar()
    {
        _topStrip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Location = new Point(0, 0), Size = new Size(820, 28), BackColor = Color.WhiteSmoke };
        var tight = new Padding(2, 0, 2, 0);

        _fileMenu = new ToolStripDropDownButton(Lang.Get("File")) { Margin = tight };
        var exitItem = new ToolStripMenuItem(Lang.Get("Exit"));
        exitItem.Click += (s, e) => Close();
        _fileMenu.DropDownItems.Add(exitItem);

        _langMenu = new ToolStripDropDownButton(Lang.Get("Language")) { Margin = tight };
        foreach (var lang in Lang.SupportedLanguages)
        {
            var item = new ToolStripMenuItem(lang);
            item.Click += (s, e) => { Lang.SetLanguage(lang); ApplyLanguage(); };
            _langMenu.DropDownItems.Add(item);
        }

        var aboutBtn = new ToolStripButton(Lang.Get("About")) { Margin = tight };
        aboutBtn.Click += (s, e) => ShowAbout();

        var sep1 = new ToolStripSeparator { Margin = new Padding(0, 0, 0, 0) };

        var adkLabel = new ToolStripLabel("ADK:") { Margin = tight };
        _adkVersionStrip = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, Margin = tight };
        _adkVersionStrip.SelectedIndexChanged += (s, e) => ApplyAdkVersion();
        var scanBtn = new ToolStripButton("Scan") { Margin = tight };
        scanBtn.Click += (s, e) => ScanAdkVersions();

        var sep2 = new ToolStripSeparator { Margin = new Padding(0, 0, 0, 0) };

        var rootLabel = new ToolStripLabel("Root:") { Margin = tight };
        _adkRoot = Application.StartupPath;
        _adkRootStrip = new ToolStripTextBox { Text = _adkRoot, Width = 220, Margin = tight, AutoSize = false };
        var browseBtn = new ToolStripButton("...") { Margin = tight, AutoSize = false, Size = new Size(26, 22) };
        browseBtn.Click += (s, e) =>
        {
            using var dlg = new FolderBrowserDialog();
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _adkRootStrip.Text = dlg.SelectedPath;
                _adkRoot = dlg.SelectedPath;
                ScanAdkVersions();
            }
        };

        _runBtn = new ToolStripButton(Lang.Get("Run")) { Margin = tight, AutoSize = false, Size = new Size(70, 24), BackColor = Color.FromArgb(0, 120, 215), ForeColor = Color.White, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
        _runBtn.Click += (s, e) => RunCommand();

        _topStrip.Items.AddRange(new ToolStripItem[] {
            _fileMenu, _langMenu, aboutBtn, sep1,
            adkLabel, _adkVersionStrip, scanBtn, sep2,
            rootLabel, _adkRootStrip, browseBtn, _runBtn
        });

        Controls.Add(_topStrip);
    }

    private void InitNavPanel()
    {
        _navPanel = new Panel { Location = new Point(0, 28), Size = new Size(170, 490), BackColor = Color.FromArgb(245, 245, 245), BorderStyle = BorderStyle.FixedSingle };

        _navTitle = new Label { Text = Lang.Get("NavTitle"), Location = new Point(15, 12), Size = new Size(140, 24), Font = new Font("Segoe UI", 11F, FontStyle.Bold), ForeColor = Color.FromArgb(60, 60, 60) };
        _navPanel.Controls.Add(_navTitle);

        var pageNames = new[] { Lang.Get("TabBuild"), Lang.Get("TabPatch"), Lang.Get("TabUpdate"), Lang.Get("TabBcd") };
        _navButtons = new Button[4];
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            var btn = new Button
            {
                Text = pageNames[i],
                Location = new Point(10, 45 + i * 50),
                Size = new Size(150, 42),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0),
                BackColor = Color.White,
                ForeColor = Color.FromArgb(50, 50, 50),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(220, 235, 250);
            btn.Click += (s, e) => SelectPage(idx);
            _navButtons[i] = btn;
            _navPanel.Controls.Add(btn);
        }

        Controls.Add(_navPanel);
    }

    private void InitContentPanel()
    {
        _contentPanel = new Panel { Location = new Point(170, 28), Size = new Size(650, 490), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        Controls.Add(_contentPanel);
    }

    private void SelectPage(int index)
    {
        if (_currentPage == index) return;
        _currentPage = index;
        for (int i = 0; i < _navButtons.Length; i++)
        {
            if (i == index)
            {
                _navButtons[i].BackColor = Color.FromArgb(0, 120, 215);
                _navButtons[i].ForeColor = Color.White;
                _navButtons[i].Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            }
            else
            {
                _navButtons[i].BackColor = Color.White;
                _navButtons[i].ForeColor = Color.FromArgb(50, 50, 50);
                _navButtons[i].Font = new Font("Segoe UI", 10F);
            }
        }
        _contentPanel.Controls.Clear();
        switch (index)
        {
            case 0: BuildBuildPage(); break;
            case 1: BuildPatchPage(); break;
            case 2: BuildUpdatePage(); break;
            case 3: BuildBcdPage(); break;
        }
    }

    private void BuildBuildPage()
    {
        var y = 20;
        AddPageTitle(_contentPanel, Lang.Get("TabBuild"), ref y);
        AddRow(_contentPanel, Lang.Get("ToolPath"), out _buildToolEdit, ref y, true);
        AddRow(_contentPanel, Lang.Get("OutputDir"), out _buildOutDirEdit, ref y, true);
        AddRow(_contentPanel, Lang.Get("FfuName"), out _buildFfuNameEdit, ref y, false);
        _buildFfuNameEdit.Text = "flash.ffu";
        AddRow(_contentPanel, Lang.Get("XmlPath"), out _buildXmlEdit, ref y, true);
        AddRow(_contentPanel, Lang.Get("PkgDir"), out _buildPkgEdit, ref y, true);
        AddComboRow(_contentPanel, Lang.Get("CpuType"), out _buildCpuCombo, ref y, new[] { "x86", "amd64", "arm", "arm64" }, 1);
    }

    private void BuildPatchPage()
    {
        var y = 20;
        AddPageTitle(_contentPanel, Lang.Get("TabPatch"), ref y);
        AddRow(_contentPanel, Lang.Get("ToolPath"), out _patchToolEdit, ref y, true);
        AddRow(_contentPanel, Lang.Get("FfuPath"), out _patchFfuEdit, ref y, true);
        AddComboRow(_contentPanel, Lang.Get("CpuType"), out _patchCpuCombo, ref y, new[] { "x86", "amd64", "arm", "arm64" }, 1);
        AddRow(_contentPanel, Lang.Get("DriverDir"), out _patchDriverEdit, ref y, true);
    }

    private void BuildUpdatePage()
    {
        var y = 20;
        AddPageTitle(_contentPanel, Lang.Get("TabUpdate"), ref y);
        AddRow(_contentPanel, Lang.Get("ToolPath"), out _updateToolEdit, ref y, true);
        AddRow(_contentPanel, Lang.Get("VhdPath"), out _updateVhdEdit, ref y, true);
        AddRow(_contentPanel, Lang.Get("CabDir"), out _updateCabEdit, ref y, true);
    }

    private void BuildBcdPage()
    {
        var y = 20;
        AddPageTitle(_contentPanel, Lang.Get("TabBcd"), ref y);
        AddRow(_contentPanel, Lang.Get("BcdPath"), out _bcdEdit, ref y, true);
        y += 10;
        _bcdDebug = AddCheck(_contentPanel, Lang.Get("Debug"), ref y);
        _bcdSerial = AddCheck(_contentPanel, Lang.Get("Serial"), ref y);
        _bcdPort = AddCheck(_contentPanel, Lang.Get("DebugPort"), ref y);
        _bcdBaud = AddCheck(_contentPanel, Lang.Get("BaudRate"), ref y);
        _bcdTestSign = AddCheck(_contentPanel, Lang.Get("TestSign"), ref y);
        _bcdNoInt = AddCheck(_contentPanel, Lang.Get("NoIntegrity"), ref y);
    }

    private void AddPageTitle(Control parent, string title, ref int y)
    {
        var lbl = new Label { Text = title, Location = new Point(25, y), Size = new Size(600, 30), Font = new Font("Segoe UI", 14F, FontStyle.Bold), ForeColor = Color.FromArgb(30, 30, 30) };
        parent.Controls.Add(lbl);
        var line = new Panel { Location = new Point(25, y + 35), Size = new Size(600, 2), BackColor = Color.FromArgb(0, 120, 215) };
        parent.Controls.Add(line);
        y += 55;
    }

    private void AddRow(Control parent, string label, out TextBox textBox, ref int y, bool withBrowse)
    {
        var lbl = new Label { Text = label, Location = new Point(25, y + 4), Size = new Size(120, 20), Font = new Font("Segoe UI", 9.5F), ForeColor = Color.FromArgb(60, 60, 60) };
        parent.Controls.Add(lbl);
        textBox = new TextBox { Location = new Point(150, y), Size = new Size(withBrowse ? 380 : 470, 24), Font = new Font("Segoe UI", 9.5F), BorderStyle = BorderStyle.FixedSingle };
        parent.Controls.Add(textBox);
        if (withBrowse)
        {
            var btn = new Button { Text = Lang.Get("Browse"), Location = new Point(535, y - 1), Size = new Size(80, 26), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9F), BackColor = Color.WhiteSmoke };
            btn.FlatAppearance.BorderSize = 1;
            var tb = textBox;
            btn.Click += (s, e) =>
            {
                if (label.Contains("XML") || label.Contains("FFU") || label.Contains("VHD") || label.Contains("BCD") || label.Contains("Tool"))
                {
                    var filter = label.Contains("XML") ? "XML Files|*.xml|All Files|*.*" :
                                 label.Contains("FFU") ? "FFU Files|*.ffu|All Files|*.*" :
                                 label.Contains("VHD") ? "VHD Files|*.vhd;*.vhdx|All Files|*.*" :
                                 label.Contains("BCD") ? "All Files|*.*" :
                                 "EXE/CMD Files|*.exe;*.cmd|All Files|*.*";
                    using var dlg = new OpenFileDialog { Filter = filter };
                    if (dlg.ShowDialog() == DialogResult.OK) tb.Text = dlg.FileName;
                }
                else
                {
                    using var dlg = new FolderBrowserDialog();
                    if (dlg.ShowDialog() == DialogResult.OK) tb.Text = dlg.SelectedPath;
                }
            };
            parent.Controls.Add(btn);
        }
        y += 38;
    }

    private void AddComboRow(Control parent, string label, out ComboBox combo, ref int y, string[] items, int selectedIndex)
    {
        var lbl = new Label { Text = label, Location = new Point(25, y + 4), Size = new Size(120, 20), Font = new Font("Segoe UI", 9.5F), ForeColor = Color.FromArgb(60, 60, 60) };
        parent.Controls.Add(lbl);
        combo = new ComboBox { Location = new Point(150, y), Size = new Size(200, 24), DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9.5F) };
        combo.Items.AddRange(items);
        combo.SelectedIndex = selectedIndex;
        parent.Controls.Add(combo);
        y += 38;
    }

    private CheckBox AddCheck(Control parent, string text, ref int y)
    {
        var chk = new CheckBox { Text = text, Location = new Point(150, y), Size = new Size(400, 22), Font = new Font("Segoe UI", 9.5F), ForeColor = Color.FromArgb(50, 50, 50) };
        parent.Controls.Add(chk);
        y += 30;
        return chk;
    }

    private void InitStatus()
    {
        _statusStrip = new StatusStrip { Location = new Point(0, 518), Size = new Size(820, 22) };
        _statusLabel = new ToolStripStatusLabel(Lang.Get("Ready")) { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _statusStrip.Items.Add(_statusLabel);
        Controls.Add(_statusStrip);
    }

    private void ApplyLanguage()
    {
        Text = Lang.Get("Title") + " - WSK Tools v1.0.4";
        _fileMenu.Text = Lang.Get("File");
        _langMenu.Text = Lang.Get("Language");
        _runBtn.Text = Lang.Get("Run");
        _statusLabel.Text = Lang.Get("Ready");
        if (_navTitle != null) _navTitle.Text = Lang.Get("NavTitle");
        if (_navButtons != null)
        {
            _navButtons[0].Text = Lang.Get("TabBuild");
            _navButtons[1].Text = Lang.Get("TabPatch");
            _navButtons[2].Text = Lang.Get("TabUpdate");
            _navButtons[3].Text = Lang.Get("TabBcd");
        }
        if (_currentPage >= 0) SelectPage(_currentPage);
    }

    private void ScanAdkVersions()
    {
        _adkVersionStrip.Items.Clear();
        var kitsRoot = Path.Combine(_adkRoot, "Windows Kits", "10");
        if (Directory.Exists(kitsRoot))
        {
            foreach (var dir in Directory.GetDirectories(kitsRoot))
            {
                var name = Path.GetFileName(dir);
                var binPath = Path.Combine(dir, "Tools", "bin", "i386");
                if (Directory.Exists(binPath))
                    _adkVersionStrip.Items.Add(name);
            }
        }
        if (_adkVersionStrip.Items.Count > 0)
            _adkVersionStrip.SelectedIndex = 0;
    }

    private void ApplyAdkVersion()
    {
        if (_adkVersionStrip.SelectedItem == null) return;
        var ver = _adkVersionStrip.SelectedItem.ToString();
        var binPath = Path.Combine(_adkRoot, "Windows Kits", "10", ver, "Tools", "bin", "i386");
        if (!Directory.Exists(binPath)) return;

        var imggen = Path.Combine(binPath, "imggen.cmd");
        var imageapp = Path.Combine(binPath, "imageapp.exe");
        var updateapp = Path.Combine(binPath, "UpdateApp.exe");

        if (File.Exists(imggen) && _buildToolEdit != null) _buildToolEdit.Text = imggen;
        if (File.Exists(imageapp) && _patchToolEdit != null) _patchToolEdit.Text = imageapp;
        if (File.Exists(updateapp) && _updateToolEdit != null) _updateToolEdit.Text = updateapp;
    }

    private void RunCommand()
    {
        if (_isRunning) return;
        string? cmd = null;
        string workDir = "";
        string? error = null;

        switch (_currentPage)
        {
            case 0: cmd = BuildImgGenCommand(out workDir, out error); break;
            case 1: cmd = BuildPatchCommand(out workDir, out error); break;
            case 2: cmd = BuildUpdateCommand(out workDir, out error); break;
            case 3: cmd = BuildBcdCommand(out workDir, out error); break;
        }

        if (string.IsNullOrEmpty(cmd))
        {
            MessageBox.Show(error ?? "Unknown error", Lang.Get("Failed"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _statusLabel.Text = Lang.Get("Failed");
            return;
        }
        ExecuteCommand(cmd, workDir);
    }

    private string? BuildImgGenCommand(out string workDir, out string? error)
    {
        workDir = ""; error = null;
        if (string.IsNullOrWhiteSpace(_buildToolEdit.Text)) { error = Lang.Get("SelectTool"); return null; }
        if (string.IsNullOrWhiteSpace(_buildOutDirEdit.Text)) { error = Lang.Get("SelectOutput"); return null; }
        if (string.IsNullOrWhiteSpace(_buildXmlEdit.Text)) { error = Lang.Get("SelectXml"); return null; }
        if (string.IsNullOrWhiteSpace(_buildPkgEdit.Text)) { error = Lang.Get("SelectPkg"); return null; }

        var outDir = _buildOutDirEdit.Text.Trim();
        if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

        var ffuName = string.IsNullOrWhiteSpace(_buildFfuNameEdit.Text) ? "flash.ffu" : _buildFfuNameEdit.Text.Trim();
        var outFile = Path.Combine(outDir, ffuName);
        workDir = Path.GetDirectoryName(_buildToolEdit.Text.Trim()) ?? "";
        var cpu = _buildCpuCombo.SelectedItem?.ToString() ?? "";

        var cmd = $"\"{_buildToolEdit.Text.Trim()}\" \"{outFile}\" \"{_buildXmlEdit.Text.Trim()}\" \"{_buildPkgEdit.Text.Trim()}\"";
        if (!string.IsNullOrEmpty(cpu)) cmd += $" {cpu}";
        return cmd;
    }

    private string? BuildPatchCommand(out string workDir, out string? error)
    {
        workDir = ""; error = null;
        if (string.IsNullOrWhiteSpace(_patchToolEdit.Text)) { error = Lang.Get("SelectTool"); return null; }
        if (string.IsNullOrWhiteSpace(_patchFfuEdit.Text)) { error = Lang.Get("SelectFfu"); return null; }
        if (string.IsNullOrWhiteSpace(_patchDriverEdit.Text)) { error = Lang.Get("SelectDriver"); return null; }
        if (_patchCpuCombo.SelectedIndex < 0) { error = Lang.Get("SelectCpu"); return null; }

        var cpu = _patchCpuCombo.SelectedItem.ToString()!;
        workDir = Path.GetDirectoryName(_patchToolEdit.Text.Trim()) ?? "";
        return $"\"{_patchToolEdit.Text.Trim()}\" \"{_patchFfuEdit.Text.Trim()}\" /CPUType:{cpu} /Patch /Drivers:\"{_patchDriverEdit.Text.Trim()}\"";
    }

    private string? BuildUpdateCommand(out string workDir, out string? error)
    {
        workDir = ""; error = null;
        if (string.IsNullOrWhiteSpace(_updateToolEdit.Text)) { error = Lang.Get("SelectTool"); return null; }
        if (string.IsNullOrWhiteSpace(_updateVhdEdit.Text)) { error = Lang.Get("SelectVhd"); return null; }
        if (string.IsNullOrWhiteSpace(_updateCabEdit.Text)) { error = Lang.Get("SelectCab"); return null; }

        workDir = Path.GetDirectoryName(_updateToolEdit.Text.Trim()) ?? "";
        return $"\"{_updateToolEdit.Text.Trim()}\" mountandinstall \"{_updateVhdEdit.Text.Trim()}\" \"{_updateCabEdit.Text.Trim()}\"";
    }

    private string? BuildBcdCommand(out string workDir, out string? error)
    {
        workDir = @"C:\Windows\System32"; error = null;
        if (string.IsNullOrWhiteSpace(_bcdEdit.Text)) { error = Lang.Get("SelectBcd"); return null; }

        var bcd = _bcdEdit.Text.Trim();
        var sb = new StringBuilder();
        if (_bcdDebug.Checked) sb.AppendLine($"bcdedit /store \"{bcd}\" /set {{default}} debug on");
        if (_bcdSerial.Checked) sb.AppendLine($"bcdedit /store \"{bcd}\" /set {{default}} debugtype serial");
        if (_bcdPort.Checked) sb.AppendLine($"bcdedit /store \"{bcd}\" /set {{default}} debugport 1");
        if (_bcdBaud.Checked) sb.AppendLine($"bcdedit /store \"{bcd}\" /set {{default}} baudrate 115200");
        if (_bcdTestSign.Checked) sb.AppendLine($"bcdedit /store \"{bcd}\" /set {{default}} testsigning on");
        if (_bcdNoInt.Checked) sb.AppendLine($"bcdedit /store \"{bcd}\" /set {{default}} nointegritychecks on");

        if (sb.Length == 0) { error = Lang.Get("SelectOption"); return null; }
        return sb.ToString();
    }

    private void ExecuteCommand(string cmd, string workDir)
    {
        _isRunning = true;
        _runBtn.Enabled = false;
        _statusLabel.Text = Lang.Get("Running");

        string? batchPath = null;
        try
        {
            batchPath = Path.Combine(Path.GetTempPath(), $"wcos_run_{Guid.NewGuid():N}.bat");
            var batchContent = $@"@echo off
chcp 936 >nul
title WCOS Stage Tool
cd /d ""{workDir}""
echo ============================================================
echo  WCOS Stage Tool - Command Execution
echo ============================================================
echo  WorkDir: {workDir}
echo  Command: {cmd}
echo ============================================================
echo.
{cmd}
echo.
echo ============================================================
echo  Execution finished. Exit code: %ERRORLEVEL%
echo  Window stays open. Type 'exit' to close.
echo ============================================================
";
            File.WriteAllText(batchPath, batchContent, Encoding.GetEncoding(936));

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/k \"" + batchPath + "\"",
                UseShellExecute = true,
                WorkingDirectory = workDir
            };

            Process.Start(psi);
            _statusLabel.Text = Lang.Get("Running") + " (console)";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = Lang.Get("Failed");
            MessageBox.Show(ex.Message, Lang.Get("Failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _isRunning = false;
            _runBtn.Enabled = true;
        }
    }

    private void ShowAbout()
    {
        using var dlg = new Form
        {
            Text = Lang.Get("About"),
            Size = new Size(420, 300),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.White
        };
        var lbl = new Label
        {
            Text = Lang.Get("AboutText"),
            Location = new Point(20, 20),
            Size = new Size(360, 180),
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = Color.FromArgb(40, 40, 40)
        };
        var warn = new Label
        {
            Text = Lang.Get("TestWarning"),
            Location = new Point(20, 200),
            Size = new Size(360, 30),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.Red
        };
        var btn = new Button { Text = Lang.Get("OK"), Location = new Point(160, 230), Size = new Size(80, 28), DialogResult = DialogResult.OK };
        dlg.Controls.AddRange(new Control[] { lbl, warn, btn });
        dlg.AcceptButton = btn;
        dlg.ShowDialog(this);
    }
}
