using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using DeviceLayoutToVhd;

namespace DeviceLayoutExchanger;

public class MainForm : Form
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    private TabControl _tabControl = null!;
    private TabPage _tabLayout = null!;
    private TabPage _tabDisk = null!;

    private ToolStrip _toolStrip = null!;
    private TreeView _treeView = null!;

    private ToolStrip _diskToolStrip = null!;
    private SplitContainer _diskSplit = null!;
    private ListView _diskListView = null!;
    private ListView _partitionListView = null!;
    private ListView _poolListView = null!;
    private Label _diskInfoLabel = null!;

    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _statusLabel = null!;

    private DeviceLayoutInfo? _currentLayout;
    private string? _currentXmlPath;

    public MainForm()
    {
        try
        {
            Text = "DeviceLayout Exchanger - WSK Tools v1.0.3";
            ClientSize = new Size(1000, 650);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.White;

            InitToolStrip();
            InitTreeView();
            InitDiskManagementPage();
            InitTabControl();
            InitStatusStrip();

            SuspendLayout();
            Controls.Add(_tabControl);
            Controls.Add(_statusStrip);
            ResumeLayout();

            Load += (s, e) =>
            {
                WindowState = FormWindowState.Normal;
                Show();
                Activate();
                BringToFront();
                if (Left < 0 || Top < 0 || Left > Screen.PrimaryScreen?.WorkingArea.Width)
                {
                    StartPosition = FormStartPosition.CenterScreen;
                }
            };
        }
        catch (Exception ex)
        {
            MessageBox.Show($"初始化失败: {ex.Message}\n\n{ex.StackTrace}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void InitTabControl()
    {
        _tabControl = new TabControl { Dock = DockStyle.Fill };

        _tabLayout = new TabPage("设备布局");
        _tabLayout.Controls.Add(_treeView);
        _tabLayout.Controls.Add(_toolStrip);

        _tabDisk = new TabPage("磁盘管理");
        _tabDisk.Controls.Add(_diskSplit);
        _tabDisk.Controls.Add(_diskToolStrip);

        _tabControl.TabPages.Add(_tabLayout);
        _tabControl.TabPages.Add(_tabDisk);

        _tabControl.SelectedIndexChanged += (s, e) =>
        {
            if (_tabControl.SelectedTab == _tabDisk)
            {
                RefreshAllDiskInfo();
            }
        };
    }

    private void InitToolStrip()
    {
        _toolStrip = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            Dock = DockStyle.Top,
            BackColor = Color.WhiteSmoke
        };

        var btnOpenXml = new ToolStripButton("打开 XML") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btnOpenXml.Click += (s, e) => OpenXml();

        var btnOpenCab = new ToolStripButton("打开 CAB") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btnOpenCab.Click += (s, e) => OpenCab();

        var sep1 = new ToolStripSeparator();

        var lblFormat = new ToolStripLabel("格式:");
        var cmbFormat = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
        cmbFormat.Items.AddRange(new object[] { "VHDX", "VHD" });
        cmbFormat.SelectedIndex = 0;
        cmbFormat.Tag = cmbFormat;

        var btnCreate = new ToolStripButton("创建虚拟磁盘")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            BackColor = Color.FromArgb(0, 120, 215),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };
        btnCreate.Click += (s, e) => CreateWithGeneratorV2();

        var sep2 = new ToolStripSeparator();

        var btnHelp = new ToolStripButton("帮助") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btnHelp.Click += (s, e) => ShowHelp();

        var btnAbout = new ToolStripButton("关于") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btnAbout.Click += (s, e) => ShowAbout();

        _toolStrip.Items.AddRange(new ToolStripItem[] {
            btnOpenXml, btnOpenCab, sep1, lblFormat, cmbFormat, btnCreate, sep2, btnHelp, btnAbout
        });
    }

    private void InitTreeView()
    {
        _treeView = new TreeView
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9.5F),
            ShowLines = true,
            ShowPlusMinus = true
        };
        _treeView.AfterSelect += (s, e) => ShowDetail(e.Node);
    }

    private void InitStatusStrip()
    {
        _statusStrip = new StatusStrip { Dock = DockStyle.Bottom };
        _statusLabel = new ToolStripStatusLabel("就绪") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _statusStrip.Items.Add(_statusLabel);
    }

    private void OpenXml()
    {
        using var dlg = new OpenFileDialog { Filter = "XML Files|*.xml|All Files|*.*", Title = "选择设备布局 XML" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        LoadXml(dlg.FileName);
    }

    private void LoadXml(string path)
    {
        try
        {
            _currentLayout = DeviceLayoutParser.Parse(path);
            _currentXmlPath = path;
            PopulateTree();
            _statusLabel.Text = $"已加载: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"解析 XML 失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenCab()
    {
        using var dlg = new OpenFileDialog { Filter = "CAB Files|*.cab|All Files|*.*", Title = "选择 CAB 包" };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        var tempDir = Path.Combine(Path.GetTempPath(), $"dlx_cab_{Guid.NewGuid():N}");
        // Persistent directory for extracted XML (survives after temp dir cleanup)
        var persistDir = Path.Combine(Application.StartupPath, "cab_extracted");
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(persistDir);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "expand.exe",
                Arguments = $"-F:* \"{dlg.FileName}\" \"{tempDir}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit(30000);

            var xmlFiles = Directory.GetFiles(tempDir, "*.xml", SearchOption.AllDirectories);
            if (xmlFiles.Length == 0)
            {
                MessageBox.Show("CAB 中未找到 XML 文件", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string selectedXml = xmlFiles[0];
            if (xmlFiles.Length > 1)
            {
                using var selectDlg = new Form { Text = "选择 XML", Size = new Size(500, 400), StartPosition = FormStartPosition.CenterParent };
                var listBox = new ListBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9.5F) };
                foreach (var f in xmlFiles) listBox.Items.Add(f.Replace(tempDir + "\\", ""));
                var btnOk = new Button { Text = "确定", Dock = DockStyle.Bottom, Height = 35 };
                btnOk.Click += (s, e) => { selectDlg.DialogResult = DialogResult.OK; selectDlg.Close(); };
                selectDlg.Controls.Add(listBox);
                selectDlg.Controls.Add(btnOk);
                if (selectDlg.ShowDialog(this) == DialogResult.OK && listBox.SelectedIndex >= 0)
                {
                    selectedXml = xmlFiles[listBox.SelectedIndex];
                }
                else return;
            }

            // Copy XML to persistent location (so it survives after temp dir cleanup)
            string cabName = Path.GetFileNameWithoutExtension(dlg.FileName);
            string persistXml = Path.Combine(persistDir, $"{cabName}_{DateTime.Now:yyyyMMdd_HHmmss}.xml");
            File.Copy(selectedXml, persistXml, true);

            LoadXml(persistXml);
            _statusLabel.Text = $"已从 CAB 加载: {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"读取 CAB 失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    private void PopulateTree()
    {
        _treeView.BeginUpdate();
        _treeView.Nodes.Clear();
        if (_currentLayout == null) { _treeView.EndUpdate(); return; }

        var rootNode = _treeView.Nodes.Add($"设备布局 (扇区大小: {_currentLayout.SectorSize})");

        foreach (var store in _currentLayout.Stores)
        {
            var storeSize = store.SizeInSectors > 0 ? FormatSize(store.SizeInSectors * _currentLayout.SectorSize) : "大小未指定";
            var storeLabel = !string.IsNullOrEmpty(store.StoreType) ? store.StoreType : store.Id;
            var storeNode = rootNode.Nodes.Add($"存储: {storeLabel} ({storeSize})");
            storeNode.Tag = store;
            int partIndex = 1;
            foreach (var part in store.Partitions)
            {
                var partName = !string.IsNullOrEmpty(part.Name) ? part.Name : $"分区 {partIndex}";
                var partNode = storeNode.Nodes.Add($"{partName} ({FormatPartitionSize(part)})");
                partNode.Tag = part;
                partIndex++;
            }
            storeNode.Expand();
        }

        foreach (var pool in _currentLayout.StoragePools)
        {
            var poolNode = rootNode.Nodes.Add($"存储池: {pool.Name} ({pool.Stores.Count} 个存储)");
            poolNode.Tag = pool;
            foreach (var store in pool.Stores)
            {
                var storeSize = store.SizeInSectors > 0 ? FormatSize(store.SizeInSectors * _currentLayout.SectorSize) : "大小未指定";
                var storeLabel = !string.IsNullOrEmpty(store.StoreType) ? store.StoreType : store.Id;
                var storeNode = poolNode.Nodes.Add($"存储: {storeLabel} ({storeSize})");
                storeNode.Tag = store;
                int partIndex = 1;
                foreach (var part in store.Partitions)
                {
                    var partName = !string.IsNullOrEmpty(part.Name) ? part.Name : $"分区 {partIndex}";
                    var partNode = storeNode.Nodes.Add($"{partName} ({FormatPartitionSize(part)})");
                    partNode.Tag = part;
                    partIndex++;
                }
                storeNode.Expand();
            }
            poolNode.Expand();
        }

        rootNode.Expand();
        _treeView.EndUpdate();
    }

    private string FormatPartitionSize(PartitionInfo part)
    {
        var sectorSize = _currentLayout?.SectorSize ?? 512;
        if (part.TotalSectors > 0)
        {
            var size = FormatSize(part.TotalSectors * sectorSize);
            if (part.UseAllSpace) return $"{size} + 剩余空间";
            return size;
        }
        if (part.UseAllSpace) return "使用剩余空间";
        if (part.MinFreeSectors > 0) return $"最小空闲: {FormatSize(part.MinFreeSectors * sectorSize)}";
        return "大小未指定";
    }

    private void ShowDetail(TreeNode? node)
    {
        if (node?.Tag == null)
        {
            _statusLabel.Text = _currentXmlPath != null ? $"已加载: {Path.GetFileName(_currentXmlPath)}" : "就绪";
            return;
        }

        if (node.Tag is StoreInfo store)
        {
            _statusLabel.Text = $"存储: {store.Id} | 类型: {store.StoreType} | 大小: {FormatSize(store.SizeInSectors * (_currentLayout?.SectorSize ?? 512))} | 分区数: {store.Partitions.Count}";
        }
        else if (node.Tag is PartitionInfo part)
        {
            _statusLabel.Text = $"分区: {part.Name} | 类型: {part.Type} | 文件系统: {part.FileSystem} | 大小: {FormatSize(part.TotalSectors * (_currentLayout?.SectorSize ?? 512))}";
        }
        else if (node.Tag is StoragePoolInfo pool)
        {
            _statusLabel.Text = $"存储池: {pool.Name} | 存储数: {pool.Stores.Count}";
        }
    }

    private void CreateWithGeneratorV2()
    {
        if (_currentLayout == null || string.IsNullOrEmpty(_currentXmlPath))
        {
            MessageBox.Show("请先加载设备布局 XML", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string generatorPath = Path.Combine(Application.StartupPath, "DeviceLayoutGeneratorV2.exe");
        if (!File.Exists(generatorPath))
            generatorPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DeviceLayoutGeneratorV2.exe");
        if (!File.Exists(generatorPath))
        {
            MessageBox.Show("未找到 DeviceLayoutGeneratorV2.exe，请确保它与本程序在同一目录", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Filter = "VHD Files|*.vhd|VHDX Files|*.vhdx|All Files|*.*",
            Title = "保存虚拟磁盘",
            FileName = "output.vhd"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string outputPath = dlg.FileName;

        // Create temp log file
        string logPath = Path.Combine(Path.GetTempPath(), $"DeviceLayoutGenerator_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        // Show running window via cmd.exe, redirect output to log file
        string cmdArgs = $"/c \"\"{generatorPath}\" \"{_currentXmlPath}\" \"{outputPath}\" > \"{logPath}\" 2>&1\"";

        _statusLabel.Text = "正在创建虚拟磁盘...";
        Application.DoEvents();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = cmdArgs,
                UseShellExecute = false,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            };

            var proc = Process.Start(psi);
            proc!.WaitForExit();
            int exitCode = proc.ExitCode;

            // Ensure VHD is dismounted
            try
            {
                var dismountPsi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"Dismount-VHD -Path '{outputPath}' -ErrorAction SilentlyContinue\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(dismountPsi)?.WaitForExit(5000);
            }
            catch { }

            // Check result
            bool success = (exitCode == 0 && File.Exists(outputPath));
            string resultMsg;

            if (success)
            {
                var fi = new FileInfo(outputPath);
                resultMsg = $"=== 创建成功 ==={Environment.NewLine}输出: {outputPath}{Environment.NewLine}大小: {fi.Length / 1048576.0:F2} MB";
                _statusLabel.Text = "虚拟磁盘创建成功";
            }
            else
            {
                // Delete failed output file if exists
                if (File.Exists(outputPath))
                {
                    try { File.Delete(outputPath); } catch { }
                }
                resultMsg = $"=== 创建失败 (退出码: {exitCode}) ==={Environment.NewLine}输出文件已删除";
                _statusLabel.Text = "虚拟磁盘创建失败";
            }

            // Show log window
            ShowLogWindow(logPath, resultMsg, success);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动 DeviceLayoutGeneratorV2 失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLabel.Text = "启动失败";
        }
    }

    private void ShowLogWindow(string logPath, string resultMsg, bool success)
    {
        using var logForm = new Form
        {
            Text = success ? "创建日志 - 成功" : "创建日志 - 失败",
            Size = new Size(800, 600),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable,
            MaximizeBox = true,
            MinimizeBox = false,
            BackColor = Color.White
        };

        var lblResult = new Label
        {
            Text = resultMsg,
            Dock = DockStyle.Top,
            Height = 60,
            Padding = new Padding(10),
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = success ? Color.Green : Color.Red,
            BackColor = Color.FromArgb(245, 245, 245)
        };

        var txtLog = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Consolas", 9F),
            BackColor = Color.Black,
            ForeColor = Color.LimeGreen,
            WordWrap = false
        };

        // Load log content
        try
        {
            if (File.Exists(logPath))
            {
                string logContent = File.ReadAllText(logPath);
                txtLog.Text = logContent;
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            }
            else
            {
                txtLog.Text = "(日志文件不存在)";
            }
        }
        catch (Exception ex)
        {
            txtLog.Text = $"(读取日志失败: {ex.Message})";
        }

        var panelBottom = new Panel { Dock = DockStyle.Bottom, Height = 45 };
        var btnClose = new Button
        {
            Text = "关闭",
            Location = new Point(680, 8),
            Size = new Size(100, 30),
            DialogResult = DialogResult.OK
        };
        var btnOpenLog = new Button
        {
            Text = "打开日志文件",
            Location = new Point(10, 8),
            Size = new Size(120, 30)
        };
        btnOpenLog.Click += (s, e) =>
        {
            try
            {
                if (File.Exists(logPath))
                    Process.Start(new ProcessStartInfo("notepad.exe", logPath) { UseShellExecute = true });
            }
            catch { }
        };
        panelBottom.Controls.AddRange(new Control[] { btnClose, btnOpenLog });

        logForm.Controls.Add(txtLog);
        logForm.Controls.Add(lblResult);
        logForm.Controls.Add(panelBottom);
        logForm.AcceptButton = btnClose;

        logForm.ShowDialog(this);
    }

    private void AutoCreate(VhdFormat format)
    {
        if (_currentLayout == null)
        {
            MessageBox.Show("请先加载设备布局 XML", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_currentLayout.StoragePools.Count > 0)
        {
            _statusLabel.Text = "检测到存储池布局，自动创建存储池...";
            Application.DoEvents();
            CreateAndExecuteStoragePool(format);
        }
        else if (_currentLayout.Stores.Count > 0)
        {
            _statusLabel.Text = "检测到多磁盘布局，为每个 Store 创建独立虚拟磁盘...";
            Application.DoEvents();
            CreateMultipleDisks(format);
        }
        else
        {
            MessageBox.Show("设备布局中未找到 Stores 或 StoragePools", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void CreateMultipleDisks(VhdFormat format)
    {
        if (_currentLayout == null || _currentLayout.Stores.Count == 0)
        {
            MessageBox.Show("没有可创建的 Store", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new FolderBrowserDialog { Description = "选择输出目录（将为每个 Store 创建独立的虚拟磁盘文件）" };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            var ext = format == VhdFormat.Vhdx ? "vhdx" : "vhd";
            int successCount = 0;

            foreach (var store in _currentLayout.Stores)
            {
                var storeName = string.IsNullOrEmpty(store.StoreType) ? "disk" : store.StoreType;
                var outputPath = Path.Combine(dlg.SelectedPath, $"VirtualDisk{storeName}.{ext}");

                _statusLabel.Text = $"正在创建: {storeName}...";
                Application.DoEvents();

                var singleLayout = new DeviceLayoutInfo
                {
                    SectorSize = _currentLayout.SectorSize,
                    ChunkSize = _currentLayout.ChunkSize,
                    DefaultPartitionByteAlignment = _currentLayout.DefaultPartitionByteAlignment,
                    Stores = new List<StoreInfo> { store }
                };

                VhdCreator.CreateSingleDisk(singleLayout, outputPath, format);

                if (File.Exists(outputPath))
                {
                    successCount++;
                }
            }

            _statusLabel.Text = $"创建完成: {successCount}/{_currentLayout.Stores.Count} 个磁盘";
            MessageBox.Show($"虚拟磁盘创建完成!\n\n输出目录: {dlg.SelectedPath}\n成功: {successCount}/{_currentLayout.Stores.Count} 个磁盘", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "创建失败";
            MessageBox.Show($"创建虚拟磁盘失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CreateVhd(VhdFormat format)
    {
        if (_currentLayout == null)
        {
            MessageBox.Show("请先加载设备布局 XML", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var ext = format == VhdFormat.Vhdx ? "vhdx" : "vhd";
        using var dlg = new SaveFileDialog { Filter = $"{ext.ToUpper()} Files|*.{ext}|All Files|*.*", Title = "保存虚拟磁盘", FileName = $"disk.{ext}" };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            _statusLabel.Text = "正在创建虚拟磁盘...";
            Application.DoEvents();

            VhdCreator.CreateSingleDisk(_currentLayout, dlg.FileName, format);

            if (File.Exists(dlg.FileName))
            {
                var fi = new FileInfo(dlg.FileName);
                _statusLabel.Text = $"创建完成: {Path.GetFileName(dlg.FileName)} ({FormatSize(fi.Length)})";
                MessageBox.Show($"虚拟磁盘创建成功!\n\n路径: {dlg.FileName}\n大小: {FormatSize(fi.Length)}", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                _statusLabel.Text = "创建失败";
                MessageBox.Show("虚拟磁盘创建失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "创建失败";
            MessageBox.Show($"创建虚拟磁盘失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CreateStoragePoolDisks(VhdFormat format)
    {
        if (_currentLayout == null)
        {
            MessageBox.Show("请先加载设备布局 XML", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_currentLayout.StoragePools.Count == 0)
        {
            MessageBox.Show("该设备布局不包含存储池", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new FolderBrowserDialog { Description = "选择存储池虚拟磁盘输出目录" };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            _statusLabel.Text = "正在创建存储池虚拟磁盘...";
            Application.DoEvents();

            var results = VhdCreator.CreateStoragePoolDisks(_currentLayout, dlg.SelectedPath, format);

            if (results.Count > 0)
            {
                var msg = $"存储池虚拟磁盘创建完成!\n\n输出目录: {dlg.SelectedPath}\n创建数量: {results.Count} 个\n\n";
                foreach (var r in results)
                {
                    var fi = new FileInfo(r);
                    msg += $"  - {Path.GetFileName(r)} ({FormatSize(fi.Length)})\n";
                }
                _statusLabel.Text = $"存储池磁盘创建完成: {results.Count} 个";
                MessageBox.Show(msg, "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                _statusLabel.Text = "创建失败";
                MessageBox.Show("存储池虚拟磁盘创建失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "创建失败";
            MessageBox.Show($"创建存储池虚拟磁盘失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CreateStoragePoolWithScript(VhdFormat format)
    {
        if (_currentLayout == null)
        {
            MessageBox.Show("请先加载设备布局 XML", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_currentLayout.StoragePools.Count == 0)
        {
            MessageBox.Show("该设备布局不包含存储池", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new FolderBrowserDialog { Description = "选择存储池输出目录（将创建 VHD 和 PowerShell 脚本）" };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            _statusLabel.Text = "正在生成存储池创建脚本...";
            Application.DoEvents();

            var pool = _currentLayout.StoragePools[0];
            var ext = format == VhdFormat.Vhdx ? "vhdx" : "vhd";
            var vhdPath = Path.Combine(dlg.SelectedPath, $"StoragePoolMember.{ext}");
            var scriptPath = Path.Combine(dlg.SelectedPath, "Create-StoragePool.ps1");
            var sectorSize = _currentLayout.SectorSize > 0 ? _currentLayout.SectorSize : 512;

            VhdCreator.GenerateStoragePoolScript(_currentLayout, vhdPath, scriptPath);

            var msg = $"存储池创建脚本生成完成!\n\n输出目录: {dlg.SelectedPath}\n\n文件:\n" +
                      $"  - Create-StoragePool.ps1\n\n" +
                      $"VHD 文件将在脚本执行时自动创建。\n\n" +
                      $"使用方法:\n" +
                      $"  1. 右键点击 Create-StoragePool.ps1，选择'使用 PowerShell 运行'\n" +
                      $"  2. 或以管理员身份运行 PowerShell，执行: .\\Create-StoragePool.ps1\n\n" +
                      $"脚本将自动:\n" +
                      $"  - 使用 New-VHD 创建 VHD\n" +
                      $"  - 挂载 VHD\n" +
                      $"  - 创建存储池 ({pool.Name})\n" +
                      $"  - 创建 {pool.Stores.Count} 个虚拟磁盘\n" +
                      $"  - 分区和格式化";

            _statusLabel.Text = "存储池脚本生成完成";
            MessageBox.Show(msg, "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "创建失败";
            MessageBox.Show($"创建存储池文件失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CreateWSKStoragePoolVhd(VhdFormat format)
    {
        if (_currentLayout == null)
        {
            MessageBox.Show("请先加载设备布局 XML", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_currentLayout.StoragePools.Count == 0)
        {
            MessageBox.Show("该设备布局不包含存储池", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Filter = format == VhdFormat.Vhdx ? "VHDX|*.vhdx" : "VHD|*.vhd",
            Title = "保存 WSK 存储池虚拟磁盘",
            FileName = "StoragePool.vhdx"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            _statusLabel.Text = "正在创建 WSK 存储池 VHD...";
            Application.DoEvents();

            var vhdPath = VhdCreator.CreateWSKStoragePoolVhd(_currentLayout, dlg.FileName, format);

            if (!string.IsNullOrEmpty(vhdPath))
            {
                var scriptPath = Path.Combine(Path.GetDirectoryName(vhdPath) ?? ".", "Create-WSKStoragePool.ps1");
                VhdCreator.GenerateWSKStoragePoolScript(_currentLayout, vhdPath, scriptPath);

                var fi = new FileInfo(vhdPath);
                var msg = $"WSK 存储池 VHD 创建完成!\n\n文件: {vhdPath}\n大小: {FormatSize(fi.Length)}\n\n" +
                          $"分区布局 (符合 WSK GPT_SPACES 标准):\n" +
                          $"  1. OPP (OEM Platform Partition)\n" +
                          $"  2. BS_EFIESP (FAT, EFI 系统分区)\n" +
                          $"  3. OSPool (类型 5708A6E0, Storage Spaces 成员)\n\n" +
                          $"存储池: {_currentLayout.StoragePools[0].Name} ({_currentLayout.StoragePools[0].Stores.Count} 个虚拟磁盘)\n\n" +
                          $"已生成脚本: {scriptPath}\n\n" +
                          $"使用方法:\n" +
                          $"  以管理员身份运行 PowerShell, 执行:\n" +
                          $"  .\\Create-WSKStoragePool.ps1\n\n" +
                          $"脚本将自动:\n" +
                          $"  - 挂载 VHD\n" +
                          $"  - 在 OSPool 分区上创建 Storage Spaces 存储池\n" +
                          $"  - 创建 {_currentLayout.StoragePools[0].Stores.Count} 个虚拟磁盘\n" +
                          $"  - 分区和格式化 (NTFS/FAT32)";

                _statusLabel.Text = "WSK 存储池 VHD 创建完成";
                MessageBox.Show(msg, "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                _statusLabel.Text = "创建失败";
                MessageBox.Show("WSK 存储池 VHD 创建失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "创建失败";
            MessageBox.Show($"创建 WSK 存储池 VHD 失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CreateAndExecuteStoragePool(VhdFormat format)
    {
        if (_currentLayout == null)
        {
            MessageBox.Show("请先加载设备布局 XML", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_currentLayout.StoragePools.Count == 0)
        {
            MessageBox.Show("该设备布局不包含存储池", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new FolderBrowserDialog { Description = "选择存储池输出目录（将创建 VHD 并自动执行创建存储池）" };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            _statusLabel.Text = "正在生成存储池创建脚本...";
            Application.DoEvents();

            var ext = format == VhdFormat.Vhdx ? "vhdx" : "vhd";
            var vhdPath = Path.Combine(dlg.SelectedPath, $"StoragePoolMember.{ext}");
            var scriptPath = Path.Combine(dlg.SelectedPath, "Create-StoragePool.ps1");
            var pool = _currentLayout.StoragePools[0];
            var poolName = string.IsNullOrEmpty(pool.Name) ? "OSPool" : pool.Name;
            var sectorSize = _currentLayout.SectorSize > 0 ? _currentLayout.SectorSize : 512;
            var helperExe = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? ".", "StoragePoolHelper.exe");

            // Calculate front partitions total size
            var physicalStore = _currentLayout.Stores.FirstOrDefault();
            long frontTotalSize = 0;
            var frontParts = new System.Collections.Generic.List<(string Name, string Type, long Size, string FileSystem)>();
            if (physicalStore != null)
            {
                foreach (var part in physicalStore.Partitions)
                {
                    if (part.UseAllSpace) continue;
                    var partSize = part.TotalSectors * sectorSize;
                    if (partSize <= 0) continue;
                    frontTotalSize += partSize;
                    frontParts.Add((part.Name, part.Type, partSize, part.FileSystem));
                }
            }

            // Calculate pool size
            long poolTotalSize = 0;
            foreach (var store in pool.Stores)
            {
                var sz = store.SizeInSectors * sectorSize;
                if (sz <= 0) sz = 2L * 1024 * 1024 * 1024;
                poolTotalSize += sz;
            }
            poolTotalSize = (long)(poolTotalSize * 1.15);
            poolTotalSize = (poolTotalSize + 511) / 512 * 512; // Align to 512-byte sector
            long diskSize = frontTotalSize + poolTotalSize + 64L * 1024 * 1024;
            diskSize = (diskSize + 511) / 512 * 512; // Align to 512-byte sector
            if (diskSize < 4L * 1024 * 1024 * 1024) diskSize = 4L * 1024 * 1024 * 1024;

            // Generate space arguments for helper
            var spaceArgs = new System.Collections.Generic.List<string>();
            foreach (var store in pool.Stores)
            {
                var spaceName = !string.IsNullOrEmpty(store.StoreType) ? store.StoreType : "VirtualDisk";
                var spaceSize = store.SizeInSectors * sectorSize;
                if (spaceSize <= 0) spaceSize = 2L * 1024 * 1024 * 1024;
                spaceArgs.Add($"\"{spaceName}\" {spaceSize}");
            }

            // Generate PowerShell script with backup/restore flow (correct partition order)
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("#Requires -RunAsAdministrator");
            sb.AppendLine("# DeviceLayout_Exchanger - Storage Pool Creation Script (v3 - Backup/Restore)");
            sb.AppendLine("# Flow: create pool on blank disk -> backup pool data -> remove pool -> create front partitions -> create OSPool -> restore data");
            sb.AppendLine("$ErrorActionPreference = 'Continue'");
            sb.AppendLine("");
            sb.AppendLine($"$vhdPath = '{vhdPath}'");
            sb.AppendLine($"$poolName = '{poolName}'");
            sb.AppendLine($"$helperExe = '{helperExe}'");
            sb.AppendLine($"$diskSize = {diskSize}");
            sb.AppendLine("$backupFile = Join-Path $env:TEMP 'ospool_backup.bin'");
            sb.AppendLine("");
            sb.AppendLine("Write-Host '=== Dismount existing VHD if mounted ===' -ForegroundColor Cyan");
            sb.AppendLine("Dismount-VHD -Path $vhdPath -ErrorAction SilentlyContinue");
            sb.AppendLine("Start-Sleep -Seconds 2");
            sb.AppendLine("if (Test-Path $vhdPath) { Remove-Item $vhdPath -Force -ErrorAction SilentlyContinue }");
            sb.AppendLine("if (Test-Path $backupFile) { Remove-Item $backupFile -Force -ErrorAction SilentlyContinue }");
            sb.AppendLine("Start-Sleep -Milliseconds 500");
            sb.AppendLine("");
            sb.AppendLine("Write-Host '=== Creating VHD ===' -ForegroundColor Cyan");
            sb.AppendLine("Write-Host \"  Path: $vhdPath\"");
            sb.AppendLine("Write-Host \"  Size: $($diskSize/1GB) GB\"");
            sb.AppendLine("$vhd = New-VHD -Path $vhdPath -SizeBytes $diskSize -Dynamic");
            sb.AppendLine("if (-not $vhd) { Write-Host 'ERROR: Failed to create VHD' -ForegroundColor Red; exit 1 }");
            sb.AppendLine("Write-Host 'VHD created.'");
            sb.AppendLine("");
            sb.AppendLine("Write-Host '=== Mounting VHD ===' -ForegroundColor Cyan");
            sb.AppendLine("Mount-VHD -Path $vhdPath -PassThru | Out-Null");
            sb.AppendLine("if (-not $?) { Write-Host 'ERROR: Failed to mount VHD' -ForegroundColor Red; exit 1 }");
            sb.AppendLine("Start-Sleep -Seconds 3");
            sb.AppendLine("$vhdName = [System.IO.Path]::GetFileName($vhdPath)");
            sb.AppendLine("$disk = Get-Disk | Where-Object { $_.Location -like \"*$vhdName*\" } | Select-Object -First 1");
            sb.AppendLine("if (-not $disk) { Write-Host \"ERROR: VHD disk not found (name=$vhdName)\" -ForegroundColor Red; Dismount-VHD -Path $vhdPath -ErrorAction SilentlyContinue; exit 1 }");
            sb.AppendLine("$diskNum = $disk.Number");
            sb.AppendLine("Write-Host \"Found disk: $diskNum ($($disk.Location))\"");
            sb.AppendLine("");
            sb.AppendLine("Write-Host '=== Clearing disk (ensure blank) ===' -ForegroundColor Cyan");
            sb.AppendLine("Clear-Disk -Number $diskNum -RemoveData -RemoveOEM -Confirm:$false -ErrorAction SilentlyContinue");
            sb.AppendLine("Start-Sleep -Seconds 1");
            sb.AppendLine("");
            sb.AppendLine("Write-Host '=== Initializing disk ===' -ForegroundColor Cyan");
            sb.AppendLine("Initialize-Disk -Number $diskNum -PartitionStyle GPT -Confirm:$false");
            sb.AppendLine("Start-Sleep -Seconds 1");
            sb.AppendLine("Write-Host 'Disk initialized as GPT.'");
            sb.AppendLine("");
            sb.AppendLine("Write-Host '=== Creating storage pool on blank disk (via 32-bit helper) ===' -ForegroundColor Cyan");
            sb.AppendLine("$beforeDisks = Get-Disk | Select-Object -ExpandProperty Number");
            sb.AppendLine($"& $helperExe $diskNum $poolName {pool.Stores.Count} {string.Join(" ", spaceArgs)}");
            sb.AppendLine("if ($LASTEXITCODE -ne 0) { Write-Host \"ERROR: StoragePoolHelper failed with exit code $LASTEXITCODE\" -ForegroundColor Red; Dismount-VHD -Path $vhdPath -ErrorAction SilentlyContinue; exit 1 }");
            sb.AppendLine("Write-Host 'Storage pool created successfully.'");
            sb.AppendLine("Start-Sleep -Seconds 5");
            sb.AppendLine("");
            sb.AppendLine("Write-Host '=== Initializing pool virtual disks ===' -ForegroundColor Cyan");
            sb.AppendLine("$afterDisks = Get-Disk | Select-Object -ExpandProperty Number");
            sb.AppendLine("$newDisks = $afterDisks | Where-Object { $beforeDisks -notcontains $_ }");
            sb.AppendLine("Write-Host \"Found $($newDisks.Count) new virtual disk(s)\"");
            sb.AppendLine("$vdIdx = 0");
            sb.AppendLine("foreach ($vdNum in $newDisks) {");
            sb.AppendLine("    $vdIdx++");
            sb.AppendLine("    Write-Host \"  Initializing virtual disk $vdIdx (Disk $vdNum)...\"");
            sb.AppendLine("    try {");
            sb.AppendLine("        Initialize-Disk -Number $vdNum -PartitionStyle GPT -Confirm:$false -ErrorAction Stop");
            sb.AppendLine("        Start-Sleep -Milliseconds 500");
            sb.AppendLine("        $vdPart = New-Partition -DiskNumber $vdNum -UseMaximumSize");
            sb.AppendLine("        try { Format-Volume -Partition $vdPart -FileSystem NTFS -NewFileSystemLabel \"VirtualDisk$vdIdx\" -Confirm:$false -Force -ErrorAction Stop | Out-Null } catch { Write-Host \"    WARNING: Format failed: $($_.Exception.Message)\" -ForegroundColor Yellow }");
            sb.AppendLine("    } catch { Write-Host \"    WARNING: Init failed: $($_.Exception.Message)\" -ForegroundColor Yellow }");
            sb.AppendLine("}");
            sb.AppendLine("Start-Sleep -Seconds 2");
            sb.AppendLine("");
            sb.AppendLine("Write-Host '=== Finding pool partition ===' -ForegroundColor Cyan");
            sb.AppendLine("[int]$poolPartNum = 2");
            sb.AppendLine("$parts = Get-Partition -DiskNumber $diskNum");
            sb.AppendLine("foreach ($p in $parts) { if ($p.Type -eq 'Unknown') { $poolPartNum = [int]$p.PartitionNumber; break } }");
            sb.AppendLine("Write-Host \"Pool partition number: $poolPartNum\"");
            sb.AppendLine("$poolPart = Get-Partition -DiskNumber $diskNum -PartitionNumber $poolPartNum");
            sb.AppendLine("[long]$poolSize = [long]$poolPart.Size");
            sb.AppendLine("Write-Host \"Pool size: $($poolSize/1MB)MB\"");
            sb.AppendLine("");
            sb.AppendLine("Write-Host '=== Backing up pool partition data ===' -ForegroundColor Cyan");
            sb.AppendLine("$srcPath = \"\\\\?\\GLOBALROOT\\Device\\Harddisk$diskNum\\Partition$poolPartNum\"");
            sb.AppendLine("Write-Host \"Source: $srcPath\"");
            sb.AppendLine("[long]$chunkSize = 64MB");
            sb.AppendLine("$buf = New-Object byte[] $chunkSize");
            sb.AppendLine("$srcFs = New-Object System.IO.FileStream($srcPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)");
            sb.AppendLine("$dstFs = [System.IO.File]::Create($backupFile)");
            sb.AppendLine("[long]$remaining = $poolSize");
            sb.AppendLine("while ($remaining -gt 0) { [long]$toRead = [Math]::Min($chunkSize, $remaining); $read = $srcFs.Read($buf, 0, [int]$toRead); if ($read -le 0) { break }; $dstFs.Write($buf, 0, $read); $remaining -= $read }");
            sb.AppendLine("$srcFs.Close(); $dstFs.Close()");
            sb.AppendLine("[long]$backupSize = [long](Get-Item $backupFile).Length");
            sb.AppendLine("Write-Host \"Backup: $($backupSize/1MB)MB\"");
            sb.AppendLine("");
            sb.AppendLine("Write-Host '=== Removing pool partition ===' -ForegroundColor Cyan");
            sb.AppendLine("Remove-Partition -DiskNumber $diskNum -PartitionNumber $poolPartNum -Confirm:$false");
            sb.AppendLine("Start-Sleep -Milliseconds 500");
            sb.AppendLine("");
            sb.AppendLine("Write-Host '=== Creating front partitions ===' -ForegroundColor Cyan");
            int fpIdx = 0;
            foreach (var fp in frontParts)
            {
                fpIdx++;
                var partType = string.IsNullOrEmpty(fp.Type) ? "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7" : fp.Type.Trim('{', '}');
                var partName = string.IsNullOrEmpty(fp.Name) ? $"Partition{fpIdx}" : fp.Name;
                sb.AppendLine($"# Front partition {fpIdx}: {partName}");
                sb.AppendLine($"$p{fpIdx} = New-Partition -DiskNumber $diskNum -Size {fp.Size}");
                sb.AppendLine($"Set-Partition -DiskNumber $diskNum -PartitionNumber $p{fpIdx}.PartitionNumber -GptType '{{{partType}}}'");
                sb.AppendLine($"try {{ $wmiPart = Get-CimInstance -ClassName MSFT_Partition -Namespace root/microsoft/windows/storage -Filter \"DiskNumber=$diskNum AND PartitionNumber=$($p{fpIdx}.PartitionNumber)\"; $wmiPart | Set-CimInstance -Property @{{Name = '{partName}'}} -ErrorAction SilentlyContinue }} catch {{}}");
                if (!string.IsNullOrEmpty(fp.FileSystem) && fp.FileSystem.StartsWith("FAT", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"try {{ Format-Volume -Partition $p{fpIdx} -FileSystem FAT32 -NewFileSystemLabel '{partName}' -Confirm:$false -Force -ErrorAction Stop | Out-Null }} catch {{ Write-Host '    WARNING: FAT32 format failed' -ForegroundColor Yellow }}");
                }
                else if (!string.IsNullOrEmpty(fp.FileSystem) && fp.FileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"try {{ Format-Volume -Partition $p{fpIdx} -FileSystem NTFS -NewFileSystemLabel '{partName}' -Confirm:$false -Force -ErrorAction Stop | Out-Null }} catch {{ Write-Host '    WARNING: NTFS format failed' -ForegroundColor Yellow }}");
                }
                sb.AppendLine("");
            }
            sb.AppendLine("Write-Host '=== Creating OSPool partition ===' -ForegroundColor Cyan");
            sb.AppendLine("$ospool = New-Partition -DiskNumber $diskNum -UseMaximumSize");
            sb.AppendLine("$ospoolNum = $ospool.PartitionNumber");
            sb.AppendLine("Set-Partition -DiskNumber $diskNum -PartitionNumber $ospoolNum -GptType '{5708A6E0-9001-4b99-b064-1fe564896bdb}'");
            sb.AppendLine("try { $wmiOspool = Get-CimInstance -ClassName MSFT_Partition -Namespace root/microsoft/windows/storage -Filter \"DiskNumber=$diskNum AND PartitionNumber=$ospoolNum\"; $wmiOspool | Set-CimInstance -Property @{Name = 'OSPool'} -ErrorAction SilentlyContinue } catch {}");
            sb.AppendLine("[long]$ospoolSize = [long]$ospool.Size");
            sb.AppendLine("Write-Host \"OSPool: $ospoolNum Size: $($ospoolSize/1MB)MB\"");
            sb.AppendLine("Start-Sleep -Milliseconds 500");
            sb.AppendLine("");
            sb.AppendLine("Write-Host '=== Restoring pool data to OSPool partition ===' -ForegroundColor Cyan");
            sb.AppendLine("$dstPath = \"\\\\?\\GLOBALROOT\\Device\\Harddisk$diskNum\\Partition$ospoolNum\"");
            sb.AppendLine("Write-Host \"Dest: $dstPath\"");
            sb.AppendLine("$srcFs2 = [System.IO.File]::OpenRead($backupFile)");
            sb.AppendLine("$dstFs2 = New-Object System.IO.FileStream($dstPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Write, [System.IO.FileShare]::ReadWrite)");
            sb.AppendLine("[long]$restoreSize = [Math]::Min([long]$srcFs2.Length, $ospoolSize)");
            sb.AppendLine("Write-Host \"Restoring: $($restoreSize/1MB)MB\"");
            sb.AppendLine("[long]$remaining2 = $restoreSize");
            sb.AppendLine("while ($remaining2 -gt 0) { [long]$toRead2 = [Math]::Min($chunkSize, $remaining2); $read2 = $srcFs2.Read($buf, 0, [int]$toRead2); if ($read2 -le 0) { break }; $dstFs2.Write($buf, 0, $read2); $remaining2 -= $read2 }");
            sb.AppendLine("$srcFs2.Close(); $dstFs2.Close()");
            sb.AppendLine("Write-Host 'Restore done.'");
            sb.AppendLine("");
            sb.AppendLine("Write-Host '=== Final partition layout ===' -ForegroundColor Cyan");
            sb.AppendLine("Get-Partition -DiskNumber $diskNum | Select-Object PartitionNumber, Type, Size, Offset | Format-Table -AutoSize");
            sb.AppendLine("");
            sb.AppendLine("Write-Host '=== Cleaning up ===' -ForegroundColor Cyan");
            sb.AppendLine("Remove-Item $backupFile -Force -ErrorAction SilentlyContinue");
            sb.AppendLine("Dismount-VHD -Path $vhdPath -ErrorAction SilentlyContinue");
            sb.AppendLine("Write-Host 'VHD dismounted.'");
            sb.AppendLine("");
            sb.AppendLine("Write-Host '=== Storage pool creation complete! ===' -ForegroundColor Green");
            sb.AppendLine("exit 0");

            System.IO.File.WriteAllText(scriptPath, sb.ToString(), System.Text.Encoding.UTF8);
            _statusLabel.Text = "脚本生成完成，正在以管理员权限执行...";
            Application.DoEvents();

            var result = MessageBox.Show(
                $"脚本已生成:\n  VHD: {vhdPath}\n  脚本: {scriptPath}\n\n即将以管理员权限执行 PowerShell 脚本创建存储池。\n\n注意：执行过程中会弹出 UAC 提示，请点击'是'继续。\n\n是否现在执行？",
                "确认执行", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                bool success = VhdCreator.ExecuteStoragePoolScript(scriptPath, out string output);
                _statusLabel.Text = success ? "存储池创建完成" : "脚本执行失败";

                var outputForm = new Form
                {
                    Text = success ? "执行成功" : "执行失败",
                    Size = new Size(900, 600),
                    StartPosition = FormStartPosition.CenterParent
                };
                var textBox = new TextBox
                {
                    Multiline = true,
                    ScrollBars = ScrollBars.Both,
                    Dock = DockStyle.Fill,
                    Text = output,
                    Font = new Font("Consolas", 9F),
                    ReadOnly = true
                };
                outputForm.Controls.Add(textBox);
                outputForm.ShowDialog(this);
            }
            else
            {
                _statusLabel.Text = "已取消执行";
                MessageBox.Show($"已取消执行。\n\nVHD: {vhdPath}\n脚本: {scriptPath}\n\n您可以稍后手动以管理员身份运行脚本。", "已取消", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "创建失败";
            MessageBox.Show($"创建存储池失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CreateStoragePoolNative(VhdFormat format)
    {
        if (_currentLayout == null)
        {
            MessageBox.Show("请先加载设备布局 XML", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_currentLayout.StoragePools.Count == 0)
        {
            MessageBox.Show("该设备布局不包含存储池", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Filter = format == VhdFormat.Vhdx ? "VHDX|*.vhdx" : "VHD|*.vhd",
            Title = "保存原生存储池虚拟磁盘",
            FileName = "NativeStoragePool.vhdx"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            _statusLabel.Text = "正在创建原生存储池 VHD (GPT 直接操作方案)...";
            Application.DoEvents();

            var vhdPath = dlg.FileName;
            var pool = _currentLayout.StoragePools[0];
            var sectorSize = _currentLayout.SectorSize > 0 ? _currentLayout.SectorSize : 512;
            var poolName = string.IsNullOrEmpty(pool.Name) ? "OSPool" : pool.Name;

            // Calculate disk size from pool stores
            long totalPoolSize = 0;
            foreach (var store in pool.Stores)
            {
                var sz = store.SizeInSectors * sectorSize;
                if (sz <= 0) sz = 2L * 1024 * 1024 * 1024;
                totalPoolSize += sz;
            }
            totalPoolSize = (long)(totalPoolSize * 1.2);
            if (totalPoolSize < 16L * 1024 * 1024 * 1024) totalPoolSize = 16L * 1024 * 1024 * 1024;

            // Build front partitions from layout.Stores (top-level partitions before pool)
            var frontParts = new List<FrontPartition>();
            var topStore = _currentLayout.Stores.FirstOrDefault();
            if (topStore != null)
            {
                foreach (var part in topStore.Partitions.Where(p => p.TotalSectors > 0 || p.UseAllSpace))
                {
                    long partSize = part.UseAllSpace ? (64L * 1024 * 1024) : part.TotalSectors * sectorSize;
                    if (partSize <= 0) continue;
                    var partType = string.IsNullOrEmpty(part.Type) ? "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7" : part.Type.Trim('{', '}');
                    var partName = string.IsNullOrEmpty(part.Name) ? "Partition" : part.Name;
                    string fs = null;
                    if (!string.IsNullOrEmpty(part.FileSystem))
                    {
                        if (part.FileSystem.StartsWith("FAT", StringComparison.OrdinalIgnoreCase)) fs = "FAT32";
                        else if (part.FileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase)) fs = "NTFS";
                    }
                    frontParts.Add(new FrontPartition
                    {
                        Name = partName,
                        SizeBytes = partSize,
                        TypeGuid = Guid.Parse(partType),
                        FileSystem = fs
                    });
                }
            }

            // Build virtual disk specs from pool stores
            var vdisks = new List<VirtualDiskSpec>();
            int vdIdx = 0;
            foreach (var store in pool.Stores)
            {
                vdIdx++;
                var vdName = !string.IsNullOrEmpty(store.StoreType) ? store.StoreType : $"VirtualDisk{vdIdx}";
                var vdSize = store.SizeInSectors * sectorSize;
                if (vdSize <= 0) vdSize = 2L * 1024 * 1024 * 1024;
                vdisks.Add(new VirtualDiskSpec { Name = vdName, SizeBytes = vdSize });
            }

            // Create using NativeStoragePoolCreator
            var creator = new NativeStoragePoolCreator
            {
                VhdPath = vhdPath,
                PoolName = poolName,
                DiskSizeBytes = totalPoolSize,
                Format = format,
                FrontPartitions = frontParts,
                VirtualDisks = vdisks,
                Log = (msg) =>
                {
                    _statusLabel.Text = msg.Length > 100 ? msg.Substring(0, 100) + "..." : msg;
                    Application.DoEvents();
                }
            };

            bool success = creator.Create();

            if (success)
            {
                var fi = new FileInfo(vhdPath);
                _statusLabel.Text = "原生存储池创建完成";
                MessageBox.Show($"原生存储池 VHD 创建完成!\n\n文件: {vhdPath}\n大小: {FormatSize(fi.Length)}\n存储池: {poolName}\n虚拟磁盘: {pool.Stores.Count} 个\n前部分区: {frontParts.Count} 个\n\n方案: Windows 原生 Storage Spaces + GPT 直接操作\n挂载后可在磁盘管理中看到存储池和虚拟磁盘。", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                _statusLabel.Text = "创建失败";
                MessageBox.Show("原生存储池创建失败，请查看状态信息了解详情。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "创建失败";
            MessageBox.Show($"创建原生存储池失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string FindAdkDll()
    {
        var adkRoots = new[]
        {
            @"E:\WSK_Tools\ADK\Windows Kits\10",
            @"C:\Program Files (x86)\Windows Kits\10",
            @"C:\Program Files\Windows Kits\10"
        };
        foreach (var root in adkRoots)
        {
            if (!Directory.Exists(root)) continue;
            var versions = Directory.GetDirectories(root).OrderByDescending(d => d);
            foreach (var ver in versions)
            {
                var dllPath = Path.Combine(ver, @"Tools\bin\i386\imagestorageservice.dll");
                if (File.Exists(dllPath)) return dllPath;
            }
        }
        return "";
    }

    private string FormatSize(long bytes)
    {
        if (bytes <= 0) return "未知";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:F2} {units[unit]}";
    }

    private void ShowHelp()
    {
        using var dlg = new Form
        {
            Text = "帮助 - 存储池创建选项说明",
            Size = new Size(680, 520),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.White
        };
        var txt = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(15, 15),
            Size = new Size(635, 430),
            Font = new Font("Segoe UI", 9.5F),
            Text = @"【DeviceLayout Explorer 使用帮助】

1. 打开设备布局
   - 点击「打开 XML」加载设备布局 XML 文件
   - 点击「打开 CAB」从 CAB 包中提取设备布局
   - 左侧树状视图显示存储、分区、存储池等详细信息

2. 创建虚拟磁盘
   - 点击「创建虚拟磁盘」按钮
   - 选择输出 VHD/VHDX 文件路径
   - 程序将调用 DeviceLayoutGeneratorV2（imageapp兼容流程）
   - 自动创建分区、存储池、虚拟磁盘并格式化
   - 创建完成后自动卸载虚拟磁盘

3. 磁盘管理
   - 切换到「磁盘管理」标签页
   - 查看物理磁盘、分区、存储池信息
   - 挂载/卸载 VHD 虚拟磁盘
   - 分配/移除盘符
   - 格式化分区
   - 磁盘联机/脱机

4. 格式选择
   - VHDX：推荐，支持更大容量和更好性能
   - VHD：兼容旧系统，最大 2TB

【注意事项】
- 创建虚拟磁盘需要管理员权限
- 确保 DeviceLayoutGeneratorV2.exe 在同一目录
- x86 版本包含完整 ADK DLL，可直接使用
- 创建过程中请勿关闭程序"
        };
        var btn = new Button { Text = "确定", Location = new Point(290, 455), Size = new Size(100, 30), DialogResult = DialogResult.OK };
        dlg.Controls.AddRange(new Control[] { txt, btn });
        dlg.AcceptButton = btn;
        dlg.ShowDialog(this);
    }

    private void ShowAbout()
    {
        using var dlg = new Form
        {
            Text = "关于",
            Size = new Size(420, 300),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.White
        };
        var lbl = new Label
        {
            Text = "DeviceLayout Explorer\nWSK Tools v1.0.4 Build 260827\n\n设备布局 XML 预览与虚拟磁盘创建工具\n支持从 CAB 包中提取设备布局\n使用 imageapp 兼容流程创建虚拟磁盘\n\n组织: WinStory 2026\nhttps://wiki.win-story.cn\n编译者: DF4D3110",
            Location = new Point(20, 20),
            Size = new Size(360, 200),
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = Color.FromArgb(40, 40, 40)
        };
        var btn = new Button { Text = "确定", Location = new Point(160, 230), Size = new Size(80, 28), DialogResult = DialogResult.OK };
        dlg.Controls.AddRange(new Control[] { lbl, btn });
        dlg.AcceptButton = btn;
        dlg.ShowDialog(this);
    }

    private void InitDiskManagementPage()
    {
        _diskToolStrip = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            Dock = DockStyle.Top,
            BackColor = Color.WhiteSmoke
        };

        var btnRefresh = new ToolStripButton("刷新") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btnRefresh.Click += (s, e) => RefreshAllDiskInfo();

        var btnMount = new ToolStripButton("挂载 VHD") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btnMount.Click += (s, e) => MountVhd();

        var btnDismount = new ToolStripButton("卸载 VHD") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btnDismount.Click += (s, e) => DismountVhd();

        var sep1 = new ToolStripSeparator();

        var btnAssign = new ToolStripButton("分配盘符") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btnAssign.Click += (s, e) => AssignDriveLetter();

        var btnRemoveLetter = new ToolStripButton("移除盘符") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btnRemoveLetter.Click += (s, e) => RemoveDriveLetter();

        var sep2 = new ToolStripSeparator();

        var btnFormat = new ToolStripButton("格式化") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btnFormat.Click += (s, e) => FormatPartition();

        var btnOnline = new ToolStripButton("联机") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btnOnline.Click += (s, e) => SetDiskOnline(true);

        var btnOffline = new ToolStripButton("脱机") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btnOffline.Click += (s, e) => SetDiskOnline(false);

        _diskToolStrip.Items.AddRange(new ToolStripItem[] {
            btnRefresh, btnMount, btnDismount, sep1,
            btnAssign, btnRemoveLetter, sep2,
            btnFormat, btnOnline, btnOffline
        });

        _diskSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 350
        };

        _diskListView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            Font = new Font("Segoe UI", 9F)
        };
        _diskListView.Columns.Add("磁盘", 60);
        _diskListView.Columns.Add("友好名称", 140);
        _diskListView.Columns.Add("大小", 80);
        _diskListView.Columns.Add("分区样式", 70);
        _diskListView.Columns.Add("状态", 60);
        _diskListView.SelectedIndexChanged += (s, e) => RefreshPartitions();

        _diskInfoLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.DimGray,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            Text = "选择磁盘查看分区和卷信息"
        };

        var rightPanel = new Panel { Dock = DockStyle.Fill };
        rightPanel.Controls.Add(_partitionListView = CreatePartitionListView());
        rightPanel.Controls.Add(_diskInfoLabel);
        _diskInfoLabel.BringToFront();

        var leftPanel = new Panel { Dock = DockStyle.Fill };
        leftPanel.Controls.Add(_diskListView);
        var poolLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 20,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 120, 215),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            Text = "存储池 (Storage Spaces)"
        };
        _poolListView = new ListView
        {
            Dock = DockStyle.Bottom,
            Height = 120,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            Font = new Font("Segoe UI", 9F)
        };
        _poolListView.Columns.Add("名称", 120);
        _poolListView.Columns.Add("操作状态", 80);
        _poolListView.Columns.Add("健康状态", 80);
        leftPanel.Controls.Add(poolLabel);
        leftPanel.Controls.Add(_poolListView);

        _diskSplit.Panel1.Controls.Add(leftPanel);
        _diskSplit.Panel2.Controls.Add(rightPanel);
    }

    private ListView CreatePartitionListView()
    {
        var lv = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            Font = new Font("Segoe UI", 9F)
        };
        lv.Columns.Add("分区", 50);
        lv.Columns.Add("盘符", 50);
        lv.Columns.Add("大小", 80);
        lv.Columns.Add("剩余", 80);
        lv.Columns.Add("文件系统", 80);
        lv.Columns.Add("类型", 100);
        return lv;
    }

    private string RunPowerShell(string script)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using var proc = Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd() ?? "";
            var error = proc?.StandardError.ReadToEnd() ?? "";
            proc?.WaitForExit(30000);
            return output + (string.IsNullOrEmpty(error) ? "" : "\n[ERR] " + error);
        }
        catch (Exception ex)
        {
            return $"[ERROR] {ex.Message}";
        }
    }

    private void RefreshAllDiskInfo()
    {
        _statusLabel.Text = "正在刷新磁盘信息...";
        Application.DoEvents();
        RefreshDisks();
        RefreshStoragePools();
        _statusLabel.Text = "磁盘信息已刷新";
    }

    private void RefreshDisks()
    {
        _diskListView.Items.Clear();
        var script = "Get-Disk | Where-Object { $_.BusType -eq 'FileBackedVirtual' -or $_.Location -match '\\.(vhd|vhdx)$' } | Select-Object Number, FriendlyName, Size, PartitionStyle, OperationalStatus, Location | ConvertTo-Csv -NoTypeInformation";
        var output = RunPowerShell(script);
        var lines = output.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#TYPE")).Skip(1);
        foreach (var line in lines)
        {
            var parts = ParseCsvLine(line);
            if (parts.Length >= 5)
            {
                var item = new ListViewItem(parts[0].Trim('"'));
                item.SubItems.Add(parts[1].Trim('"'));
                item.SubItems.Add(FormatSize(long.TryParse(parts[2].Trim('"'), out var sz) ? sz : 0));
                item.SubItems.Add(parts[3].Trim('"'));
                item.SubItems.Add(parts[4].Trim('"'));
                if (parts.Length >= 6) item.Tag = parts[5].Trim('"');
                _diskListView.Items.Add(item);
            }
        }
        if (_diskListView.Items.Count == 0)
        {
            var item = new ListViewItem("(无虚拟磁盘)");
            item.SubItems.Add("-");
            item.SubItems.Add("-");
            item.SubItems.Add("-");
            item.SubItems.Add("-");
            _diskListView.Items.Add(item);
        }
    }

    private void RefreshPartitions()
    {
        _partitionListView.Items.Clear();
        if (_diskListView.SelectedItems.Count == 0)
        {
            _diskInfoLabel.Text = "选择磁盘查看分区和卷信息";
            return;
        }
        var diskNum = _diskListView.SelectedItems[0].Text;
        _diskInfoLabel.Text = $"磁盘 {diskNum} 的分区和卷";

        var script = $"Get-Partition -DiskNumber {diskNum} | Select-Object PartitionNumber, DriveLetter, Size, Type | ConvertTo-Csv -NoTypeInformation";
        var output = RunPowerShell(script);
        var lines = output.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#TYPE")).Skip(1);
        foreach (var line in lines)
        {
            var parts = ParseCsvLine(line);
            if (parts.Length >= 4)
            {
                var item = new ListViewItem(parts[0].Trim('"'));
                item.SubItems.Add(parts[1].Trim('"'));
                item.SubItems.Add(FormatSize(long.TryParse(parts[2].Trim('"'), out var sz) ? sz : 0));
                item.SubItems.Add("-");
                item.SubItems.Add("-");
                item.SubItems.Add(parts[3].Trim('"'));
                _partitionListView.Items.Add(item);
            }
        }

        var volScript = $"Get-Volume | Where-Object {{ $_.DriveLetter }} | Select-Object DriveLetter, Size, SizeRemaining, FileSystem | ConvertTo-Csv -NoTypeInformation";
        var volOutput = RunPowerShell(volScript);
        var volLines = volOutput.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#TYPE")).Skip(1);
        foreach (var line in volLines)
        {
            var parts = ParseCsvLine(line);
            if (parts.Length >= 4)
            {
                var letter = parts[0].Trim('"');
                foreach (ListViewItem item in _partitionListView.Items)
                {
                    if (item.SubItems[1].Text == letter)
                    {
                        item.SubItems[3].Text = FormatSize(long.TryParse(parts[2].Trim('"'), out var rem) ? rem : 0);
                        item.SubItems[4].Text = parts[3].Trim('"');
                        break;
                    }
                }
            }
        }
    }

    private void RefreshStoragePools()
    {
        _poolListView.Items.Clear();
        var script = "Get-StoragePool | Where-Object { $_.FriendlyName -ne 'Primordial' } | Select-Object FriendlyName, OperationalStatus, HealthStatus | ConvertTo-Csv -NoTypeInformation";
        var output = RunPowerShell(script);
        var lines = output.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#TYPE")).Skip(1);
        foreach (var line in lines)
        {
            var parts = ParseCsvLine(line);
            if (parts.Length >= 3)
            {
                var item = new ListViewItem(parts[0].Trim('"'));
                item.SubItems.Add(parts[1].Trim('"'));
                item.SubItems.Add(parts[2].Trim('"'));
                _poolListView.Items.Add(item);
            }
        }
        if (_poolListView.Items.Count == 0)
        {
            var item = new ListViewItem("(无存储池)");
            item.SubItems.Add("-");
            item.SubItems.Add("-");
            _poolListView.Items.Add(item);
        }
    }

    private string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString());
        return result.ToArray();
    }

    private void MountVhd()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "虚拟磁盘|*.vhd;*.vhdx|VHD|*.vhd|VHDX|*.vhdx|所有文件|*.*",
            Title = "选择要挂载的虚拟磁盘"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        _statusLabel.Text = "正在挂载虚拟磁盘...";
        Application.DoEvents();
        var output = RunPowerShell($"Mount-VHD -Path \"{dlg.FileName}\" -PassThru | Select-Object DiskNumber | ConvertTo-Csv -NoTypeInformation");
        MessageBox.Show($"挂载完成:\n{output}", "挂载 VHD", MessageBoxButtons.OK, MessageBoxIcon.Information);
        RefreshAllDiskInfo();
    }

    private void DismountVhd()
    {
        if (_diskListView.SelectedItems.Count == 0)
        {
            MessageBox.Show("请先选择要卸载的磁盘", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var vhdPath = _diskListView.SelectedItems[0].Tag?.ToString() ?? "";
        if (string.IsNullOrEmpty(vhdPath) || (!vhdPath.EndsWith(".vhd", StringComparison.OrdinalIgnoreCase) && !vhdPath.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("选中的磁盘不是虚拟磁盘(VHD/VHDX)", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show($"确定要卸载虚拟磁盘吗?\n\n路径: {vhdPath}", "确认卸载", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _statusLabel.Text = "正在卸载虚拟磁盘...";
        Application.DoEvents();
        RunPowerShell($"Dismount-VHD -Path \"{vhdPath}\"");
        MessageBox.Show("卸载完成", "卸载 VHD", MessageBoxButtons.OK, MessageBoxIcon.Information);
        RefreshAllDiskInfo();
    }

    private void AssignDriveLetter()
    {
        if (_partitionListView.SelectedItems.Count == 0)
        {
            MessageBox.Show("请先选择要分配盘符的分区", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var partNum = _partitionListView.SelectedItems[0].Text;
        var diskNum = _diskListView.SelectedItems.Count > 0 ? _diskListView.SelectedItems[0].Text : "0";
        var used = RunPowerShell("(Get-Volume).DriveLetter | Where-Object { $_ } | ConvertTo-Csv -NoTypeInformation");
        var usedLetters = new HashSet<char>(used.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#TYPE")).Select(l => l.Trim('"', '\r', '\n', ' ')).Where(l => l.Length == 1).Select(l => char.ToUpper(l[0])));
        var available = Enumerable.Range('C', 'Z' - 'C' + 1).Select(c => (char)c).Where(c => !usedLetters.Contains(c)).ToArray();
        if (available.Length == 0)
        {
            MessageBox.Show("没有可用的盘符", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var dlg = new Form { Text = "分配盘符", Size = new Size(250, 150), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var lbl = new Label { Text = "选择盘符:", Location = new Point(20, 20), Size = new Size(80, 25) };
        var cmb = new ComboBox { Location = new Point(100, 20), Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
        cmb.Items.AddRange(available.Cast<object>().ToArray());
        cmb.SelectedIndex = 0;
        var btnOk = new Button { Text = "确定", Location = new Point(40, 70), Width = 70, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "取消", Location = new Point(120, 70), Width = 70, DialogResult = DialogResult.Cancel };
        dlg.Controls.AddRange(new Control[] { lbl, cmb, btnOk, btnCancel });
        dlg.AcceptButton = btnOk;
        dlg.CancelButton = btnCancel;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var letter = cmb.SelectedItem?.ToString() ?? "";
        _statusLabel.Text = $"正在分配盘符 {letter}...";
        Application.DoEvents();
        RunPowerShell($"Set-Partition -DiskNumber {diskNum} -PartitionNumber {partNum} -NewDriveLetter {letter}");
        MessageBox.Show($"盘符 {letter}: 分配完成", "分配盘符", MessageBoxButtons.OK, MessageBoxIcon.Information);
        RefreshPartitions();
    }

    private void RemoveDriveLetter()
    {
        if (_partitionListView.SelectedItems.Count == 0)
        {
            MessageBox.Show("请先选择要移除盘符的分区", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var letter = _partitionListView.SelectedItems[0].SubItems[1].Text;
        if (string.IsNullOrEmpty(letter))
        {
            MessageBox.Show("该分区没有盘符", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show($"确定要移除盘符 {letter}: 吗?", "确认移除", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var partNum = _partitionListView.SelectedItems[0].Text;
        var diskNum = _diskListView.SelectedItems.Count > 0 ? _diskListView.SelectedItems[0].Text : "0";
        _statusLabel.Text = $"正在移除盘符 {letter}...";
        Application.DoEvents();
        RunPowerShell($"Remove-PartitionAccessPath -DiskNumber {diskNum} -PartitionNumber {partNum} -AccessPath \"{letter}:\\\"");
        MessageBox.Show($"盘符 {letter}: 已移除", "移除盘符", MessageBoxButtons.OK, MessageBoxIcon.Information);
        RefreshPartitions();
    }

    private void FormatPartition()
    {
        if (_partitionListView.SelectedItems.Count == 0)
        {
            MessageBox.Show("请先选择要格式化的分区", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var letter = _partitionListView.SelectedItems[0].SubItems[1].Text;
        if (string.IsNullOrEmpty(letter))
        {
            MessageBox.Show("该分区没有盘符，无法格式化", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dlg = new Form { Text = "格式化分区", Size = new Size(300, 200), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var lblFs = new Label { Text = "文件系统:", Location = new Point(20, 20), Size = new Size(80, 25) };
        var cmbFs = new ComboBox { Location = new Point(110, 20), Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbFs.Items.AddRange(new object[] { "NTFS", "FAT32", "exFAT" });
        cmbFs.SelectedIndex = 0;
        var lblLabel = new Label { Text = "卷标:", Location = new Point(20, 55), Size = new Size(80, 25) };
        var txtLabel = new TextBox { Location = new Point(110, 55), Width = 150, Text = "New Volume" };
        var chkQuick = new CheckBox { Text = "快速格式化", Location = new Point(110, 90), Width = 150, Checked = true };
        var btnOk = new Button { Text = "确定", Location = new Point(60, 125), Width = 80, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "取消", Location = new Point(150, 125), Width = 80, DialogResult = DialogResult.Cancel };
        dlg.Controls.AddRange(new Control[] { lblFs, cmbFs, lblLabel, txtLabel, chkQuick, btnOk, btnCancel });
        dlg.AcceptButton = btnOk;
        dlg.CancelButton = btnCancel;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var fs = cmbFs.SelectedItem?.ToString() ?? "NTFS";
        var volLabel = txtLabel.Text;
        var quick = chkQuick.Checked;
        if (MessageBox.Show($"确定要格式化 {letter}: 吗?\n\n文件系统: {fs}\n卷标: {volLabel}\n\n此操作将删除该分区上的所有数据!", "确认格式化", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _statusLabel.Text = $"正在格式化 {letter}:...";
        Application.DoEvents();
        RunPowerShell($"Format-Volume -DriveLetter {letter} -FileSystem {fs} -NewFileSystemLabel \"{volLabel}\" {(quick ? "" : "-Full")} -Confirm:$false");
        MessageBox.Show($"格式化 {letter}: 完成", "格式化", MessageBoxButtons.OK, MessageBoxIcon.Information);
        RefreshPartitions();
    }

    private void SetDiskOnline(bool online)
    {
        if (_diskListView.SelectedItems.Count == 0)
        {
            MessageBox.Show("请先选择磁盘", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var diskNum = _diskListView.SelectedItems[0].Text;
        _statusLabel.Text = online ? "正在联机磁盘..." : "正在脱机磁盘...";
        Application.DoEvents();
        if (online)
            RunPowerShell($"Set-Disk -Number {diskNum} -IsOffline $false");
        else
            RunPowerShell($"Set-Disk -Number {diskNum} -IsOffline $true");
        MessageBox.Show(online ? "磁盘已联机" : "磁盘已脱机", online ? "联机" : "脱机", MessageBoxButtons.OK, MessageBoxIcon.Information);
        RefreshAllDiskInfo();
    }

    /// <summary>17704+ 完整设备布局创建（物理 VHD + 存储池虚拟磁盘）</summary>
    private void CreateFullLayout17704()
    {
        if (_currentLayout == null)
        {
            MessageBox.Show("请先加载设备布局 XML", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new FolderBrowserDialog
        {
            Description = "选择 VHD 输出目录",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        var outputDir = dlg.SelectedPath;
        _statusLabel.Text = "正在创建 17704 完整布局...";
        Application.DoEvents();

        try
        {
            var creator = new DeviceLayoutVhdCreator17704(_currentLayout, outputDir, msg =>
            {
                _statusLabel.Text = msg.Length > 100 ? msg.Substring(0, 100) + "..." : msg;
                Application.DoEvents();
            });
            creator.CreateFullImage();
            _statusLabel.Text = "17704 完整布局创建完成";
            MessageBox.Show($"VHD 文件已创建到:\n{outputDir}", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "创建失败";
            MessageBox.Show($"创建失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>18963+ 完整设备布局创建（增加extentSize/MinSectorCount/BitLocker元数据）</summary>
    private void CreateFullLayout18963()
    {
        if (_currentLayout == null)
        {
            MessageBox.Show("请先加载设备布局 XML", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new FolderBrowserDialog
        {
            Description = "选择 VHD 输出目录",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        var outputDir = dlg.SelectedPath;
        _statusLabel.Text = "正在创建 18963 完整布局...";
        Application.DoEvents();

        try
        {
            var creator = new DeviceLayoutVhdCreator18963(_currentLayout, outputDir, msg =>
            {
                _statusLabel.Text = msg.Length > 100 ? msg.Substring(0, 100) + "..." : msg;
                Application.DoEvents();
            });
            creator.CreateFullImage();
            _statusLabel.Text = "18963 完整布局创建完成";
            MessageBox.Show($"VHD 文件已创建到:\n{outputDir}", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "创建失败";
            MessageBox.Show($"创建失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
