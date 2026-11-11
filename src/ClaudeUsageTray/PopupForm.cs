namespace ClaudeUsageTray;

/// <summary>Bảng chi tiết hiện phía trên khay hệ thống; mất focus thì tự ẩn.</summary>
internal sealed class PopupForm : Form
{
    private UsageData? _data;
    private long _shownAt;
    private const int BaseWidth = 340;

    public PopupForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Bg;
        DoubleBuffered = true;
        KeyPreview = true;
        Text = "Claude Usage";
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Hide(); };
        Deactivate += (_, _) =>
        {
            Log.Write($"Deactivate after {Environment.TickCount64 - _shownAt}ms");
            // Windows có thể trả focus về khay ngay sau khi bảng hiện: bỏ qua lần mất focus quá sớm
            if (Environment.TickCount64 - _shownAt > 400) Hide();
        };
    }

    private float DpiScale => DeviceDpi / 96f;

    public void SetData(UsageData? d)
    {
        _data = d;
        if (Visible) { Relayout(); Invalidate(); }
    }

    public void ShowNearTray()
    {
        Relayout();
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        var margin = (int)(10 * DpiScale);
        Location = new Point(wa.Right - Width - margin, wa.Bottom - Height - margin);
        _shownAt = Environment.TickCount64;
        Show();
        Activate();
        Log.Write($"Shown at {Location} size {Size}");
    }

    private void Relayout()
    {
        var w = (int)(BaseWidth * DpiScale);
        var h = Renderer.DrawPanel(null, _data, w, DpiScale, DateTimeOffset.Now);
        Size = new Size(w, h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Renderer.DrawPanel(e.Graphics, _data, Width, DpiScale, DateTimeOffset.Now);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW: không hiện trong Alt+Tab
            cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
            return cp;
        }
    }
}
