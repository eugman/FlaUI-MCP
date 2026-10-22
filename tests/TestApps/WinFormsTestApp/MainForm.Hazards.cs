using System;
using System.Drawing;
using System.Windows.Forms;

namespace WinFormsTestApp;

/// <summary>
/// Tab 6: controls that punish the wrong input. A tree that renames on a click of its selected row, a tree that
/// ignores or delays Select, a button under a sibling overlay, controls with no Value pattern, a menu, a tool
/// window and a borderless window over every screen. The integration tests check that the tools refuse or route
/// around each one rather than sending input that lands somewhere else.
/// </summary>
public partial class MainForm
{
    private TabPage CreateHazardsTab()
    {
        var tab = new TabPage("Hazards") { Name = "HazardsTab" };
        var canvas = new Panel
        {
            Name = "HazardsCanvas",
            Dock = DockStyle.Fill,
            AutoScroll = true,
        };
        tab.Controls.Add(canvas);
        tab.Controls.Add(CreateHazardMenu(canvas));

        AddRenameTree(canvas);
        AddStubbornTree(canvas);
        AddCoveredButton(canvas);
        AddNoValueControls(canvas);
        AddWindowLaunchers(canvas);
        return tab;
    }

    private static Label StatusLabel(string name, string text, Point location) =>
        new()
        {
            Name = name,
            Text = text,
            Location = location,
            AutoSize = true,
        };

    private static MenuStrip CreateHazardMenu(Control canvas)
    {
        var status = StatusLabel("MenuStatusLabel", "Menu: none", new Point(400, 10));
        canvas.Controls.Add(status);
        var file = new ToolStripMenuItem("File") { Name = "FileMenu" };
        file.DropDownItems.Add(
            new ToolStripMenuItem("New", null, (_, _) => status.Text = "Menu: New") { Name = "NewMenuItem" }
        );
        file.DropDownItems.Add(
            new ToolStripMenuItem("Rename Mode") { Name = "RenameModeMenuItem", CheckOnClick = true }
        );
        file.DropDownItems.Add(new ToolStripMenuItem("Unavailable") { Name = "UnavailableMenuItem", Enabled = false });
        return new MenuStrip { Name = "HazardMenu", Items = { file } };
    }

    /// <summary>A tree with inline rename: a click on its selected row starts editing that row's label.</summary>
    private static void AddRenameTree(Control canvas)
    {
        var status = StatusLabel("RenameStatusLabel", "Not renaming", new Point(10, 105));
        var tree = new TreeView
        {
            Name = "RenameTreeView",
            AccessibleName = "Rename Tree",
            LabelEdit = true,
            HideSelection = false,
            Location = new Point(10, 10),
            Size = new Size(180, 90),
        };
        tree.Nodes.Add("Rename Me");
        tree.Nodes.Add("Second Node");
        tree.BeforeLabelEdit += (_, e) => status.Text = $"Renaming {e.Node?.Text}";
        tree.AfterLabelEdit += (_, e) => status.Text = $"Renamed to {e.Label ?? e.Node?.Text}";
        canvas.Controls.Add(tree);
        canvas.Controls.Add(status);
    }

    /// <summary>A tree whose rows refuse Select ("Ignores Select") or take it only after a second.</summary>
    private static void AddStubbornTree(Control canvas)
    {
        var status = StatusLabel("StubbornStatusLabel", "Selected: none", new Point(200, 105));
        var tree = new TreeView
        {
            Name = "StubbornTreeView",
            AccessibleName = "Stubborn Tree",
            HideSelection = false,
            Location = new Point(200, 10),
            Size = new Size(180, 90),
        };
        tree.Nodes.Add("Ignores Select");
        var delayed = tree.Nodes.Add("Delays Select");
        tree.Nodes.Add("Plain Node");
        var allowDelayed = false;
        var timer = new Timer { Interval = 1000 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            allowDelayed = true;
            tree.SelectedNode = delayed;
            allowDelayed = false;
        };
        tree.BeforeSelect += (_, e) =>
        {
            if (e.Node?.Text == "Ignores Select" || (e.Node == delayed && !allowDelayed))
            {
                e.Cancel = true;
                if (e.Node == delayed && !timer.Enabled)
                {
                    timer.Start();
                }
            }
        };
        tree.AfterSelect += (_, e) => status.Text = $"Selected: {e.Node?.Text}";
        canvas.Controls.Add(tree);
        canvas.Controls.Add(status);
    }

    /// <summary>A button entirely under a sibling label: a press at any of its points lands on the label.</summary>
    private static void AddCoveredButton(Control canvas)
    {
        var clicks = 0;
        var counter = StatusLabel("CoveredClicksLabel", "Covered clicks: 0", new Point(170, 145));
        var button = new Button
        {
            Name = "CoveredButton",
            Text = "Covered Button",
            Location = new Point(10, 140),
            Size = new Size(140, 30),
        };
        button.Click += (_, _) => counter.Text = $"Covered clicks: {++clicks}";
        var overlay = new Label
        {
            Name = "OverlayLabel",
            Text = "Overlay",
            BackColor = Color.Khaki,
            Location = new Point(5, 135),
            Size = new Size(150, 40),
        };
        canvas.Controls.Add(button);
        canvas.Controls.Add(overlay);
        canvas.Controls.Add(counter);
        overlay.BringToFront();
    }

    /// <summary>
    /// Two controls with no Value pattern: a pane that hands focus straight back, and a box that takes keys only, next to
    /// a text box that holds focus first so a misdirected Ctrl+A and typing would show in it.
    /// </summary>
    private static void AddNoValueControls(Control canvas)
    {
        canvas.Controls.Add(
            new FocusRefusingPane
            {
                Name = "NoValuePane",
                AccessibleName = "No Value Pane",
                BorderStyle = BorderStyle.FixedSingle,
                TabStop = false,
                Location = new Point(10, 190),
                Size = new Size(140, 30),
            }
        );
        var typed = StatusLabel("KeysOnlyTextLabel", "Keys: ", new Point(320, 195));
        var box = new KeysOnlyBox
        {
            Name = "KeysOnlyBox",
            AccessibleName = "Keys Only Box",
            Location = new Point(170, 190),
            Size = new Size(140, 30),
        };
        box.ContentChanged += (_, _) => typed.Text = $"Keys: {box.Content}";
        canvas.Controls.Add(box);
        canvas.Controls.Add(typed);
        canvas.Controls.Add(
            new TextBox
            {
                Name = "DecoyBox",
                AccessibleName = "Decoy Box",
                Text = "keep me",
                Location = new Point(10, 230),
                Width = 140,
            }
        );
    }

    private void AddWindowLaunchers(Control canvas)
    {
        var toolWindow = new Button
        {
            Name = "OpenToolWindowButton",
            Text = "Open Tool Window",
            Location = new Point(10, 270),
            AutoSize = true,
        };
        toolWindow.Click += (_, _) =>
            new Form
            {
                Text = "Test Tool Window",
                Name = "TestToolWindow",
                FormBorderStyle = FormBorderStyle.FixedToolWindow,
                Size = new Size(220, 140),
                StartPosition = FormStartPosition.CenterParent,
            }.Show(this);
        var fullScreen = new Button
        {
            Name = "OpenFullScreenButton",
            Text = "Open Full Screen Window",
            Location = new Point(170, 270),
            AutoSize = true,
        };
        fullScreen.Click += (_, _) => FullScreenWindow.Open();
        canvas.Controls.Add(toolWindow);
        canvas.Controls.Add(fullScreen);
    }
}
