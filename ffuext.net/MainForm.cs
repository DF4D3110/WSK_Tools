using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Windows.Forms;

namespace FfuExt
{
    public class MainForm : Form
    {
        private TextBox _txtFfuPath = null!;
        private ComboBox _cmbDisks = null!;
        private Button _btnStart = null!;
        private ProgressBar _progressBar = null!;
        private TextBox _txtOutput = null!;
        private StatusStrip _statusStrip = null!;
        private ToolStripStatusLabel _statusLabel = null!;
        private bool _isApplying;

        public MainForm()
        {
            Text = "FFU Apply Tool - WSK Tools v1.0.4";
            ClientSize = new Size(700, 500);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.White;

            InitUI();
            Load += (s, e) => RefreshDisks();
        }

        private void InitUI()
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 140, Padding = new Padding(10) };

            // FFU Path
            var lblFfu = new Label { Text = "FFU File:", Location = new Point(10, 15), Width = 70 };
            _txtFfuPath = new TextBox { Location = new Point(80, 12), Width = 520 };
            var btnBrowse = new Button { Text = "Browse", Location = new Point(605, 11), Width = 75 };
            btnBrowse.Click += (s, e) => BrowseFfu();

            // Disk selection
            var lblDisk = new Label { Text = "Target Disk:", Location = new Point(10, 45), Width = 70 };
            _cmbDisks = new ComboBox { Location = new Point(80, 42), Width = 400, DropDownStyle = ComboBoxStyle.DropDownList };
            var btnRefresh = new Button { Text = "Refresh", Location = new Point(485, 41), Width = 75 };
            btnRefresh.Click += (s, e) => RefreshDisks();

            // Start button
            _btnStart = new Button
            {
                Text = "Apply FFU",
                Location = new Point(10, 75),
                Width = 120,
                Height = 35,
                BackColor = Color.FromArgb(200, 50, 50),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            _btnStart.Click += (s, e) => ApplyFfu();

            // Warning label
            var lblWarning = new Label
            {
                Text = "WARNING: This will erase all data on the target disk!",
                Location = new Point(140, 82),
                Width = 500,
                ForeColor = Color.Red,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            panel.Controls.AddRange(new Control[] {
                lblFfu, _txtFfuPath, btnBrowse,
                lblDisk, _cmbDisks, btnRefresh,
                _btnStart, lblWarning
            });

            // Progress bar
            _progressBar = new ProgressBar { Dock = DockStyle.Top, Height = 20, Minimum = 0, Maximum = 100 };

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
            Controls.Add(_progressBar);
            Controls.Add(panel);
            Controls.Add(_statusStrip);
        }

        private void BrowseFfu()
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "FFU Files|*.ffu|All Files|*.*",
                Title = "Select FFU image file"
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _txtFfuPath.Text = dlg.FileName;
        }

        private void RefreshDisks()
        {
            _cmbDisks.Items.Clear();
            _statusLabel.Text = "Refreshing disk list...";
            Application.DoEvents();

            try
            {
                // Use WMI to get physical disks
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
                foreach (ManagementObject disk in searcher.Get())
                {
                    string index = disk["Index"]?.ToString() ?? "?";
                    string model = disk["Model"]?.ToString() ?? "Unknown";
                    string size = disk["Size"]?.ToString() ?? "0";
                    long sizeBytes = long.TryParse(size, out long s) ? s : 0;
                    string sizeStr = FormatSize(sizeBytes);
                    string item = $"Disk {index}: {model} ({sizeStr})";
                    _cmbDisks.Items.Add(item);
                }

                if (_cmbDisks.Items.Count > 0)
                    _cmbDisks.SelectedIndex = 0;

                _statusLabel.Text = $"Found {_cmbDisks.Items.Count} disk(s)";
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Error refreshing disks: {ex.Message}";
                MessageBox.Show($"Error refreshing disk list: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private void ApplyFfu()
        {
            if (_isApplying)
            {
                MessageBox.Show("FFU application is already in progress.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string ffuPath = _txtFfuPath.Text.Trim();
            if (string.IsNullOrEmpty(ffuPath) || !File.Exists(ffuPath))
            {
                MessageBox.Show("Please select a valid FFU file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (_cmbDisks.SelectedIndex < 0)
            {
                MessageBox.Show("Please select a target disk.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Get disk index
            string selectedDisk = _cmbDisks.SelectedItem?.ToString() ?? "";
            int diskIndex = -1;
            if (selectedDisk.StartsWith("Disk "))
            {
                int colonIdx = selectedDisk.IndexOf(':');
                if (colonIdx > 5)
                    int.TryParse(selectedDisk.Substring(5, colonIdx - 5), out diskIndex);
            }

            if (diskIndex < 0)
            {
                MessageBox.Show("Could not determine target disk index.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Confirm
            var result = MessageBox.Show(
                $"Are you sure you want to apply FFU to {selectedDisk}?\n\nThis will ERASE ALL DATA on this disk!",
                "Confirm FFU Application",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (result != DialogResult.Yes)
                return;

            _isApplying = true;
            _btnStart.Enabled = false;
            _cmbDisks.Enabled = false;
            _txtOutput.Clear();
            _progressBar.Value = 0;
            _statusLabel.Text = "Applying FFU...";

            var worker = new System.ComponentModel.BackgroundWorker { WorkerSupportsCancellation = false };
            worker.DoWork += (s, e) =>
            {
                try
                {
                    AppendOutput("=== FFU Apply ===");
                    AppendOutput($"FFU: {ffuPath}");
                    AppendOutput($"Target: {selectedDisk}");
                    AppendOutput("");

                    // Try using dism.exe to apply FFU
                    string dismPath = FindDism();
                    if (!string.IsNullOrEmpty(dismPath))
                    {
                        AppendOutput($"Using DISM: {dismPath}");
                        string arguments = $"/Apply-FFU /ImageFile:\"{ffuPath}\" /ApplyDrive:\\\\.\\PhysicalDrive{diskIndex}";

                        var psi = new ProcessStartInfo
                        {
                            FileName = dismPath,
                            Arguments = arguments,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                            Verb = "runas"
                        };

                        var proc = Process.Start(psi);
                        while (!proc!.StandardOutput.EndOfStream)
                        {
                            string line = proc.StandardOutput.ReadLine()!;
                            if (!string.IsNullOrEmpty(line))
                            {
                                AppendOutput(line);
                                // Try to parse progress
                                if (line.Contains('%'))
                                {
                                    int pct = ExtractPercentage(line);
                                    if (pct >= 0)
                                        UpdateProgress(pct);
                                }
                            }
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
                    else
                    {
                        AppendOutput("ERROR: DISM not found. Please install Windows ADK.");
                        e.Result = 1;
                    }
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
                    UpdateProgress(100);
                    AppendOutput("");
                    AppendOutput("=== FFU APPLY SUCCESSFUL ===");
                    _statusLabel.Text = "FFU applied successfully";
                    MessageBox.Show("FFU image applied successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    AppendOutput("");
                    AppendOutput($"=== FFU APPLY FAILED (exit code: {exitCode}) ===");
                    _statusLabel.Text = $"FFU apply failed (exit code: {exitCode})";
                    MessageBox.Show($"FFU application failed with exit code {exitCode}.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                _isApplying = false;
                _btnStart.Enabled = true;
                _cmbDisks.Enabled = true;
            };

            worker.RunWorkerAsync();
        }

        private string FindDism()
        {
            // Check common locations
            string[] paths = {
                @"C:\Windows\System32\dism.exe",
                @"C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\amd64\DISM\dism.exe",
                @"C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\x86\DISM\dism.exe"
            };

            foreach (string path in paths)
            {
                if (File.Exists(path))
                    return path;
            }

            // Try PATH
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "where.exe",
                    Arguments = "dism.exe",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                var proc = Process.Start(psi);
                string output = proc!.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                if (proc.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length > 0 && File.Exists(lines[0]))
                        return lines[0];
                }
            }
            catch { }

            return "";
        }

        private int ExtractPercentage(string line)
        {
            try
            {
                int pctIdx = line.IndexOf('%');
                if (pctIdx > 0)
                {
                    int start = pctIdx - 1;
                    while (start >= 0 && (char.IsDigit(line[start]) || line[start] == '.'))
                        start--;
                    start++;
                    string numStr = line.Substring(start, pctIdx - start);
                    if (double.TryParse(numStr, out double pct))
                        return (int)Math.Min(100, Math.Max(0, pct));
                }
            }
            catch { }
            return -1;
        }

        private void UpdateProgress(int value)
        {
            if (_progressBar.InvokeRequired)
            {
                _progressBar.Invoke((Action)(() => UpdateProgress(value)));
                return;
            }
            _progressBar.Value = Math.Min(100, Math.Max(0, value));
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
