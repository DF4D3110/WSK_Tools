using DiscUtils;
using DiscUtils.Partitions;
using DiscUtils.Ntfs;
using DiscUtils.Fat;
using System.Runtime.InteropServices;

namespace VirtualDiskExplorer;

public partial class MainForm : Form
{
    private VirtualDisk? _currentDisk;
    private DiscFileSystem? _currentFs;
    private PartitionInfo? _currentPartition;
    private string _currentPath = "";
    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    private string _diskPath = "";
    private readonly Stack<NestedDiskInfo> _diskStack = new();
    private VirtualDisk? _nestedDisk;
    private string? _currentTempFile;

    private const int ICON_FOLDER = 0;
    private const int ICON_DISK = 1;
    private const int ICON_PARTITION = 2;
    private const int ICON_FILE = 3;
    private const int ICON_VHD = 4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    private static Icon GetSystemIcon(string path, uint attributes)
    {
        SHFILEINFO shfi = new SHFILEINFO();
        SHGetFileInfo(path, attributes, ref shfi, (uint)Marshal.SizeOf(shfi), SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
        if (shfi.hIcon != IntPtr.Zero)
        {
            return Icon.FromHandle(shfi.hIcon);
        }
        return SystemIcons.Application;
    }

    public MainForm()
    {
        InitializeComponent();
        InitIcons();
        InitLanguageMenu();
        ApplyLanguage();
    }

    private ToolStripMenuItem? _langMenu;

    private void InitLanguageMenu()
    {
        _langMenu = new ToolStripMenuItem(Lang.Get("LanguageMenu"));
        for (int i = 0; i < Lang.SupportedLanguages.Length; i++)
        {
            string code = Lang.SupportedLanguages[i];
            string name = Lang.LanguageNames[i];
            var item = new ToolStripMenuItem(name, null, (s, e) =>
            {
                Lang.SetLanguage(code);
                ApplyLanguage();
            });
            _langMenu.DropDownItems.Add(item);
        }
        menuStrip.Items.Insert(menuStrip.Items.IndexOf(helpMenu), _langMenu);
    }

    public void OpenDiskFilePublic(string path) => OpenDiskFile(path);

    private void ApplyLanguage()
    {
        Text = Lang.Get("AppTitle");
        fileMenu.Text = Lang.Get("FileMenu");
        openMenuItem.Text = Lang.Get("OpenDisk");
        exitMenuItem.Text = Lang.Get("Exit");
        helpMenu.Text = Lang.Get("HelpMenu");
        aboutMenuItem.Text = Lang.Get("About");
        if (_langMenu != null) _langMenu.Text = Lang.Get("LanguageMenu");
        openToolBtn.Text = Lang.Get("OpenBtn");
        backToolBtn.Text = Lang.Get("BackBtn");
        forwardToolBtn.Text = Lang.Get("ForwardBtn");
        upToolBtn.Text = Lang.Get("UpBtn");
        pathLabel.Text = Lang.Get("PathLabel");
        nameColumn.Text = Lang.Get("ColName");
        sizeColumn.Text = Lang.Get("ColSize");
        typeColumn.Text = Lang.Get("ColType");
        modifiedColumn.Text = Lang.Get("ColModified");
        openFileDialog.Title = Lang.Get("OpenDiskTitle");
        openFileDialog.Filter = Lang.Get("OpenDiskFilter");
        if (_currentDisk != null)
            LoadPartitionTree();
        if (_currentFs != null)
            NavigateTo(_currentPath);
    }

    private void InitIcons()
    {
        fileImageList.Images.Clear();
        fileImageList.Images.Add(GetSystemIcon("folder", FILE_ATTRIBUTE_DIRECTORY).ToBitmap());
        fileImageList.Images.Add(GetSystemIcon("drive.img", FILE_ATTRIBUTE_NORMAL).ToBitmap());
        fileImageList.Images.Add(GetSystemIcon("partition.img", FILE_ATTRIBUTE_NORMAL).ToBitmap());
        fileImageList.Images.Add(GetSystemIcon("file.txt", FILE_ATTRIBUTE_NORMAL).ToBitmap());
        fileImageList.Images.Add(GetSystemIcon("disk.vhdx", FILE_ATTRIBUTE_NORMAL).ToBitmap());
    }

    private static bool IsVirtualDiskFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".vhd" or ".vhdx" or ".vmdk" or ".vdi" or ".qcow" or ".qcow2" or ".raw" or ".img" or ".dd";
    }

    private void openMenuItem_Click(object? sender, EventArgs e)
    {
        if (openFileDialog.ShowDialog() != DialogResult.OK) return;
        OpenDiskFile(openFileDialog.FileName);
    }

    private void OpenDiskFile(string path)
    {
        try
        {
            CloseAllDisks();
            _diskPath = path;
            _currentDisk = DiskOpener.OpenDisk(path);
            if (_currentDisk == null)
            {
                MessageBox.Show("Failed to open disk image.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var format = DiskOpener.GetDiskFormat(path);
            var sizeGB = _currentDisk.Capacity / 1024.0 / 1024.0 / 1024.0;
            diskStatusLabel.Text = $"{Path.GetFileName(path)} [{format}] {sizeGB:F2} GB";

            LoadPartitionTree();
            UpdateNavButtons();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening disk: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CloseAllDisks()
    {
        _currentFs?.Dispose();
        _nestedDisk?.Dispose();
        _currentDisk?.Dispose();
        while (_diskStack.Count > 0)
        {
            var info = _diskStack.Pop();
            info.Fs?.Dispose();
            info.Disk?.Dispose();
            if (info.TempFile != null && File.Exists(info.TempFile))
            {
                try { File.Delete(info.TempFile); } catch { }
            }
        }
        if (_currentTempFile != null && File.Exists(_currentTempFile))
        {
            try { File.Delete(_currentTempFile); } catch { }
        }
        try
        {
            var tempDir = Path.GetTempPath();
            foreach (var f in Directory.GetFiles(tempDir, "vde_*.vhdx"))
                try { File.Delete(f); } catch { }
            foreach (var f in Directory.GetFiles(tempDir, "vde_nested_*"))
                try { File.Delete(f); } catch { }
        }
        catch { }
        _currentDisk = null;
        _nestedDisk = null;
        _currentFs = null;
        _currentPartition = null;
        _currentPath = "";
        _currentTempFile = null;
        _history.Clear();
        _historyIndex = -1;
    }

    private void LoadPartitionTree()
    {
        partitionTree.BeginUpdate();
        partitionTree.Nodes.Clear();

        if (_currentDisk == null) { partitionTree.EndUpdate(); return; }

        var diskNode = partitionTree.Nodes.Add($"{Lang.Get("Disk")} ({DiskOpener.GetDiskFormat(_diskPath)})");
        diskNode.Tag = "disk";
        diskNode.ImageIndex = ICON_DISK;
        diskNode.SelectedImageIndex = ICON_DISK;

        var partitions = DiskOpener.GetPartitions(_currentDisk);
        if (partitions.Count == 0)
        {
            var rawNode = diskNode.Nodes.Add(Lang.Get("RawFs"));
            rawNode.Tag = "raw";
            rawNode.ImageIndex = ICON_PARTITION;
            rawNode.SelectedImageIndex = ICON_PARTITION;
        }
        else
        {
            for (int i = 0; i < partitions.Count; i++)
            {
                var p = partitions[i];
                var sizeMB = p.SectorCount * 512 / 1024.0 / 1024.0;
                string label = "";
                try { if (p is GuidPartitionInfo gpi) label = gpi.Name; } catch { }

                var nodeText = string.IsNullOrEmpty(label)
                    ? $"{Lang.Get("Partition")} {i + 1}  {sizeMB:F1} MB"
                    : $"{label}  {sizeMB:F1} MB";

                var node = diskNode.Nodes.Add(nodeText);
                node.Tag = new PartitionNodeTag(p);
                node.ImageIndex = ICON_PARTITION;
                node.SelectedImageIndex = ICON_PARTITION;
            }
        }

        diskNode.Expand();
        partitionTree.EndUpdate();
        fileListView.Items.Clear();
        pathTextBox.Text = "";
        fileStatusLabel.Text = "";
    }

    private void partitionTree_AfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (_diskStack.Count > 0) return;
        if (e.Node?.Tag is string s && s == "ospool_hint") return;
        if (e.Node?.Tag is PartitionNodeTag pTag)
        {
            var p = pTag.Partition;
            if (DiskOpener.IsSpaceDB(p))
            {
                if (!pTag.MarkedAsSpaceDB)
                {
                    pTag.MarkedAsSpaceDB = true;
                    e.Node.Text = "[OSPool/SpaceDB] " + e.Node.Text + " (click to scan)";
                    e.Node.ImageIndex = ICON_VHD;
                    e.Node.SelectedImageIndex = ICON_VHD;
                    e.Node.ForeColor = Color.DarkBlue;
                    e.Node.Tag = new SpaceDBPartitionTag(p);
                }
                OpenSpaceDBPartition(p);
            }
            else
            {
                OpenPartition(p);
            }
        }
        else if (e.Node?.Tag is SpaceDBPartitionTag spTag)
        {
            OpenSpaceDBPartition(spTag.Partition);
        }
        else if (e.Node?.Tag is PartitionInfo p)
        {
            OpenPartition(p);
        }
        else if (e.Node?.Tag is string s2 && s2 == "raw")
            OpenRawDisk();
    }

    private void OpenPartition(PartitionInfo partition)
    {
        try
        {
            _currentFs?.Dispose();
            _currentPartition = partition;
            _currentFs = DiskOpener.OpenFileSystem(partition);
            _currentPath = "";

            if (_currentFs == null)
            {
                fileListView.Items.Clear();
                var item = fileListView.Items.Add("(No recognized filesystem or filesystem not supported)");
                item.ForeColor = Color.Gray;
                pathTextBox.Text = "";
                fileStatusLabel.Text = "";
                return;
            }

            NavigateTo("");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening partition: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenSpaceDBPartition(PartitionInfo partition)
    {
        try
        {
            _currentFs?.Dispose();
            _currentPartition = partition;
            _currentFs = null;
            _currentPath = "";

            var info = DiskOpener.GetSpaceDBInfo(partition);
            fileListView.Items.Clear();
            pathTextBox.Text = "\\[OSPool/SpaceDB]";

            var headerItem = fileListView.Items.Add(Lang.Get("SPACEDB"));
            headerItem.ForeColor = Color.DarkBlue;
            headerItem.Font = new Font(fileListView.Font, FontStyle.Bold);
            headerItem.SubItems.Add("");
            headerItem.SubItems.Add(Lang.Get("StoragePool"));
            headerItem.SubItems.Add("");

            if (info != null)
            {
                var verItem = fileListView.Items.Add($"{Lang.Get("SPACEDBVersion")}{info.Version}");
                verItem.SubItems.Add("");
                verItem.SubItems.Add(Lang.Get("Info"));
                verItem.SubItems.Add("");

                var poolItem = fileListView.Items.Add($"{Lang.Get("SPACEDBPoolId")}{info.PoolId}");
                poolItem.SubItems.Add("");
                poolItem.SubItems.Add(Lang.Get("Info"));
                poolItem.SubItems.Add("");

                var sizeItem = fileListView.Items.Add($"{Lang.Get("SPACEDBSize")}{info.TotalSize / 1024.0 / 1024 / 1024:F2} GB");
                sizeItem.SubItems.Add("");
                sizeItem.SubItems.Add(Lang.Get("Info"));
                sizeItem.SubItems.Add("");

                if (info.VirtualDisks.Count > 0)
                {
                    var vdHeader = fileListView.Items.Add($"--- {info.VirtualDisks.Count} {Lang.Get("SPACEDBVirtualDisks")} ---");
                    vdHeader.ForeColor = Color.DarkGreen;
                    vdHeader.Font = new Font(fileListView.Font, FontStyle.Bold);
                    vdHeader.SubItems.Add("");
                    vdHeader.SubItems.Add(Lang.Get("VirtualDisk"));
                    vdHeader.SubItems.Add("");

                    foreach (var vd in info.VirtualDisks)
                    {
                        var item = fileListView.Items.Add(vd.Name);
                        item.ImageIndex = ICON_VHD;
                        item.Tag = new SpaceDBVirtualDiskTag(partition, vd);
                        item.SubItems.Add(vd.Capacity > 0 ? $"{vd.CapacityGB:F2} GB" : "?");
                        item.SubItems.Add(vd.DataOffset > 0 ? $"@ {vd.DataOffset / 1024.0 / 1024:F0} MB" : "SPACEDB");
                        item.SubItems.Add(Lang.Get("DoubleClickBrowse"));
                    }
                }
            }

            var noteItem = fileListView.Items.Add(Lang.Get("SPACEDBNote"));
            noteItem.ForeColor = Color.Orange;
            noteItem.SubItems.Add("");
            noteItem.SubItems.Add("Note");
            noteItem.SubItems.Add("");

            fileStatusLabel.Text = info != null && info.VirtualDisks.Count > 0
                ? $"SpaceDB/OSPool - {info.VirtualDisks.Count} {Lang.Get("SPACEDBVirtualDisks")}"
                : "SpaceDB/OSPool";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Lang.Get("ErrorOpenPartition")}{ex.Message}", Lang.Get("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenSpaceDBVirtualDisk(PartitionInfo partition, SpaceDBVirtualDisk vdisk)
    {
        try
        {
            if (vdisk.DataOffset == 0 || vdisk.Capacity == 0)
            {
                var r0 = MessageBox.Show(
                    Lang.Format("SPACEDBLocationNotFound"),
                    Lang.Get("SPACEDBOpenTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r0 == DialogResult.Yes)
                    ExtractAndOpenSpaceDB(partition, vdisk);
                return;
            }

            var result = MessageBox.Show(
                Lang.Format("SPACEDBOpenPrompt", vdisk.Name, vdisk.CapacityGB),
                Lang.Get("SPACEDBOpenTitle"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (result == DialogResult.Cancel) return;

            if (result == DialogResult.No)
            {
                ExtractAndOpenSpaceDB(partition, vdisk);
                return;
            }

            fileListView.Items.Clear();
            var loadingItem = fileListView.Items.Add(Lang.Format("OpeningDirect", vdisk.Name));
            loadingItem.ForeColor = Color.Orange;
            fileStatusLabel.Text = "Opening...";
            Application.DoEvents();

            Task.Run(() =>
            {
                try
                {
                    var partitionStream = partition.Open();
                    var vdiskStream = new SpaceDBVirtualDiskStream(partitionStream, vdisk.DataOffset, vdisk.Capacity, vdisk.Extents);
                    var nestedDisk = DiskOpener.OpenDiskFromStream(vdiskStream);
                    
                    this.Invoke(() =>
                    {
                        if (nestedDisk == null)
                        {
                            fileListView.Items.Clear();
                            fileListView.Items.Add(Lang.Get("DirectReadFailed")).ForeColor = Color.Red;
                            var retry = MessageBox.Show(Lang.Get("DirectReadFailed"), Lang.Get("SPACEDBOpenTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                            if (retry == DialogResult.Yes)
                                ExtractAndOpenSpaceDB(partition, vdisk);
                            return;
                        }

                        _diskStack.Push(new NestedDiskInfo(_currentDisk!, _currentFs!, _currentPartition!, _currentPath, _diskPath, null));
                        _nestedDisk = nestedDisk;
                        _currentDisk = nestedDisk;
                        _diskPath = vdisk.Name;
                        _currentFs = null;
                        _currentPartition = null;
                        _currentPath = "";
                        _history.Clear();
                        _historyIndex = -1;

                        var sizeGB = nestedDisk.Capacity / 1024.0 / 1024 / 1024;
                        diskStatusLabel.Text = $"{Lang.Format("NestedDiskLabel", vdisk.Name, "Raw", sizeGB)} {Lang.Get("DirectReadLabel")}";

                        LoadNestedPartitionTree();
                        UpdateNavButtons();
                    });
                }
                catch (Exception ex)
                {
                    this.Invoke(() =>
                    {
                        fileListView.Items.Clear();
                        var errItem = fileListView.Items.Add($"{Lang.Get("DirectReadError")}{ex.Message}");
                        errItem.ForeColor = Color.Red;
                        var retry = MessageBox.Show($"{Lang.Get("DirectReadError")}{ex.Message}\n\n{Lang.Get("DirectReadFailed")}", Lang.Get("SPACEDBOpenTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (retry == DialogResult.Yes)
                            ExtractAndOpenSpaceDB(partition, vdisk);
                    });
                }
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening SpaceDB virtual disk: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExtractAndOpenSpaceDB(PartitionInfo partition, SpaceDBVirtualDisk vdisk)
    {
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"vde_spacedb_{Guid.NewGuid():N}.vhdx");
            fileListView.Items.Clear();
            var extractingItem = fileListView.Items.Add(Lang.Format("Extracting", vdisk.Name));
            extractingItem.ForeColor = Color.Orange;
            fileStatusLabel.Text = "Extracting...";
            Application.DoEvents();

            Task.Run(() =>
            {
                var result = DiskOpener.ExtractSpaceDBVirtualDisk(partition, vdisk.DataOffset, tempPath);
                this.Invoke(() =>
                {
                    if (result == null || !File.Exists(result))
                    {
                        fileListView.Items.Clear();
                        fileListView.Items.Add(Lang.Get("ExtractionFailed")).ForeColor = Color.Red;
                        return;
                    }

                    var nestedDisk = DiskOpener.OpenDisk(result);
                    if (nestedDisk == null)
                    {
                        try { File.Delete(tempPath); } catch { }
                        fileListView.Items.Clear();
                        var failItem = fileListView.Items.Add(Lang.Get("OpenExtractedFailed"));
                        failItem.ForeColor = Color.Red;
                        return;
                    }

                    _diskStack.Push(new NestedDiskInfo(_currentDisk!, _currentFs!, _currentPartition!, _currentPath, _diskPath, _currentTempFile));
                    _nestedDisk = nestedDisk;
                    _currentDisk = nestedDisk;
                    _diskPath = vdisk.Name;
                    _currentFs = null;
                    _currentPartition = null;
                    _currentPath = "";
                    _currentTempFile = tempPath;
                    _history.Clear();
                    _historyIndex = -1;

                    var format = DiskOpener.GetDiskFormat(tempPath);
                    var sizeGB = nestedDisk.Capacity / 1024.0 / 1024 / 1024;
                    diskStatusLabel.Text = $"{Lang.Format("NestedDiskLabel", vdisk.Name, format, sizeGB)} {Lang.Get("ExtractedLabel")}";

                    LoadNestedPartitionTree();
                    UpdateNavButtons();
                });
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Extraction error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenRawDisk()
    {
        try
        {
            _currentFs?.Dispose();
            _currentPartition = null;
            if (_currentDisk != null)
            {
                var stream = _currentDisk.Content;
                stream.Position = 0;
                if (NtfsFileSystem.Detect(stream))
                {
                    stream.Position = 0;
                    _currentFs = new NtfsFileSystem(stream);
                }
                else
                {
                    stream.Position = 0;
                    if (FatFileSystem.Detect(stream))
                    {
                        stream.Position = 0;
                        _currentFs = new FatFileSystem(stream);
                    }
                }
            }
            _currentPath = "";
            if (_currentFs != null)
                NavigateTo("");
            else
            {
                fileListView.Items.Clear();
                fileListView.Items.Add("(No recognized filesystem)").ForeColor = Color.Gray;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenNestedDisk(string vhdPathInFs)
    {
        if (_currentFs == null) return;
        string? tempFile = null;
        try
        {
            var ext = Path.GetExtension(vhdPathInFs).ToLowerInvariant();
            tempFile = Path.Combine(Path.GetTempPath(), $"vde_nested_{Guid.NewGuid():N}{ext}");

            using (var srcStream = _currentFs.OpenFile(vhdPathInFs, FileMode.Open))
            using (var dstStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
            {
                srcStream.CopyTo(dstStream);
            }

            var nestedDisk = DiskOpener.OpenDisk(tempFile);
            if (nestedDisk == null)
            {
                File.Delete(tempFile);
                MessageBox.Show("Failed to open nested disk image.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _diskStack.Push(new NestedDiskInfo(_currentDisk!, _currentFs!, _currentPartition!, _currentPath, _diskPath, _currentTempFile));
            _nestedDisk = nestedDisk;
            _currentDisk = nestedDisk;
            _diskPath = vhdPathInFs;
            _currentFs = null;
            _currentPartition = null;
            _currentPath = "";
            _currentTempFile = tempFile;
            _history.Clear();
            _historyIndex = -1;

            var format = DiskOpener.GetDiskFormat(vhdPathInFs);
            var sizeGB = nestedDisk.Capacity / 1024.0 / 1024.0 / 1024.0;
            diskStatusLabel.Text = $"[Nested] {Path.GetFileName(vhdPathInFs)} [{format}] {sizeGB:F2} GB";

            LoadNestedPartitionTree();
            UpdateNavButtons();
        }
        catch (Exception ex)
        {
            if (tempFile != null && File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
            MessageBox.Show($"Error opening nested disk: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadNestedPartitionTree()
    {
        partitionTree.BeginUpdate();
        partitionTree.Nodes.Clear();

        if (_currentDisk == null) { partitionTree.EndUpdate(); return; }

        var diskNode = partitionTree.Nodes.Add($"[Nested] {Path.GetFileName(_diskPath)}");
        diskNode.Tag = "disk";
        diskNode.ImageIndex = ICON_DISK;
        diskNode.SelectedImageIndex = ICON_DISK;

        var partitions = DiskOpener.GetPartitions(_currentDisk);
        for (int i = 0; i < partitions.Count; i++)
        {
            var p = partitions[i];
            var fsType = DiskOpener.GetFileSystemType(p);
            var sizeMB = p.SectorCount * 512 / 1024.0 / 1024.0;
            string label = "";
            try { if (p is GuidPartitionInfo gpi) label = gpi.Name; } catch { }
            var nodeText = string.IsNullOrEmpty(label)
                ? $"Partition {i + 1} [{fsType}] {sizeMB:F1} MB"
                : $"{label} [{fsType}] {sizeMB:F1} MB";
            var node = diskNode.Nodes.Add(nodeText);
            node.Tag = p;
            node.ImageIndex = ICON_PARTITION;
            node.SelectedImageIndex = ICON_PARTITION;
        }
        diskNode.Expand();
        partitionTree.EndUpdate();
        fileListView.Items.Clear();
    }

    private void CloseNestedDisk()
    {
        if (_diskStack.Count == 0) return;
        var prev = _diskStack.Pop();
        _nestedDisk?.Dispose();
        _nestedDisk = null;
        if (prev.TempFile != null && File.Exists(prev.TempFile))
        {
            try { File.Delete(prev.TempFile); } catch { }
        }
        _currentDisk = prev.Disk;
        _currentFs = prev.Fs;
        _currentPartition = prev.Partition;
        _currentPath = prev.Path;
        _diskPath = prev.DiskPath;
        _currentTempFile = prev.TempFile;
        _history.Clear();
        _historyIndex = -1;

        var format = DiskOpener.GetDiskFormat(_diskPath);
        var sizeGB = _currentDisk.Capacity / 1024.0 / 1024.0 / 1024.0;
        diskStatusLabel.Text = $"{Path.GetFileName(_diskPath)} [{format}] {sizeGB:F2} GB";

        LoadPartitionTree();
        
        if (_currentPartition != null && DiskOpener.IsSpaceDB(_currentPartition))
        {
            OpenSpaceDBPartition(_currentPartition);
        }
        else if (_currentFs != null)
        {
            NavigateTo(_currentPath);
        }
        UpdateNavButtons();
    }

    private void NavigateTo(string path)
    {
        if (_currentFs == null) return;
        _currentPath = path;
        pathTextBox.Text = string.IsNullOrEmpty(path) ? "\\" : path;

        fileListView.BeginUpdate();
        fileListView.Items.Clear();

        try
        {
            var dirs = _currentFs.GetDirectories(path).ToList();
            var files = _currentFs.GetFiles(path).ToList();

            foreach (var dir in dirs)
            {
                var name = Path.GetFileName(dir.TrimEnd('\\'));
                var item = new ListViewItem(name) { Tag = dir, ImageIndex = ICON_FOLDER };
                item.SubItems.Add("");
                item.SubItems.Add("File folder");
                try
                {
                    var info = _currentFs.GetDirectoryInfo(dir);
                    item.SubItems.Add(info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
                }
                catch { item.SubItems.Add(""); }
                fileListView.Items.Add(item);
            }

            long totalSize = 0;
            foreach (var file in files)
            {
                var name = Path.GetFileName(file.TrimEnd('\\'));
                var isVhd = IsVirtualDiskFile(name);
                var item = new ListViewItem(name)
                {
                    Tag = file,
                    ImageIndex = isVhd ? ICON_VHD : ICON_FILE
                };
                try
                {
                    var info = _currentFs.GetFileInfo(file);
                    var size = info.Length;
                    totalSize += size;
                    item.SubItems.Add(FormatSize(size));
                    item.SubItems.Add(isVhd ? "Virtual Disk" : GetFileType(name));
                    item.SubItems.Add(info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
                }
                catch
                {
                    item.SubItems.Add("");
                    item.SubItems.Add(isVhd ? "Virtual Disk" : GetFileType(name));
                    item.SubItems.Add("");
                }
                fileListView.Items.Add(item);
            }

            fileStatusLabel.Text = $"{dirs.Count} folder(s), {files.Count} file(s), {FormatSize(totalSize)}";
        }
        catch (Exception ex)
        {
            fileListView.Items.Add($"Error: {ex.Message}").ForeColor = Color.Red;
        }

        fileListView.EndUpdate();
        AddToHistory(path);
        UpdateNavButtons();
    }

    private void fileListView_DoubleClick(object? sender, EventArgs e)
    {
        if (fileListView.SelectedItems.Count == 0) return;
        var item = fileListView.SelectedItems[0];

        if (item.Tag is SpaceDBVirtualDiskTag vdTag)
        {
            OpenSpaceDBVirtualDisk(vdTag.Partition, vdTag.VirtualDisk);
            return;
        }

        if (item.Tag is string path)
        {
            if (_currentFs != null)
            {
                if (_currentFs.DirectoryExists(path))
                {
                    NavigateTo(path);
                }
                else if (IsVirtualDiskFile(path))
                {
                    OpenNestedDisk(path);
                }
            }
        }
    }

    private void fileListView_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (fileListView.SelectedItems.Count == 0)
        {
            fileStatusLabel.Text = fileListView.Items.Count > 0 ? $"{fileListView.Items.Count} item(s)" : "";
            return;
        }
        var item = fileListView.SelectedItems[0];
        if (item.SubItems.Count > 1 && !string.IsNullOrEmpty(item.SubItems[1].Text))
            fileStatusLabel.Text = $"{item.Text} - {item.SubItems[1].Text}";
        else
            fileStatusLabel.Text = item.Text;
    }

    private void backToolBtn_Click(object? sender, EventArgs e)
    {
        if (_historyIndex > 0)
        {
            _historyIndex--;
            _currentPath = _history[_historyIndex];
            NavigateToNoHistory(_currentPath);
        }
    }

    private void forwardToolBtn_Click(object? sender, EventArgs e)
    {
        if (_historyIndex < _history.Count - 1)
        {
            _historyIndex++;
            _currentPath = _history[_historyIndex];
            NavigateToNoHistory(_currentPath);
        }
    }

    private void upToolBtn_Click(object? sender, EventArgs e)
    {
        if (_diskStack.Count > 0)
        {
            CloseNestedDisk();
            return;
        }
        if (string.IsNullOrEmpty(_currentPath)) return;
        var trimmed = _currentPath.TrimEnd('\\');
        var lastSlash = trimmed.LastIndexOf('\\');
        if (lastSlash <= 0)
            NavigateTo("");
        else
            NavigateTo(trimmed.Substring(0, lastSlash));
    }

    private void NavigateToNoHistory(string path)
    {
        if (_currentFs == null) return;
        _currentPath = path;
        pathTextBox.Text = string.IsNullOrEmpty(path) ? "\\" : path;

        fileListView.BeginUpdate();
        fileListView.Items.Clear();
        try
        {
            foreach (var dir in _currentFs.GetDirectories(path))
            {
                var name = Path.GetFileName(dir.TrimEnd('\\'));
                var item = new ListViewItem(name) { Tag = dir, ImageIndex = ICON_FOLDER };
                item.SubItems.Add("");
                item.SubItems.Add("File folder");
                fileListView.Items.Add(item);
            }
            foreach (var file in _currentFs.GetFiles(path))
            {
                var name = Path.GetFileName(file.TrimEnd('\\'));
                var isVhd = IsVirtualDiskFile(name);
                var item = new ListViewItem(name) { Tag = file, ImageIndex = isVhd ? ICON_VHD : ICON_FILE };
                try
                {
                    var info = _currentFs.GetFileInfo(file);
                    item.SubItems.Add(FormatSize(info.Length));
                    item.SubItems.Add(isVhd ? "Virtual Disk" : GetFileType(name));
                    item.SubItems.Add(info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
                }
                catch { item.SubItems.Add(""); item.SubItems.Add(GetFileType(name)); item.SubItems.Add(""); }
                fileListView.Items.Add(item);
            }
        }
        catch (Exception ex) { fileListView.Items.Add($"Error: {ex.Message}").ForeColor = Color.Red; }
        fileListView.EndUpdate();
        UpdateNavButtons();
    }

    private void AddToHistory(string path)
    {
        if (_historyIndex >= 0 && _history[_historyIndex] == path) return;
        if (_historyIndex < _history.Count - 1)
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        _history.Add(path);
        _historyIndex = _history.Count - 1;
    }

    private void UpdateNavButtons()
    {
        backToolBtn.Enabled = _historyIndex > 0;
        forwardToolBtn.Enabled = _historyIndex < _history.Count - 1;
        upToolBtn.Enabled = !string.IsNullOrEmpty(_currentPath) || _diskStack.Count > 0;
        if (_diskStack.Count > 0)
            upToolBtn.Text = "Up(Disk)";
        else
            upToolBtn.Text = "Up";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }

    private static string GetFileType(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".exe" => "Application",
            ".dll" => "Application extension",
            ".sys" => "System file",
            ".txt" => "Text document",
            ".xml" => "XML document",
            ".json" => "JSON file",
            ".ini" or ".cfg" => "Configuration",
            ".log" => "Log file",
            ".bat" or ".cmd" => "Windows command script",
            ".ps1" => "PowerShell script",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" => "Image",
            ".zip" or ".cab" or ".7z" or ".rar" => "Compressed archive",
            ".ffu" => "Full Flash Update",
            ".wim" or ".esd" => "Windows image",
            _ => string.IsNullOrEmpty(ext) ? "File" : $"{ext.TrimStart('.').ToUpper()} File"
        };
    }

    private void exitMenuItem_Click(object? sender, EventArgs e) => Close();

    private void aboutMenuItem_Click(object? sender, EventArgs e)
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
        var title = new Label { Text = "VirtualDisk Explorer", Font = new System.Drawing.Font("Microsoft YaHei UI", 14F, System.Drawing.FontStyle.Bold), AutoSize = true, Location = new System.Drawing.Point(20, 15) };
        var ver = new Label { Text = "WSK Tools v1.0.2 Preview Build 260824", AutoSize = true, Location = new System.Drawing.Point(22, 45) };
        var preview = new Label { Text = "⚠ 测试版本 — 部分功能可能存在无法正常工作", Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold), ForeColor = System.Drawing.Color.Red, AutoSize = true, Location = new System.Drawing.Point(22, 68) };
        var desc = new Label { Text = "虚拟磁盘浏览器\n支持 VHD/VHDX/VMDK/VDI/QCOW2 格式\n支持分区浏览、存储池识别\n支持文件浏览和嵌套磁盘打开", AutoSize = false, Size = new System.Drawing.Size(420, 100), Location = new System.Drawing.Point(22, 100) };
        var info = new Label { Text = "组织: WinStory 2026\nhttps://wiki.win-story.cn\n编译者: DF4D3110", AutoSize = false, Size = new System.Drawing.Size(420, 80), Location = new System.Drawing.Point(22, 210) };
        var ok = new Button { Text = "确定", Size = new System.Drawing.Size(80, 28), Location = new System.Drawing.Point(180, 290), DialogResult = DialogResult.OK };
        dlg.Controls.AddRange(new Control[] { title, ver, preview, desc, info, ok });
        dlg.AcceptButton = ok;
        dlg.ShowDialog(this);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        CloseAllDisks();
        base.OnFormClosing(e);
    }
}

internal class PartitionNodeTag
{
    public PartitionInfo Partition { get; }
    public bool MarkedAsSpaceDB { get; set; }
    public PartitionNodeTag(PartitionInfo partition) { Partition = partition; }
}

internal class SpaceDBPartitionTag
{
    public PartitionInfo Partition { get; }
    public SpaceDBPartitionTag(PartitionInfo partition) { Partition = partition; }
}

internal class SpaceDBVirtualDiskTag
{
    public PartitionInfo Partition { get; }
    public SpaceDBVirtualDisk VirtualDisk { get; }
    public SpaceDBVirtualDiskTag(PartitionInfo partition, SpaceDBVirtualDisk vdisk)
    {
        Partition = partition;
        VirtualDisk = vdisk;
    }
}

internal class NestedDiskInfo
{
    public VirtualDisk Disk { get; }
    public DiscFileSystem Fs { get; }
    public PartitionInfo Partition { get; }
    public string Path { get; }
    public string DiskPath { get; }
    public string? TempFile { get; }

    public NestedDiskInfo(VirtualDisk disk, DiscFileSystem fs, PartitionInfo partition, string path, string diskPath, string? tempFile = null)
    {
        Disk = disk;
        Fs = fs;
        Partition = partition;
        Path = path;
        DiskPath = diskPath;
        TempFile = tempFile;
    }
}
