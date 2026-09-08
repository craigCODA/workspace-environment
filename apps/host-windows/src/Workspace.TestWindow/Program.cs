using System.Drawing;
using System.Windows.Forms;

namespace Workspace.TestWindow;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new AcceptanceWindow());
    }
}

internal sealed class AcceptanceWindow : Form
{
    private readonly Panel _liveIndicator;
    private readonly Label _liveLabel;
    private readonly Label _counterLabel;
    private readonly System.Windows.Forms.Timer _animationTimer;
    private int _frame;
    private int _count;

    public AcceptanceWindow()
    {
        Text = "Workspace Environment Test Window";
        AccessibleName = Text;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 520);
        ClientSize = new Size(880, 640);
        BackColor = Color.FromArgb(22, 32, 35);
        ForeColor = Color.FromArgb(239, 235, 219);
        Font = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(24),
            BackColor = BackColor,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _liveIndicator = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 16),
            BackColor = Color.FromArgb(171, 94, 58),
            AccessibleName = "Changing live visual indicator",
        };
        _liveLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "LIVE FRAME 0000",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
        };
        _liveIndicator.Controls.Add(_liveLabel);

        var controls = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            Margin = new Padding(0),
        };
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        controls.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        controls.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var inputLabel = new Label
        {
            Text = "TYPE THROUGH THE SPATIAL SURFACE",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            ForeColor = Color.FromArgb(205, 162, 104),
        };
        controls.SetColumnSpan(inputLabel, 3);

        var input = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 16, 8),
            Multiline = true,
            AccessibleName = "Test text input",
            BackColor = Color.FromArgb(237, 232, 213),
            ForeColor = Color.FromArgb(22, 32, 35),
        };

        var increment = new Button
        {
            Text = "INCREMENT",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 16, 8),
            AccessibleName = "Increment counter",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(42, 68, 70),
            ForeColor = Color.White,
        };
        increment.FlatAppearance.BorderColor = Color.FromArgb(205, 162, 104);

        _counterLabel = new Label
        {
            Text = "COUNT 000",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 8),
            TextAlign = ContentAlignment.MiddleCenter,
            AccessibleName = "Counter value",
            BorderStyle = BorderStyle.FixedSingle,
            ForeColor = Color.FromArgb(239, 235, 219),
        };
        increment.Click += (_, _) =>
        {
            _count += 1;
            _counterLabel.Text = $"COUNT {_count:000}";
        };

        controls.Controls.Add(inputLabel, 0, 0);
        controls.Controls.Add(input, 0, 1);
        controls.Controls.Add(increment, 1, 1);
        controls.Controls.Add(_counterLabel, 2, 1);

        var scrollRegion = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 12, 0, 0),
            AutoScroll = true,
            AccessibleName = "Scrollable test region",
            BackColor = Color.FromArgb(30, 45, 48),
        };
        for (var index = 0; index < 40; index += 1)
        {
            scrollRegion.Controls.Add(new Label
            {
                Text = $"SCROLL MARKER {index + 1:00}",
                Location = new Point(20, 18 + index * 42),
                AutoSize = true,
                ForeColor = index % 2 == 0
                    ? Color.FromArgb(239, 235, 219)
                    : Color.FromArgb(205, 162, 104),
            });
        }

        layout.Controls.Add(_liveIndicator, 0, 0);
        layout.Controls.Add(controls, 0, 1);
        layout.Controls.Add(scrollRegion, 0, 2);
        Controls.Add(layout);

        _animationTimer = new System.Windows.Forms.Timer { Interval = 450 };
        _animationTimer.Tick += (_, _) =>
        {
            _frame += 1;
            _liveLabel.Text = $"LIVE FRAME {_frame:0000}";
            _liveIndicator.BackColor = _frame % 2 == 0
                ? Color.FromArgb(171, 94, 58)
                : Color.FromArgb(48, 112, 110);
        };
        _animationTimer.Start();
        FormClosed += (_, _) => _animationTimer.Dispose();
    }
}
