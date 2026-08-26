using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DiscUtils;

namespace FFUExplorer;

public class MainForm : Form
{
    private FfuImage? _ffu;
    private FfuImage? _originalFfu;
    private string? _ffuFilePath;
    private string? _currentPath;
    private FfuPartition? _currentPartition;
    private DiscUtilsFileSystem? _currentFs;
    private OSPoolParser? _osPool;
    private bool _inOSPoolDisk;
    private FfuImage? _osPoolDiskImage;
    private string? _osPoolDiskName;

    private MenuStrip _menuStrip = null!;
    private ToolStrip _toolStrip = null!;
    private ToolStripButton _backBtn = null!;
    private ToolStripButton _upBtn = null!;
    private ToolStripButton _openBtn = null!;
    private ToolStripMenuItem _fileMenu = null!;
    private ToolStripMenuItem _openItem = null!;
    private ToolStripMenuItem _exitItem = null!;
    private ToolStripMenuItem _langMenu = null!;
    private ToolStripMenuItem _helpMenu = null!;
    private ToolStripMenuItem _aboutItem = null!;
    private SplitContainer _splitContainer = null!;
    private ListView _partitionList = null!;
    private TreeView _dirTree = null!;
    private ListView _fileList = null!;
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private ToolStripStatusLabel _pathLabel = null!;
    private TextBox _pathTextBox = null!;
    private ImageList _iconList = null!;

    public MainForm()
    {
        Text = "FFUExplorer - " + Lang.Get("AnalysisTitle").TrimEnd(' ', ':');
        Size = new Size(1000, 650);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(800, 500);
        Font = new Font("Microsoft YaHei UI", 9F);
        BuildUi();
        ApplyLanguage();
    }

    private void BuildUi()
    {
        _iconList = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(16, 16) };
        _iconList.Images.Add("disk", SystemIcons.GetStockIcon(StockIconId.DriveFixed).ToBitmap());
        _iconList.Images.Add("folder", SystemIcons.GetStockIcon(StockIconId.Folder).ToBitmap());
        _iconList.Images.Add("file", SystemIcons.GetStockIcon(StockIconId.Application).ToBitmap());
        _iconList.Images.Add("ffu", SystemIcons.GetStockIcon(StockIconId.DriveDVD).ToBitmap());

        _menuStrip = new MenuStrip();
        _fileMenu = new ToolStripMenuItem("文件(&F)");
        _openItem = new ToolStripMenuItem("打开 FFU...", null, (s, e) => OpenFfu()) { ShortcutKeys = Keys.Control | Keys.O };
        _exitItem = new ToolStripMenuItem("退出", null, (s, e) => Close());
        _fileMenu.DropDownItems.AddRange(new ToolStripItem[] { _openItem, new ToolStripSeparator(), _exitItem });

        _langMenu = new ToolStripMenuItem("语言(&L)");
        string[] langs = { "zh-cn", "zh-tw", "en-us", "ja-jp", "ru-ru", "ko-kr" };
        string[] langNames = { "简体中文", "繁體中文", "English", "日本語", "Русский", "한국어" };
        for (int i = 0; i < langs.Length; i++)
        {
            string code = langs[i];
            var item = new ToolStripMenuItem(langNames[i], null, (s, e) => { Lang.SetLanguage(code); ApplyLanguage(); });
            _langMenu.DropDownItems.Add(item);
        }

        _helpMenu = new ToolStripMenuItem("帮助(&H)");
        _aboutItem = new ToolStripMenuItem("关于 FFUExplorer", null, (s, e) => ShowAbout());
        _helpMenu.DropDownItems.Add(_aboutItem);
        _menuStrip.Items.AddRange(new ToolStripItem[] { _fileMenu, _langMenu, _helpMenu });
        MainMenuStrip = _menuStrip;
        Controls.Add(_menuStrip);

        _toolStrip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        _openBtn = new ToolStripButton("打开 FFU", _iconList.Images["ffu"]) { DisplayStyle = ToolStripItemDisplayStyle.ImageAndText };
        _openBtn.Click += (s, e) => OpenFfu();
        var sep = new ToolStripSeparator();
        _backBtn = new ToolStripButton("返回 OSPool") { DisplayStyle = ToolStripItemDisplayStyle.Text, Enabled = false };
        _backBtn.Click += (s, e) => ExitOSPoolDisk();
        var sep2 = new ToolStripSeparator();
        _upBtn = new ToolStripButton("上级目录") { DisplayStyle = ToolStripItemDisplayStyle.Text, Enabled = false };
        _upBtn.Click += (s, e) => GoUp();
        _toolStrip.Items.AddRange(new ToolStripItem[] { _openBtn, sep, _backBtn, sep2, _upBtn });
        Controls.Add(_toolStrip);

        _pathTextBox = new TextBox { Dock = DockStyle.Top, ReadOnly = true, Font = new Font("Consolas", 9F), Height = 24 };
        Controls.Add(_pathTextBox);

        _splitContainer = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 260, FixedPanel = FixedPanel.Panel1 };

        _partitionList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, SmallImageList = _iconList };
        _partitionList.Columns.Add("分区", 120);
        _partitionList.Columns.Add("类型", 100);
        _partitionList.Columns.Add("大小", 80);
        _partitionList.SelectedIndexChanged += (s, e) => PartitionSelected();

        var partPanel = new Panel { Dock = DockStyle.Fill };
        var partLabel = new Label { Text = "  分区列表", Dock = DockStyle.Top, Height = 22, Font = new Font(Font, FontStyle.Bold), BackColor = Color.FromArgb(240, 240, 240), TextAlign = ContentAlignment.MiddleLeft };
        partPanel.Controls.Add(_partitionList);
        partPanel.Controls.Add(partLabel);
        _splitContainer.Panel1.Controls.Add(partPanel);

        var rightPanel = new Panel { Dock = DockStyle.Fill };
        _dirTree = new TreeView { Dock = DockStyle.Left, Width = 220, ImageList = _iconList, ShowLines = true, ShowPlusMinus = true, ShowRootLines = true };
        _dirTree.AfterSelect += (s, e) => DirNodeSelected(e.Node);
        _fileList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, SmallImageList = _iconList };
        _fileList.Columns.Add("名称", 200);
        _fileList.Columns.Add("大小", 90);
        _fileList.Columns.Add("修改时间", 140);
        _fileList.DoubleClick += (s, e) => FileDoubleClicked();
        rightPanel.Controls.Add(_fileList);
        rightPanel.Controls.Add(_dirTree);
        _splitContainer.Panel2.Controls.Add(rightPanel);
        Controls.Add(_splitContainer);

        _statusStrip = new StatusStrip();
        _statusLabel = new ToolStripStatusLabel("就绪") { Spring = false, TextAlign = ContentAlignment.MiddleLeft };
        _pathLabel = new ToolStripStatusLabel("") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _statusStrip.Items.AddRange(new ToolStripItem[] { _statusLabel, _pathLabel });
        Controls.Add(_statusStrip);
        _splitContainer.BringToFront();
    }

    private void ApplyLanguage()
    {
        string[,] t = {
            { "FFU 镜像查看器", "FFU 鏡像檢視器", "FFU Image Viewer", "FFU イメージビューア", "Просмотрщик FFU", "FFU 이미지 뷰어" },
            { "文件(&F)", "檔案(&F)", "File(&F)", "ファイル(&F)", "Файл(&F)", "파일(&F)" },
            { "打开 FFU...", "開啟 FFU...", "Open FFU...", "FFU を開く...", "Открыть FFU...", "FFU 열기..." },
            { "退出", "退出", "Exit", "終了", "Выход", "종료" },
            { "语言(&L)", "語言(&L)", "Language(&L)", "言語(&L)", "Язык(&L)", "언어(&L)" },
            { "帮助(&H)", "說明(&H)", "Help(&H)", "ヘルプ(&H)", "Справка(&H)", "도움말(&H)" },
            { "关于 FFUExplorer", "關於 FFUExplorer", "About FFUExplorer", "FFUExplorer について", "О программе", "FFUExplorer 정보" },
            { "打开 FFU", "開啟 FFU", "Open FFU", "FFU を開く", "Открыть FFU", "FFU 열기" },
            { "返回 OSPool", "返回 OSPool", "Back to OSPool", "OSPool に戻る", "Назад к OSPool", "OSPool로 돌아가기" },
            { "上级目录", "上層目錄", "Up", "上へ", "Вверх", "위로" },
        };
        string[] codes = { "zh-cn", "zh-tw", "en-us", "ja-jp", "ru-ru", "ko-kr" };
        int idx = Array.IndexOf(codes, Lang.Current);
        if (idx < 0) idx = 0;
        Text = "FFUExplorer - " + t[0, idx];
        _fileMenu.Text = t[1, idx];
        _openItem.Text = t[2, idx];
        _exitItem.Text = t[3, idx];
        _langMenu.Text = t[4, idx];
        _helpMenu.Text = t[5, idx];
        _aboutItem.Text = t[6, idx];
        _openBtn.Text = t[7, idx];
        _backBtn.Text = t[8, idx];
        _upBtn.Text = t[9, idx];
    }

    private void OpenFfu()
    {
        using var dlg = new OpenFileDialog { Filter = "FFU 镜像文件 (*.ffu)|*.ffu|所有文件 (*.*)|*.*", Title = "选择 FFU 镜像文件" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        OpenFfuFile(dlg.FileName);
    }

    private void OpenFfuFile(string path)
    {
        try
        {
            _ffu?.Dispose();
            _ffu = null;
            _currentFs?.Dispose();
            _currentFs = null;

            int storeCount = FfuImage.GetStoreCount(path);
            int storeIndex = 0;

            if (storeCount > 1)
            {
                var infos = FfuImage.GetStoreInfos(path);
                using var dlg = new StoreSelectDialog(infos);
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    _statusLabel.Text = "用户取消选择磁盘";
                    return;
                }
                storeIndex = dlg.SelectedIndex;
            }

            _statusLabel.Text = "正在加载 FFU...";
            Application.DoEvents();
            Cursor = Cursors.WaitCursor;
            try
            {
                _ffu = new FfuImage();
                if (!_ffu.Open(path, storeIndex))
                    throw new IOException("无法解析 FFU 镜像（未找到分区表）");
                _ffuFilePath = path;
                _originalFfu = _ffu;
                DisplayPartitions(path);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"打开 FFU 失败:\r\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLabel.Text = "加载失败";
        }
    }

    private void DisplayPartitions(string path)
    {
        if (_ffu == null) return;
        Text = $"FFUExplorer - {Path.GetFileName(path)}";
        _pathTextBox.Text = path;
        _statusLabel.Text = $"已加载: {Path.GetFileName(path)}";
        _pathLabel.Text = $"{_ffu.Partitions.Count} 个分区 | 磁盘大小: {FormatSize(_ffu.DiskSize)} | 扇区: {_ffu.SectorSize}B | {_ffu.DevicePath}";

        _partitionList.Items.Clear();
        _dirTree.Nodes.Clear();
        _fileList.Items.Clear();

        foreach (var part in _ffu.Partitions)
        {
            var item = new ListViewItem(part.Name) { ImageKey = "disk" };
            item.SubItems.Add(part.FileSystem);
            item.SubItems.Add(FormatSize(part.SizeBytes));
            item.Tag = part;
            _partitionList.Items.Add(item);
        }

        if (_partitionList.Items.Count > 0)
            _partitionList.Items[0].Selected = true;
    }

    private void PartitionSelected()
    {
        if (_partitionList.SelectedItems.Count == 0) return;
        if (_partitionList.SelectedItems[0].Tag is not FfuPartition part) return;

        _currentFs?.Dispose();
        _osPool?.Dispose();
        _osPool = null;
        _currentPartition = part;
        _currentPath = @"\";

        _dirTree.Nodes.Clear();
        _fileList.Items.Clear();

        if (IsOSPoolPartition(part))
        {
            ShowOSPoolVirtualDisks(part);
            return;
        }

        _currentFs = _ffu?.OpenFileSystem(part);

        if (_currentFs == null)
        {
            _statusLabel.Text = $"分区 {part.Name}: 无法识别文件系统 ({part.FileSystem})";
            var item = new ListViewItem("(无法浏览此分区的文件系统)") { ImageKey = "file", ForeColor = Color.Gray };
            _fileList.Items.Add(item);
            return;
        }

        var rootNode = new TreeNode($"{part.Name} ({part.FileSystem})") { ImageKey = "folder", SelectedImageKey = "folder", Tag = @"\" };
        _dirTree.Nodes.Add(rootNode);
        LoadSubDirs(rootNode);
        rootNode.Expand();
        _dirTree.SelectedNode = rootNode;
        _statusLabel.Text = $"分区: {part.Name} | 文件系统: {part.FileSystem}";
    }

    private bool IsOSPoolPartition(FfuPartition part)
    {
        string guidStr = part.GuidType.ToString("N").ToLower();
        if (guidStr.StartsWith("e75caf8f")) return true;
        var stream = _ffu?.OpenPartitionRaw(part);
        if (stream != null)
        {
            try
            {
                byte[] sig = new byte[8];
                stream.Read(sig, 0, 8);
                if (System.Text.Encoding.ASCII.GetString(sig, 0, 7) == "SPACEDB")
                    return true;
            }
            catch { }
        }
        return false;
    }

    private void ShowOSPoolVirtualDisks(FfuPartition part)
    {
        var stream = _ffu?.OpenPartitionRaw(part);
        if (stream == null)
        {
            _statusLabel.Text = $"OSPool 分区 {part.Name}: 无法打开";
            return;
        }
        _osPool = new OSPoolParser(stream, false);
        if (!_osPool.IsOSPool)
        {
            _statusLabel.Text = $"分区 {part.Name}: 不是有效的 OSPool";
            var item = new ListViewItem("(不是有效的 OSPool 分区)") { ImageKey = "file", ForeColor = Color.Gray };
            _fileList.Items.Add(item);
            return;
        }

        _statusLabel.Text = $"OSPool: {part.Name} | {_osPool.VirtualDisks.Count} 个虚拟磁盘";
        _pathTextBox.Text = $"{part.Name} (OSPool)";

        var headerItem = new ListViewItem("=== OSPool 虚拟磁盘 (双击打开) ===") { ImageKey = "disk", ForeColor = Color.Navy, Font = new Font(_fileList.Font, FontStyle.Bold) };
        _fileList.Items.Add(headerItem);

        foreach (var vd in _osPool.VirtualDisks)
        {
            string name = vd.DisplayName;
            var item = new ListViewItem(name) { ImageKey = "disk" };
            item.SubItems.Add(FormatSize(vd.DeclaredSize));
            item.SubItems.Add($"{vd.PartitionCount} 分区");
            item.Tag = new OSPoolDiskEntry { Index = vd.Index, Name = name };
            _fileList.Items.Add(item);
        }
    }

    private void EnterOSPoolDisk(int diskIndex, string diskName)
    {
        if (_osPool == null) return;
        var stream = _osPool.OpenVirtualDisk(diskIndex);
        if (stream == null) return;

        _originalFfu = _ffu;
        _osPoolDiskImage?.Dispose();
        _osPoolDiskImage = new FfuImage();
        _osPoolDiskName = diskName;
        _inOSPoolDisk = true;

        if (!_osPoolDiskImage.OpenRaw(stream, diskName))
        {
            _statusLabel.Text = $"虚拟磁盘 {diskName}: 无法解析分区表";
            _inOSPoolDisk = false;
            _ffu = _originalFfu;
            return;
        }

        _partitionList.Items.Clear();
        foreach (var part in _osPoolDiskImage.Partitions)
        {
            var item = new ListViewItem(part.Name) { ImageKey = "disk" };
            item.SubItems.Add(part.FileSystem);
            item.SubItems.Add(FormatSize(part.SizeBytes));
            item.Tag = part;
            _partitionList.Items.Add(item);
        }

        _ffu = _osPoolDiskImage;
        _backBtn.Enabled = true;
        _pathTextBox.Text = $"OSPool > {diskName}";
        _statusLabel.Text = $"虚拟磁盘: {diskName} | {_osPoolDiskImage.Partitions.Count} 个分区 | 点击返回按钮回到 OSPool";

        if (_partitionList.Items.Count > 0)
            _partitionList.Items[0].Selected = true;
    }

    private void ExitOSPoolDisk()
    {
        if (!_inOSPoolDisk) return;
        _inOSPoolDisk = false;
        _backBtn.Enabled = false;
        _osPoolDiskImage?.Dispose();
        _osPoolDiskImage = null;
        _ffu = _originalFfu;
        if (_ffuFilePath != null)
            DisplayPartitions(_ffuFilePath);
        _partitionList.SelectedItems.Clear();
        if (_currentPartition != null)
        {
            foreach (ListViewItem item in _partitionList.Items)
            {
                if (item.Tag is FfuPartition p && p.Name == _currentPartition.Name)
                {
                    item.Selected = true;
                    break;
                }
            }
        }
    }

    private void LoadSubDirs(TreeNode parent)
    {
        if (_currentFs == null) return;
        try
        {
            string path = parent.Tag?.ToString() ?? @"\";
            var dirs = _currentFs.FileSystem.GetDirectories(path).ToArray();
            foreach (var dir in dirs)
            {
                string name = Path.GetFileName(dir.TrimEnd('\\'));
                if (string.IsNullOrEmpty(name)) name = dir;
                var node = new TreeNode(name) { ImageKey = "folder", SelectedImageKey = "folder", Tag = dir };
                parent.Nodes.Add(node);
                try
                {
                    var subDirs = _currentFs.FileSystem.GetDirectories(dir).ToArray();
                    if (subDirs.Length > 0)
                        node.Nodes.Add(new TreeNode("...") { Tag = null });
                }
                catch { }
            }
        }
        catch { }
    }

    private void DirNodeSelected(TreeNode? node)
    {
        if (node == null || _currentFs == null) return;
        string path = node.Tag?.ToString() ?? @"\";
        if (string.IsNullOrEmpty(path)) return;

        if (node.Nodes.Count == 1 && node.Nodes[0].Tag == null)
        {
            node.Nodes.Clear();
            LoadSubDirs(node);
        }

        _currentPath = path;
        _pathTextBox.Text = $"{_currentPartition?.Name}:{path}";
        LoadFileList(path);
    }

    private void LoadFileList(string path)
    {
        if (_currentFs == null) return;
        _fileList.Items.Clear();
        try
        {
            var dirs = _currentFs.FileSystem.GetDirectories(path).ToArray();
            foreach (var dir in dirs)
            {
                string name = Path.GetFileName(dir.TrimEnd('\\'));
                if (string.IsNullOrEmpty(name)) name = dir;
                var item = new ListViewItem(name) { ImageKey = "folder" };
                item.SubItems.Add("");
                item.SubItems.Add("");
                item.Tag = new FileEntry { Name = name, Path = dir, IsDirectory = true };
                _fileList.Items.Add(item);
            }

            var files = _currentFs.FileSystem.GetFiles(path).ToArray();
            foreach (var file in files)
            {
                string name = Path.GetFileName(file);
                long size = 0;
                DateTime modified = DateTime.MinValue;
                try
                {
                    var info = _currentFs.FileSystem.GetFileInfo(file);
                    size = info.Length;
                    modified = info.LastWriteTime;
                }
                catch { }
                var item = new ListViewItem(name) { ImageKey = "file" };
                item.SubItems.Add(FormatSize(size));
                item.SubItems.Add(modified == DateTime.MinValue ? "" : modified.ToString("yyyy-MM-dd HH:mm"));
                item.Tag = new FileEntry { Name = name, Path = file, IsDirectory = false };
                _fileList.Items.Add(item);
            }
        }
        catch (Exception ex)
        {
            var item = new ListViewItem($"(读取失败: {ex.Message})") { ImageKey = "file", ForeColor = Color.Red };
            _fileList.Items.Add(item);
        }
        _upBtn.Enabled = (path != @"\" && path != "" && !string.IsNullOrEmpty(path));
    }

    private void FileDoubleClicked()
    {
        if (_fileList.SelectedItems.Count == 0) return;
        var tag = _fileList.SelectedItems[0].Tag;
        if (tag is OSPoolDiskEntry osDisk)
        {
            EnterOSPoolDisk(osDisk.Index, osDisk.Name);
            return;
        }
        if (tag is not FileEntry entry) return;
        if (entry.IsDirectory)
        {
            var node = FindDirNode(entry.Path);
            if (node != null)
            {
                _dirTree.SelectedNode = node;
                node.Expand();
            }
            else
            {
                _currentPath = entry.Path;
                LoadFileList(entry.Path);
            }
        }
    }

    private TreeNode? FindDirNode(string path)
    {
        foreach (TreeNode root in _dirTree.Nodes)
        {
            var found = FindNodeRecursive(root, path);
            if (found != null) return found;
        }
        return null;
    }

    private TreeNode? FindNodeRecursive(TreeNode node, string path)
    {
        if (node.Tag?.ToString() == path) return node;
        foreach (TreeNode child in node.Nodes)
        {
            var found = FindNodeRecursive(child, path);
            if (found != null) return found;
        }
        return null;
    }

    private void GoUp()
    {
        if (string.IsNullOrEmpty(_currentPath) || _currentPath == @"\") return;
        string parent = Path.GetDirectoryName(_currentPath.TrimEnd('\\')) ?? @"\";
        if (string.IsNullOrEmpty(parent)) parent = @"\";
        var node = FindDirNode(parent);
        if (node != null) _dirTree.SelectedNode = node;
        else { _currentPath = parent; LoadFileList(parent); }
    }

    private void ShowAbout()
    {
        using var dlg = new Form
        {
            Text = "关于 FFUExplorer",
            Size = new System.Drawing.Size(480, 360),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = new System.Drawing.Font("Microsoft YaHei UI", 9F)
        };
        var title = new Label { Text = "FFUExplorer", Font = new System.Drawing.Font("Microsoft YaHei UI", 14F, System.Drawing.FontStyle.Bold), AutoSize = true, Location = new System.Drawing.Point(20, 15) };
        var ver = new Label { Text = "WSK Tools v1.0.4 Preview Build 260826", AutoSize = true, Location = new System.Drawing.Point(22, 45) };
        var preview = new Label { Text = "⚠ 测试版本 — 部分功能可能存在无法正常工作", Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold), ForeColor = System.Drawing.Color.Red, AutoSize = true, Location = new System.Drawing.Point(22, 68) };
        var desc = new Label { Text = "FFU 镜像分区与文件浏览器\n支持 V1/V1.1(压缩)/V2 所有 FFU 格式\n只读浏览，不解压不释放\n\n基于 Img2Ffu (Gustave Monce, MIT)", AutoSize = false, Size = new System.Drawing.Size(420, 100), Location = new System.Drawing.Point(22, 100) };
        var info = new Label { Text = "组织: WinStory 2026\nhttps://wiki.win-story.cn\n编译者: DF4D3110", AutoSize = false, Size = new System.Drawing.Size(420, 80), Location = new System.Drawing.Point(22, 210) };
        var ok = new Button { Text = "确定", Size = new System.Drawing.Size(80, 28), Location = new System.Drawing.Point(180, 290), DialogResult = DialogResult.OK };
        dlg.Controls.AddRange(new Control[] { title, ver, preview, desc, info, ok });
        dlg.AcceptButton = ok;
        dlg.ShowDialog(this);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _currentFs?.Dispose();
        _ffu?.Dispose();
        base.OnFormClosing(e);
    }
}

internal class FileEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsDirectory { get; set; }
}

internal class OSPoolDiskEntry
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
}

internal class StoreSelectDialog : Form
{
    private readonly ListBox _listBox;
    private readonly Button _okBtn;
    private readonly Button _cancelBtn;
    public int SelectedIndex => _listBox.SelectedIndex;

    public StoreSelectDialog(List<StoreInfo> stores)
    {
        Text = "选择磁盘";
        Size = new Size(550, 350);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Microsoft YaHei UI", 9F);

        var label = new Label { Text = $"检测到 {stores.Count} 个磁盘，请选择要查看的磁盘：", Location = new Point(12, 12), AutoSize = true };

        _listBox = new ListBox { Location = new Point(12, 40), Size = new Size(510, 220), Font = new Font("Consolas", 9F) };
        foreach (var s in stores)
        {
            string dev = string.IsNullOrEmpty(s.DevicePath) ? $"Store {s.Index}" : s.DevicePath;
            _listBox.Items.Add($"[{s.Index}] {dev}  ({s.Size / 1024.0 / 1024.0 / 1024.0:F2} GB, {s.SectorSize}B/sector)");
        }
        _listBox.SelectedIndex = 0;
        _listBox.DoubleClick += (s, e) => DialogResult = DialogResult.OK;

        _okBtn = new Button { Text = "确定", Location = new Point(340, 275), Size = new Size(85, 30), DialogResult = DialogResult.OK };
        _cancelBtn = new Button { Text = "取消", Location = new Point(437, 275), Size = new Size(85, 30), DialogResult = DialogResult.Cancel };

        Controls.AddRange(new Control[] { label, _listBox, _okBtn, _cancelBtn });
        AcceptButton = _okBtn;
        CancelButton = _cancelBtn;
    }
}
