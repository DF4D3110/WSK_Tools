using System;
using System.Drawing;
using System.Windows.Forms;

namespace WSKTools.Common
{
    public class AboutDialog : Form
    {
        public AboutDialog(string appName, string description, string extraInfo = "")
        {
            Text = "关于 " + appName;
            Size = new Size(480, 380);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };

            var titleLabel = new Label
            {
                Text = appName,
                Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(20, 15)
            };

            var versionLabel = new Label
            {
                Text = "WSK Tools v1.0.4 Preview Build 260826",
                Font = new Font("Microsoft YaHei UI", 9F),
                AutoSize = true,
                Location = new Point(22, 45)
            };

            var previewLabel = new Label
            {
                Text = "⚠ 测试版本 — 部分功能可能存在无法正常工作",
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = Color.Red,
                AutoSize = true,
                Location = new Point(22, 68)
            };

            var descLabel = new Label
            {
                Text = description,
                Font = new Font("Microsoft YaHei UI", 9F),
                AutoSize = false,
                Size = new Size(420, 120),
                Location = new Point(22, 100)
            };

            var infoLabel = new Label
            {
                Text = extraInfo + "\n\n组织: WinStory 2026\nhttps://wiki.win-story.cn\n编译者: DF4D3110",
                Font = new Font("Microsoft YaHei UI", 9F),
                AutoSize = false,
                Size = new Size(420, 100),
                Location = new Point(22, 220)
            };

            var okButton = new Button
            {
                Text = "确定",
                Size = new Size(80, 28),
                Location = new Point(180, 310),
                DialogResult = DialogResult.OK
            };

            panel.Controls.Add(titleLabel);
            panel.Controls.Add(versionLabel);
            panel.Controls.Add(previewLabel);
            panel.Controls.Add(descLabel);
            panel.Controls.Add(infoLabel);
            panel.Controls.Add(okButton);
            Controls.Add(panel);
            AcceptButton = okButton;
        }
    }
}
