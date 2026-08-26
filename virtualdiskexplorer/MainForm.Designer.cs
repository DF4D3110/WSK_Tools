namespace VirtualDiskExplorer;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;
    private System.Windows.Forms.MenuStrip menuStrip;
    private System.Windows.Forms.ToolStripMenuItem fileMenu;
    private System.Windows.Forms.ToolStripMenuItem openMenuItem;
    private System.Windows.Forms.ToolStripSeparator fileSep1;
    private System.Windows.Forms.ToolStripMenuItem exitMenuItem;
    private System.Windows.Forms.ToolStripMenuItem helpMenu;
    private System.Windows.Forms.ToolStripMenuItem aboutMenuItem;
    private System.Windows.Forms.ToolStrip toolStrip;
    private System.Windows.Forms.ToolStripButton openToolBtn;
    private System.Windows.Forms.ToolStripButton backToolBtn;
    private System.Windows.Forms.ToolStripButton forwardToolBtn;
    private System.Windows.Forms.ToolStripButton upToolBtn;
    private System.Windows.Forms.ToolStripSeparator toolSep1;
    private System.Windows.Forms.ToolStripLabel pathLabel;
    private System.Windows.Forms.ToolStripTextBox pathTextBox;
    private System.Windows.Forms.SplitContainer splitContainer;
    private System.Windows.Forms.TreeView partitionTree;
    private System.Windows.Forms.ListView fileListView;
    private System.Windows.Forms.ColumnHeader nameColumn;
    private System.Windows.Forms.ColumnHeader sizeColumn;
    private System.Windows.Forms.ColumnHeader typeColumn;
    private System.Windows.Forms.ColumnHeader modifiedColumn;
    private System.Windows.Forms.StatusStrip statusStrip;
    private System.Windows.Forms.ToolStripStatusLabel diskStatusLabel;
    private System.Windows.Forms.ToolStripStatusLabel fileStatusLabel;
    private System.Windows.Forms.OpenFileDialog openFileDialog;
    private System.Windows.Forms.ImageList fileImageList;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();
        this.menuStrip = new System.Windows.Forms.MenuStrip();
        this.fileMenu = new System.Windows.Forms.ToolStripMenuItem();
        this.openMenuItem = new System.Windows.Forms.ToolStripMenuItem();
        this.fileSep1 = new System.Windows.Forms.ToolStripSeparator();
        this.exitMenuItem = new System.Windows.Forms.ToolStripMenuItem();
        this.helpMenu = new System.Windows.Forms.ToolStripMenuItem();
        this.aboutMenuItem = new System.Windows.Forms.ToolStripMenuItem();
        this.toolStrip = new System.Windows.Forms.ToolStrip();
        this.openToolBtn = new System.Windows.Forms.ToolStripButton();
        this.backToolBtn = new System.Windows.Forms.ToolStripButton();
        this.forwardToolBtn = new System.Windows.Forms.ToolStripButton();
        this.upToolBtn = new System.Windows.Forms.ToolStripButton();
        this.toolSep1 = new System.Windows.Forms.ToolStripSeparator();
        this.pathLabel = new System.Windows.Forms.ToolStripLabel();
        this.pathTextBox = new System.Windows.Forms.ToolStripTextBox();
        this.splitContainer = new System.Windows.Forms.SplitContainer();
        this.partitionTree = new System.Windows.Forms.TreeView();
        this.fileListView = new System.Windows.Forms.ListView();
        this.nameColumn = new System.Windows.Forms.ColumnHeader();
        this.sizeColumn = new System.Windows.Forms.ColumnHeader();
        this.typeColumn = new System.Windows.Forms.ColumnHeader();
        this.modifiedColumn = new System.Windows.Forms.ColumnHeader();
        this.statusStrip = new System.Windows.Forms.StatusStrip();
        this.diskStatusLabel = new System.Windows.Forms.ToolStripStatusLabel();
        this.fileStatusLabel = new System.Windows.Forms.ToolStripStatusLabel();
        this.openFileDialog = new System.Windows.Forms.OpenFileDialog();
        this.fileImageList = new System.Windows.Forms.ImageList(this.components);
        this.menuStrip.SuspendLayout();
        this.toolStrip.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).BeginInit();
        this.splitContainer.Panel1.SuspendLayout();
        this.splitContainer.Panel2.SuspendLayout();
        this.splitContainer.SuspendLayout();
        this.statusStrip.SuspendLayout();
        this.SuspendLayout();

        this.menuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { this.fileMenu, this.helpMenu });
        this.menuStrip.Location = new System.Drawing.Point(0, 0);
        this.menuStrip.Name = "menuStrip";
        this.menuStrip.Size = new System.Drawing.Size(1000, 24);
        this.menuStrip.TabIndex = 0;

        this.fileMenu.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { this.openMenuItem, this.fileSep1, this.exitMenuItem });
        this.fileMenu.Name = "fileMenu";
        this.fileMenu.Size = new System.Drawing.Size(37, 20);
        this.fileMenu.Text = "&File";

        this.openMenuItem.Name = "openMenuItem";
        this.openMenuItem.ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.O;
        this.openMenuItem.Size = new System.Drawing.Size(150, 22);
        this.openMenuItem.Text = "&Open Disk...";
        this.openMenuItem.Click += new System.EventHandler(this.openMenuItem_Click);

        this.fileSep1.Name = "fileSep1";
        this.fileSep1.Size = new System.Drawing.Size(147, 6);

        this.exitMenuItem.Name = "exitMenuItem";
        this.exitMenuItem.Size = new System.Drawing.Size(150, 22);
        this.exitMenuItem.Text = "E&xit";
        this.exitMenuItem.Click += new System.EventHandler(this.exitMenuItem_Click);

        this.helpMenu.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { this.aboutMenuItem });
        this.helpMenu.Name = "helpMenu";
        this.helpMenu.Size = new System.Drawing.Size(44, 20);
        this.helpMenu.Text = "&Help";

        this.aboutMenuItem.Name = "aboutMenuItem";
        this.aboutMenuItem.Size = new System.Drawing.Size(150, 22);
        this.aboutMenuItem.Text = "&About";
        this.aboutMenuItem.Click += new System.EventHandler(this.aboutMenuItem_Click);

        this.toolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
        this.toolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { this.openToolBtn, this.backToolBtn, this.forwardToolBtn, this.upToolBtn, this.toolSep1, this.pathLabel, this.pathTextBox });
        this.toolStrip.Location = new System.Drawing.Point(0, 24);
        this.toolStrip.Name = "toolStrip";
        this.toolStrip.Size = new System.Drawing.Size(1000, 25);
        this.toolStrip.TabIndex = 1;

        this.openToolBtn.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
        this.openToolBtn.Name = "openToolBtn";
        this.openToolBtn.Size = new System.Drawing.Size(40, 22);
        this.openToolBtn.Text = "Open";
        this.openToolBtn.Click += new System.EventHandler(this.openMenuItem_Click);

        this.backToolBtn.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
        this.backToolBtn.Enabled = false;
        this.backToolBtn.Name = "backToolBtn";
        this.backToolBtn.Size = new System.Drawing.Size(23, 22);
        this.backToolBtn.Text = "Back";
        this.backToolBtn.Click += new System.EventHandler(this.backToolBtn_Click);

        this.forwardToolBtn.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
        this.forwardToolBtn.Enabled = false;
        this.forwardToolBtn.Name = "forwardToolBtn";
        this.forwardToolBtn.Size = new System.Drawing.Size(23, 22);
        this.forwardToolBtn.Text = "Forward";
        this.forwardToolBtn.Click += new System.EventHandler(this.forwardToolBtn_Click);

        this.upToolBtn.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
        this.upToolBtn.Enabled = false;
        this.upToolBtn.Name = "upToolBtn";
        this.upToolBtn.Size = new System.Drawing.Size(23, 22);
        this.upToolBtn.Text = "Up";
        this.upToolBtn.Click += new System.EventHandler(this.upToolBtn_Click);

        this.toolSep1.Name = "toolSep1";
        this.toolSep1.Size = new System.Drawing.Size(6, 25);

        this.pathLabel.Name = "pathLabel";
        this.pathLabel.Size = new System.Drawing.Size(37, 22);
        this.pathLabel.Text = "Path:";

        this.pathTextBox.Name = "pathTextBox";
        this.pathTextBox.ReadOnly = true;
        this.pathTextBox.Size = new System.Drawing.Size(700, 25);
        this.pathTextBox.Text = "";

        this.splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
        this.splitContainer.Location = new System.Drawing.Point(0, 49);
        this.splitContainer.Name = "splitContainer";
        this.splitContainer.Panel1.Controls.Add(this.partitionTree);
        this.splitContainer.Panel2.Controls.Add(this.fileListView);
        this.splitContainer.Size = new System.Drawing.Size(1000, 501);
        this.splitContainer.SplitterDistance = 250;
        this.splitContainer.TabIndex = 2;

        this.partitionTree.Dock = System.Windows.Forms.DockStyle.Fill;
        this.partitionTree.Location = new System.Drawing.Point(0, 0);
        this.partitionTree.Name = "partitionTree";
        this.partitionTree.Size = new System.Drawing.Size(250, 501);
        this.partitionTree.ImageList = this.fileImageList;
        this.partitionTree.TabIndex = 0;
        this.partitionTree.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.partitionTree_AfterSelect);

        this.fileListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] { this.nameColumn, this.sizeColumn, this.typeColumn, this.modifiedColumn });
        this.fileListView.Dock = System.Windows.Forms.DockStyle.Fill;
        this.fileListView.FullRowSelect = true;
        this.fileListView.Location = new System.Drawing.Point(0, 0);
        this.fileListView.Name = "fileListView";
        this.fileListView.Size = new System.Drawing.Size(746, 501);
        this.fileListView.SmallImageList = this.fileImageList;
        this.fileListView.TabIndex = 0;
        this.fileListView.UseCompatibleStateImageBehavior = false;
        this.fileListView.View = System.Windows.Forms.View.Details;
        this.fileListView.DoubleClick += new System.EventHandler(this.fileListView_DoubleClick);
        this.fileListView.SelectedIndexChanged += new System.EventHandler(this.fileListView_SelectedIndexChanged);

        this.nameColumn.Text = "Name";
        this.nameColumn.Width = 250;
        this.sizeColumn.Text = "Size";
        this.sizeColumn.Width = 100;
        this.typeColumn.Text = "Type";
        this.typeColumn.Width = 100;
        this.modifiedColumn.Text = "Modified";
        this.modifiedColumn.Width = 150;

        this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { this.diskStatusLabel, this.fileStatusLabel });
        this.statusStrip.Location = new System.Drawing.Point(0, 550);
        this.statusStrip.Name = "statusStrip";
        this.statusStrip.Size = new System.Drawing.Size(1000, 22);
        this.statusStrip.TabIndex = 3;

        this.diskStatusLabel.Name = "diskStatusLabel";
        this.diskStatusLabel.Size = new System.Drawing.Size(200, 17);
        this.diskStatusLabel.Text = "No disk opened";
        this.diskStatusLabel.Spring = true;
        this.diskStatusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

        this.fileStatusLabel.Name = "fileStatusLabel";
        this.fileStatusLabel.Size = new System.Drawing.Size(200, 17);
        this.fileStatusLabel.Text = "";
        this.fileStatusLabel.Spring = true;
        this.fileStatusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

        this.openFileDialog.Filter = "Virtual Disks|*.vhd;*.vhdx;*.vmdk;*.vdi;*.qcow;*.qcow2;*.raw;*.img;*.dd|All files|*.*";
        this.openFileDialog.Title = "Open Virtual Disk";

        this.fileImageList.ColorDepth = System.Windows.Forms.ColorDepth.Depth32Bit;
        this.fileImageList.ImageSize = new System.Drawing.Size(16, 16);
        this.fileImageList.TransparentColor = System.Drawing.Color.Transparent;

        this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(1000, 572);
        this.Controls.Add(this.splitContainer);
        this.Controls.Add(this.toolStrip);
        this.Controls.Add(this.menuStrip);
        this.Controls.Add(this.statusStrip);
        this.MainMenuStrip = this.menuStrip;
        this.Name = "MainForm";
        this.Text = "Virtual Disk Explorer - WinStory 2026";
        this.menuStrip.ResumeLayout(false);
        this.menuStrip.PerformLayout();
        this.toolStrip.ResumeLayout(false);
        this.toolStrip.PerformLayout();
        this.splitContainer.Panel1.ResumeLayout(false);
        this.splitContainer.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).EndInit();
        this.splitContainer.ResumeLayout(false);
        this.statusStrip.ResumeLayout(false);
        this.statusStrip.PerformLayout();
        this.ResumeLayout(false);
        this.PerformLayout();
    }
}
