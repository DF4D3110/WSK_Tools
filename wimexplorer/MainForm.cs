using DiscUtils;
using DiscUtils.Wim;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace WimExplorer;

public partial class MainForm : Form
{
    private WimFile? _wimFile;
    private DiscFileSystem? _currentFs;
    private string _currentPath = @"\";
    private string _wimPath = "";
    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    private string? _dragTempDir;

    private MenuStrip menuStrip = null!;
    private ToolStripMenuItem fileMenu = null!;
    private ToolStripMenuItem openMenuItem = null!;
    private ToolStripMenuItem extractMenuItem = null!;
    private ToolStripMenuItem exitMenuItem = null!;
    private ToolStripMenuItem? langMenu;
    private ToolStripMenuItem helpMenu = null!;
    private ToolStripMenuItem aboutMenuItem = null!;
    private ToolStrip toolStrip = null!;
    private ToolStripButton openToolBtn = null!;
    private ToolStripButton extractToolBtn = null!;
    private ToolStripButton upToolBtn = null!;
    private SplitContainer splitContainer = null!;
    private Label _treeLabel = null!;
    private ListView imageListView = null!;
    private TreeView dirTree = null!;
    private ListView fileListView = null!;
    private StatusStrip statusStrip = null!;
    private ToolStripStatusLabel statusLabel = null!;
    private ToolStripStatusLabel pathLabel = null!;
    private ImageList imageList = null!;
    private OpenFileDialog openFileDialog = null!;
    private FolderBrowserDialog folderBrowserDialog = null!;

    public MainForm()
    {
        BuildUi();
        InitLanguageMenu();
        ApplyLanguage();
    }

    public void OpenWimFile(string path) => OpenWim(path);

    private void BuildUi()
    {
        Text = "WIM Image Explorer";
        Size = new System.Drawing.Size(1100, 700);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new System.Drawing.Size(800, 500);
        Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);

        imageList = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new System.Drawing.Size(16, 16) };
        imageList.Images.Add("folder", SystemIcons.GetStockIcon(StockIconId.Folder).ToBitmap());
        imageList.Images.Add("file", SystemIcons.GetStockIcon(StockIconId.Application).ToBitmap());
        imageList.Images.Add("wim", SystemIcons.GetStockIcon(StockIconId.DriveDVD).ToBitmap());
        imageList.Images.Add("image", SystemIcons.GetStockIcon(StockIconId.DriveFixed).ToBitmap());

        openFileDialog = new OpenFileDialog();
        folderBrowserDialog = new FolderBrowserDialog();

        menuStrip = new MenuStrip();
        fileMenu = new ToolStripMenuItem("&File");
        openMenuItem = new ToolStripMenuItem("&Open WIM...", null, (s, e) => OpenWimDialog()) { ShortcutKeys = Keys.Control | Keys.O };
        extractMenuItem = new ToolStripMenuItem("&Extract...", null, (s, e) => ExtractSelected()) { ShortcutKeys = Keys.Control | Keys.E, Enabled = false };
        exitMenuItem = new ToolStripMenuItem("E&xit", null, (s, e) => Close());
        fileMenu.DropDownItems.AddRange(new ToolStripItem[] { openMenuItem, extractMenuItem, new ToolStripSeparator(), exitMenuItem });

        helpMenu = new ToolStripMenuItem("&Help");
        aboutMenuItem = new ToolStripMenuItem("&About", null, (s, e) => ShowAbout());
        helpMenu.DropDownItems.Add(aboutMenuItem);
        menuStrip.Items.AddRange(new ToolStripItem[] { fileMenu, helpMenu });
        MainMenuStrip = menuStrip;

        toolStrip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        openToolBtn = new ToolStripButton("Open", imageList.Images["wim"]) { DisplayStyle = ToolStripItemDisplayStyle.ImageAndText };
        openToolBtn.Click += (s, e) => OpenWimDialog();
        extractToolBtn = new ToolStripButton("Extract", imageList.Images["file"]) { DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, Enabled = false };
        extractToolBtn.Click += (s, e) => ExtractSelected();
        upToolBtn = new ToolStripButton("Up", imageList.Images["folder"]) { DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, Enabled = false };
        upToolBtn.Click += (s, e) => GoUp();
        toolStrip.Items.AddRange(new ToolStripItem[] { openToolBtn, new ToolStripSeparator(), extractToolBtn, new ToolStripSeparator(), upToolBtn });

        var topPanel = new Panel { Dock = DockStyle.Top };
        topPanel.Controls.Add(toolStrip);
        topPanel.Controls.Add(menuStrip);
        topPanel.Height = menuStrip.Height + toolStrip.Height;

        splitContainer = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 280, FixedPanel = FixedPanel.Panel1, SplitterWidth = 6 };

        var imagePanel = new Panel { Dock = DockStyle.Fill };
        var imageLabel = new Label { Text = "  Images", Dock = DockStyle.Top, Height = 24, Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold), BackColor = System.Drawing.Color.FromArgb(240, 240, 240), TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
        imageListView = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, SmallImageList = imageList };
        imageListView.Columns.Add("Index", 50);
        imageListView.Columns.Add("Name", 150);
        imageListView.Columns.Add("Size", 80);
        imageListView.Columns.Add("Bootable", 60);
        imageListView.SelectedIndexChanged += (s, e) => ImageSelected();
        imagePanel.Controls.Add(imageListView);
        imagePanel.Controls.Add(imageLabel);
        splitContainer.Panel1.Controls.Add(imagePanel);

        var rightSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 240, FixedPanel = FixedPanel.Panel1, SplitterWidth = 6 };

        var treePanel = new Panel { Dock = DockStyle.Fill };
        _treeLabel = new Label { Text = "  Folders", Dock = DockStyle.Top, Height = 24, Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold), BackColor = System.Drawing.Color.FromArgb(240, 240, 240), TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
        dirTree = new TreeView { Dock = DockStyle.Fill, ImageList = imageList, ShowLines = true, ShowPlusMinus = true, ShowRootLines = true, BorderStyle = BorderStyle.None };
        dirTree.AfterSelect += (s, e) => DirNodeSelected(e.Node);
        treePanel.Controls.Add(dirTree);
        treePanel.Controls.Add(_treeLabel);
        rightSplit.Panel1.Controls.Add(treePanel);

        fileListView = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, SmallImageList = imageList, BorderStyle = BorderStyle.None };
        fileListView.Columns.Add("Name", 220);
        fileListView.Columns.Add("Size", 90);
        fileListView.Columns.Add("Type", 80);
        fileListView.Columns.Add("Modified", 140);
        fileListView.DoubleClick += (s, e) => FileDoubleClicked();
        fileListView.ItemDrag += (s, e) => FileListView_ItemDrag(e);
        rightSplit.Panel2.Controls.Add(fileListView);

        splitContainer.Panel2.Controls.Add(rightSplit);

        statusStrip = new StatusStrip();
        statusLabel = new ToolStripStatusLabel("Ready") { Spring = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
        pathLabel = new ToolStripStatusLabel("") { Spring = false };
        statusStrip.Items.AddRange(new ToolStripItem[] { statusLabel, pathLabel });

        Controls.Add(splitContainer);
        Controls.Add(statusStrip);
        Controls.Add(topPanel);
    }

    private void InitLanguageMenu()
    {
        langMenu = new ToolStripMenuItem(Lang.Get("LanguageMenu"));
        for (int i = 0; i < Lang.SupportedLanguages.Length; i++)
        {
            string code = Lang.SupportedLanguages[i];
            string name = Lang.LanguageNames[i];
            var item = new ToolStripMenuItem(name, null, (s, e) => { Lang.SetLanguage(code); ApplyLanguage(); });
            langMenu.DropDownItems.Add(item);
        }
        menuStrip.Items.Insert(menuStrip.Items.IndexOf(helpMenu), langMenu);
    }

    private void ApplyLanguage()
    {
        Text = Lang.Get("AppTitle");
        fileMenu.Text = Lang.Get("FileMenu");
        openMenuItem.Text = Lang.Get("OpenWim");
        extractMenuItem.Text = Lang.Get("Extract");
        exitMenuItem.Text = Lang.Get("Exit");
        helpMenu.Text = Lang.Get("HelpMenu");
        aboutMenuItem.Text = Lang.Get("About");
        if (langMenu != null) langMenu.Text = Lang.Get("LanguageMenu");
        openToolBtn.Text = Lang.Get("OpenBtn");
        extractToolBtn.Text = Lang.Get("ExtractBtn");
        upToolBtn.Text = Lang.Get("UpBtn");
        imageListView.Columns[0].Text = Lang.Get("Index");
        imageListView.Columns[1].Text = Lang.Get("Name");
        imageListView.Columns[2].Text = Lang.Get("Size");
        imageListView.Columns[3].Text = Lang.Get("Bootable");
        fileListView.Columns[0].Text = Lang.Get("ColName");
        fileListView.Columns[1].Text = Lang.Get("ColSize");
        fileListView.Columns[2].Text = Lang.Get("ColType");
        fileListView.Columns[3].Text = Lang.Get("ColModified");
        _treeLabel.Text = "  " + Lang.Get("Folder");
        openFileDialog.Title = Lang.Get("OpenWimTitle");
        openFileDialog.Filter = Lang.Get("OpenWimFilter");
        folderBrowserDialog.Description = Lang.Get("ExtractFolderTitle");
        if (_wimFile != null)
            LoadImages();
    }

    private void OpenWimDialog()
    {
        if (openFileDialog.ShowDialog(this) != DialogResult.OK) return;
        OpenWim(openFileDialog.FileName);
    }

    private void OpenWim(string path)
    {
        try
        {
            _currentFs = null;
            _wimPath = path;
            _wimFile = new WimFile(File.OpenRead(path));
            LoadImages();
            statusLabel.Text = $"{Path.GetFileName(path)} - {_wimFile.ImageCount} image(s)";
            extractMenuItem.Enabled = false;
            extractToolBtn.Enabled = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Lang.Get("ErrorOpenWim")}{ex.Message}", Lang.Get("ErrorOpenWim"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadImages()
    {
        imageListView.Items.Clear();
        dirTree.Nodes.Clear();
        fileListView.Items.Clear();
        if (_wimFile == null) return;

        var manifest = _wimFile.Manifest;
        var bootIndex = _wimFile.BootImage;
        int count = _wimFile.ImageCount;

        if (!string.IsNullOrEmpty(manifest))
        {
            try
            {
                var xml = new System.Xml.XmlDocument();
                xml.LoadXml(manifest);
                var imageNodes = xml.SelectNodes("//IMAGE");
                if (imageNodes != null)
                {
                    int i = 0;
                    foreach (System.Xml.XmlNode imgNode in imageNodes)
                    {
                        int displayIndex = int.Parse(imgNode.Attributes?["INDEX"]?.Value ?? (i + 1).ToString());
                        string name = imgNode["NAME"]?.InnerText ?? $"Image {displayIndex}";
                        string desc = imgNode["DESCRIPTION"]?.InnerText ?? "";
                        long totalBytes = long.Parse(imgNode["TOTALBYTES"]?.InnerText ?? "0");
                        bool bootable = (displayIndex == bootIndex);

                        var item = imageListView.Items.Add(displayIndex.ToString());
                        item.ImageIndex = 3;
                        item.SubItems.Add(name);
                        item.SubItems.Add(FormatSize(totalBytes));
                        item.SubItems.Add(bootable ? Lang.Get("Yes") : Lang.Get("No"));
                        item.Tag = i;
                        i++;
                    }
                }
            }
            catch
            {
                for (int i = 0; i < count; i++)
                {
                    var item = imageListView.Items.Add((i + 1).ToString());
                    item.ImageIndex = 3;
                    item.SubItems.Add($"Image {i + 1}");
                    item.SubItems.Add("?");
                    item.SubItems.Add((i + 1 == bootIndex) ? Lang.Get("Yes") : Lang.Get("No"));
                    item.Tag = i;
                }
            }
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                var item = imageListView.Items.Add((i + 1).ToString());
                item.ImageIndex = 3;
                item.SubItems.Add($"Image {i + 1}");
                item.SubItems.Add("?");
                item.SubItems.Add((i + 1 == bootIndex) ? Lang.Get("Yes") : Lang.Get("No"));
                item.Tag = i;
            }
        }
        if (imageListView.Items.Count > 0)
            imageListView.Items[0].Selected = true;
    }

    private void ImageSelected()
    {
        if (imageListView.SelectedItems.Count == 0 || _wimFile == null) return;
        int idx = (int)imageListView.SelectedItems[0].Tag;
        try
        {
            _currentFs = _wimFile.GetImage(idx);
            _currentPath = @"\";
            _history.Clear();
            _historyIndex = -1;
            LoadDirectoryTree();
            NavigateTo(@"\");
            var name = imageListView.SelectedItems[0].SubItems[1].Text;
            var size = imageListView.SelectedItems[0].SubItems[2].Text;
            var bootable = imageListView.SelectedItems[0].SubItems[3].Text == Lang.Get("Yes");
            statusLabel.Text = $"Image {idx}: {name} ({size}){(bootable ? " [Bootable]" : "")}";
            extractMenuItem.Enabled = true;
            extractToolBtn.Enabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Lang.Get("ErrorOpenWim")}{ex.Message}", Lang.Get("ErrorOpenWim"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadDirectoryTree()
    {
        dirTree.Nodes.Clear();
        if (_currentFs == null) return;
        var rootNode = dirTree.Nodes.Add(Lang.Get("Image"));
        rootNode.ImageIndex = 3;
        rootNode.SelectedImageIndex = 3;
        rootNode.Tag = @"\";
        LoadSubDirs(rootNode, @"\");
        rootNode.Expand();
    }

    private void LoadSubDirs(TreeNode parentNode, string path)
    {
        if (_currentFs == null) return;
        try
        {
            foreach (var dir in _currentFs.GetDirectories(path))
            {
                var name = Path.GetFileName(dir.TrimEnd('\\'));
                if (string.IsNullOrEmpty(name)) name = dir;
                var node = parentNode.Nodes.Add(name);
                node.ImageIndex = 0;
                node.SelectedImageIndex = 0;
                node.Tag = dir;
                LoadSubDirs(node, dir);
            }
        }
        catch { }
    }

    private void DirNodeSelected(TreeNode node)
    {
        if (node.Tag is string path)
            NavigateTo(path);
    }

    private void NavigateTo(string path)
    {
        if (_currentFs == null) return;
        _currentPath = path;
        pathLabel.Text = path;
        fileListView.Items.Clear();
        try
        {
            foreach (var dir in _currentFs.GetDirectories(path))
            {
                var name = Path.GetFileName(dir.TrimEnd('\\'));
                if (string.IsNullOrEmpty(name)) name = dir;
                var item = fileListView.Items.Add(name);
                item.ImageIndex = 0;
                item.SubItems.Add("");
                item.SubItems.Add(Lang.Get("Folder"));
                item.SubItems.Add("");
                item.Tag = new FileEntry { Path = dir, IsDirectory = true };
            }
            foreach (var file in _currentFs.GetFiles(path))
            {
                var name = Path.GetFileName(file);
                var item = fileListView.Items.Add(name);
                item.ImageIndex = 1;
                try
                {
                    var info = _currentFs.GetFileInfo(file);
                    item.SubItems.Add(FormatSize(info.Length));
                    item.SubItems.Add(Lang.Get("File"));
                    item.SubItems.Add(info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
                }
                catch
                {
                    item.SubItems.Add("?");
                    item.SubItems.Add(Lang.Get("File"));
                    item.SubItems.Add("");
                }
                item.Tag = new FileEntry { Path = file, IsDirectory = false };
            }
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"Error: {ex.Message}";
        }
        upToolBtn.Enabled = (path != @"\" && path != "");
    }

    private void GoUp()
    {
        if (_currentFs == null || string.IsNullOrEmpty(_currentPath) || _currentPath == @"\") return;
        var parent = _currentPath.TrimEnd('\\');
        var idx = parent.LastIndexOf('\\');
        if (idx <= 0)
            NavigateTo(@"\");
        else
            NavigateTo(parent.Substring(0, idx + 1));
    }

    private void FileDoubleClicked()
    {
        if (fileListView.SelectedItems.Count == 0) return;
        var entry = (FileEntry)fileListView.SelectedItems[0].Tag;
        if (entry.IsDirectory)
        {
            _history.Add(_currentPath);
            _historyIndex = _history.Count - 1;
            NavigateTo(entry.Path);
        }
    }

    private void FileListView_ItemDrag(ItemDragEventArgs e)
    {
        if (_currentFs == null || fileListView.SelectedItems.Count == 0) return;
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"wimex_drag_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            _dragTempDir = tempDir;

            var fileList = new List<string>();
            foreach (ListViewItem item in fileListView.SelectedItems)
            {
                var entry = (FileEntry)item.Tag;
                string name = item.Text;
                string destPath = Path.Combine(tempDir, name);
                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(destPath);
                    ExtractDirectoryTo(entry.Path, destPath);
                }
                else
                {
                    ExtractFileTo(entry.Path, destPath);
                }
                fileList.Add(destPath);
            }

            if (fileList.Count == 0) return;

            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, fileList.ToArray());
            var effect = fileListView.DoDragDrop(data, DragDropEffects.Copy);

            Task.Run(async () =>
            {
                await Task.Delay(5000);
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            });
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"Drag error: {ex.Message}";
        }
    }

    private void ExtractSelected()
    {
        if (_currentFs == null) return;
        if (fileListView.SelectedItems.Count == 0)
        {
            ExtractDirectory(_currentPath);
            return;
        }
        if (folderBrowserDialog.ShowDialog(this) != DialogResult.OK) return;
        string destDir = folderBrowserDialog.SelectedPath;
        foreach (ListViewItem item in fileListView.SelectedItems)
        {
            var entry = (FileEntry)item.Tag;
            if (entry.IsDirectory)
                ExtractDirectoryTo(entry.Path, Path.Combine(destDir, Path.GetFileName(entry.Path.TrimEnd('\\'))));
            else
                ExtractFileTo(entry.Path, Path.Combine(destDir, Path.GetFileName(entry.Path)));
        }
        MessageBox.Show(Lang.Get("ExtractComplete"), Lang.Get("ExtractTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ExtractDirectory(string wimPath)
    {
        if (folderBrowserDialog.ShowDialog(this) != DialogResult.OK) return;
        string destDir = folderBrowserDialog.SelectedPath;
        ExtractDirectoryTo(wimPath, destDir);
        MessageBox.Show(Lang.Get("ExtractComplete"), Lang.Get("ExtractTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ExtractDirectoryTo(string wimPath, string destPath)
    {
        if (_currentFs == null) return;
        Directory.CreateDirectory(destPath);
        foreach (var dir in _currentFs.GetDirectories(wimPath))
        {
            var name = Path.GetFileName(dir.TrimEnd('\\'));
            ExtractDirectoryTo(dir, Path.Combine(destPath, name));
        }
        foreach (var file in _currentFs.GetFiles(wimPath))
        {
            var name = Path.GetFileName(file);
            ExtractFileTo(file, Path.Combine(destPath, name));
        }
    }

    private void ExtractFileTo(string wimPath, string destPath)
    {
        if (_currentFs == null) return;
        try
        {
            if (File.Exists(destPath))
            {
                var result = MessageBox.Show($"{Lang.Get("ConfirmOverwrite")}\n{destPath}", Lang.Get("ExtractTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result != DialogResult.Yes) return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            using var src = _currentFs.OpenFile(wimPath, FileMode.Open);
            using var dst = File.Create(destPath);
            src.CopyTo(dst);
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"{Lang.Get("ExtractFailed")}{ex.Message}";
        }
    }

    private void ShowAbout()
    {
        using var dlg = new Form
        {
            Text = Lang.Get("AboutTitle"),
            Size = new System.Drawing.Size(480, 360),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = new System.Drawing.Font("Microsoft YaHei UI", 9F)
        };
        var title = new Label { Text = "WIMExplorer", Font = new System.Drawing.Font("Microsoft YaHei UI", 14F, System.Drawing.FontStyle.Bold), AutoSize = true, Location = new System.Drawing.Point(20, 15) };
        var ver = new Label { Text = "WSK Tools v1.0.4 Preview Build 260826", AutoSize = true, Location = new System.Drawing.Point(22, 45) };
        var preview = new Label { Text = "⚠ 测试版本 — 部分功能可能存在无法正常工作", Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold), ForeColor = System.Drawing.Color.Red, AutoSize = true, Location = new System.Drawing.Point(22, 68) };
        var desc = new Label { Text = "WIM 镜像浏览器\n支持 WIM/ESD/SWM 格式\n支持多镜像、分卷、可启动标志\n支持文件和文件夹解压\n支持拖拽解压到资源管理器", AutoSize = false, Size = new System.Drawing.Size(420, 100), Location = new System.Drawing.Point(22, 100) };
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
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / 1024.0 / 1024:F1} MB";
        return $"{bytes / 1024.0 / 1024 / 1024:F2} GB";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        try
        {
            if (_dragTempDir != null && Directory.Exists(_dragTempDir))
                Directory.Delete(_dragTempDir, true);
            var tempDir = Path.GetTempPath();
            foreach (var d in Directory.GetDirectories(tempDir, "wimex_drag_*"))
                try { Directory.Delete(d, true); } catch { }
        }
        catch { }
        base.OnFormClosing(e);
    }
}

internal class FileEntry
{
    public string Path = "";
    public bool IsDirectory;
}
