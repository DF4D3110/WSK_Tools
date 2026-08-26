using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace WskTool
{
    public class MainForm : Form
    {
        private TextBox _txtWskPath = null!;
        private TextBox _txtWorkspace = null!;
        private ComboBox _cmbArch = null!;
        private ComboBox _cmbProduct = null!;
        private RadioButton _radPhys = null!;
        private RadioButton _radVM = null!;
        private Button _btnBuild = null!;
        private TextBox _txtOutput = null!;
        private StatusStrip _statusStrip = null!;
        private ToolStripStatusLabel _statusLabel = null!;
        private bool _isBuilding;

        public MainForm()
        {
            Text = "WSK Tool - WSK Tools v1.0.4";
            ClientSize = new Size(800, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.White;

            InitUI();
            Load += (s, e) => DetectWskLocation();
        }

        private void InitUI()
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 200, Padding = new Padding(10) };

            // WSK Path
            var lblWsk = new Label { Text = "WSK Path:", Location = new Point(10, 15), Width = 80 };
            _txtWskPath = new TextBox { Location = new Point(90, 12), Width = 580 };
            var btnBrowseWsk = new Button { Text = "...", Location = new Point(675, 11), Width = 30 };
            btnBrowseWsk.Click += (s, e) => BrowseWskPath();
            var btnDetect = new Button { Text = "Detect", Location = new Point(710, 11), Width = 70 };
            btnDetect.Click += (s, e) => DetectWskLocation();

            // Workspace
            var lblWorkspace = new Label { Text = "Workspace:", Location = new Point(10, 45), Width = 80 };
            _txtWorkspace = new TextBox { Location = new Point(90, 42), Width = 580 };
            var btnBrowseWs = new Button { Text = "...", Location = new Point(675, 41), Width = 30 };
            btnBrowseWs.Click += (s, e) => BrowseWorkspace();

            // Architecture
            var lblArch = new Label { Text = "Architecture:", Location = new Point(10, 75), Width = 80 };
            _cmbArch = new ComboBox { Location = new Point(90, 72), Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbArch.Items.AddRange(new object[] { "amd64", "arm64", "x86", "arm" });
            _cmbArch.SelectedIndex = 0;

            // Product
            var lblProduct = new Label { Text = "Product:", Location = new Point(260, 75), Width = 60 };
            _cmbProduct = new ComboBox { Location = new Point(320, 72), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbProduct.Items.AddRange(new object[] { "FactoryOS", "AndromedaOS", "WindowsCoreOS" });
            _cmbProduct.SelectedIndex = 0;

            // Physical/VM
            var lblMode = new Label { Text = "Mode:", Location = new Point(540, 75), Width = 50 };
            _radPhys = new RadioButton { Text = "Physical", Location = new Point(590, 73), Width = 80 };
            _radVM = new RadioButton { Text = "VM", Location = new Point(670, 73), Width = 60, Checked = true };

            // Build button
            _btnBuild = new Button
            {
                Text = "Build Image",
                Location = new Point(10, 105),
                Width = 120,
                Height = 35,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            _btnBuild.Click += (s, e) => BuildImage();

            panel.Controls.AddRange(new Control[] {
                lblWsk, _txtWskPath, btnBrowseWsk, btnDetect,
                lblWorkspace, _txtWorkspace, btnBrowseWs,
                lblArch, _cmbArch, lblProduct, _cmbProduct,
                lblMode, _radPhys, _radVM, _btnBuild
            });

            // Output
            _txtOutput = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font("Consolas", 9F),
                BackColor = Color.Black,
                ForeColor = Color.LimeGreen
            };

            // Status bar
            _statusStrip = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel("Ready");
            _statusStrip.Items.Add(_statusLabel);

            Controls.Add(_txtOutput);
            Controls.Add(panel);
            Controls.Add(_statusStrip);
        }

        private void DetectWskLocation()
        {
            _statusLabel.Text = "Detecting WSK installation...";
            Application.DoEvents();

            // Check common locations
            string[] commonPaths = {
                @"C:\Program Files (x86)\Windows Kits\10\WSK",
                @"C:\Program Files\Windows Kits\10\WSK",
                @"D:\WSK",
                @"E:\WSK",
                @"E:\WSK_Tools"
            };

            foreach (string path in commonPaths)
            {
                if (Directory.Exists(path) && File.Exists(Path.Combine(path, "Version.txt")))
                {
                    _txtWskPath.Text = path;
                    string version = GetWskVersion(path);
                    _statusLabel.Text = $"WSK found: {version}";
                    return;
                }
            }

            // Search all drives
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Removable)
                {
                    try
                    {
                        string wskPath = Path.Combine(drive.RootDirectory.FullName, "WSK");
                        if (Directory.Exists(wskPath) && File.Exists(Path.Combine(wskPath, "Version.txt")))
                        {
                            _txtWskPath.Text = wskPath;
                            string version = GetWskVersion(wskPath);
                            _statusLabel.Text = $"WSK found: {version}";
                            return;
                        }
                    }
                    catch { }
                }
            }

            _statusLabel.Text = "WSK not found automatically. Please browse manually.";
        }

        private string GetWskVersion(string wskRoot)
        {
            try
            {
                string versionFile = Path.Combine(wskRoot, "Version.txt");
                if (File.Exists(versionFile))
                    return File.ReadAllText(versionFile).Trim();
            }
            catch { }
            return "Unknown";
        }

        private void BrowseWskPath()
        {
            using var dlg = new FolderBrowserDialog { Description = "Select WSK installation directory" };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _txtWskPath.Text = dlg.SelectedPath;
        }

        private void BrowseWorkspace()
        {
            using var dlg = new FolderBrowserDialog { Description = "Select workspace directory" };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _txtWorkspace.Text = dlg.SelectedPath;
        }

        private void BuildImage()
        {
            if (_isBuilding)
            {
                MessageBox.Show("Build is already in progress.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string wskPath = _txtWskPath.Text.Trim();
            if (string.IsNullOrEmpty(wskPath) || !Directory.Exists(wskPath))
            {
                MessageBox.Show("Please select a valid WSK path.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string workspace = _txtWorkspace.Text.Trim();
            if (string.IsNullOrEmpty(workspace))
            {
                MessageBox.Show("Please select a workspace directory.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string arch = _cmbArch.SelectedItem?.ToString() ?? "amd64";
            string product = _cmbProduct.SelectedItem?.ToString() ?? "FactoryOS";
            bool isVM = _radVM.Checked;

            _isBuilding = true;
            _btnBuild.Enabled = false;
            _txtOutput.Clear();
            _statusLabel.Text = "Building...";

            var worker = new System.ComponentModel.BackgroundWorker { WorkerSupportsCancellation = false };
            worker.DoWork += (s, e) =>
            {
                try
                {
                    // Find prepcmd.cmd
                    string prepCmd = Path.Combine(wskPath, "prepcmd.cmd");
                    if (!File.Exists(prepCmd))
                    {
                        // Try common subdirectories
                        string[] subDirs = { "Tools", "bin", "prep" };
                        foreach (string sub in subDirs)
                        {
                            string testPath = Path.Combine(wskPath, sub, "prepcmd.cmd");
                            if (File.Exists(testPath))
                            {
                                prepCmd = testPath;
                                break;
                            }
                        }
                    }

                    if (!File.Exists(prepCmd))
                    {
                        AppendOutput("ERROR: prepcmd.cmd not found in WSK directory.");
                        e.Result = 1;
                        return;
                    }

                    // Build command arguments
                    string vmFlag = isVM ? "-VM" : "";
                    string arguments = $"/c \"\"{prepCmd}\" -wsk \"{wskPath}\" -ws \"{workspace}\" -arch {arch} -product {product} {vmFlag}\"";

                    AppendOutput($"=== WSK Build ===");
                    AppendOutput($"WSK: {wskPath}");
                    AppendOutput($"Workspace: {workspace}");
                    AppendOutput($"Architecture: {arch}");
                    AppendOutput($"Product: {product}");
                    AppendOutput($"Mode: {(isVM ? "VM" : "Physical")}");
                    AppendOutput("");

                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WorkingDirectory = workspace
                    };

                    var proc = Process.Start(psi);
                    while (!proc!.StandardOutput.EndOfStream)
                    {
                        string line = proc.StandardOutput.ReadLine()!;
                        if (!string.IsNullOrEmpty(line))
                            AppendOutput(line);
                    }
                    while (!proc.StandardError.EndOfStream)
                    {
                        string line = proc.StandardError.ReadLine()!;
                        if (!string.IsNullOrEmpty(line))
                            AppendOutput($"ERROR: {line}");
                    }
                    proc.WaitForExit();
                    e.Result = proc.ExitCode;
                }
                catch (Exception ex)
                {
                    AppendOutput($"EXCEPTION: {ex.Message}");
                    e.Result = -1;
                }
            };

            worker.RunWorkerCompleted += (s, e) =>
            {
                int exitCode = (int)(e.Result ?? -1);
                if (exitCode == 0)
                {
                    AppendOutput("");
                    AppendOutput("=== BUILD SUCCESSFUL ===");
                    _statusLabel.Text = "Build completed successfully";
                    MessageBox.Show("WSK build completed successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    AppendOutput("");
                    AppendOutput($"=== BUILD FAILED (exit code: {exitCode}) ===");
                    _statusLabel.Text = $"Build failed (exit code: {exitCode})";
                    MessageBox.Show($"WSK build failed with exit code {exitCode}.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                _isBuilding = false;
                _btnBuild.Enabled = true;
            };

            worker.RunWorkerAsync();
        }

        private void AppendOutput(string text)
        {
            if (_txtOutput.InvokeRequired)
            {
                _txtOutput.Invoke((Action)(() => AppendOutput(text)));
                return;
            }
            _txtOutput.AppendText(text + Environment.NewLine);
            _txtOutput.ScrollToCaret();
        }
    }
}
