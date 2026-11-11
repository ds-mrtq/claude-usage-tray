using System.Runtime.InteropServices;

namespace ClaudeUsageTray;

/// <summary>
/// Bảng chi tiết dạng overlay ghim: luôn nằm trên cùng, không lấy focus, không tự ẩn khi bấm ra ngoài
/// hay chuyển cửa sổ. Chỉ ẩn khi bấm nút x trên bảng hoặc bấm lại icon khay. Kéo header để di chuyển.
/// </summary>
internal sealed class PopupForm : Form
{
    private UsageData? _data;
    private const int BaseWidth = 340;
    private bool _userMoved;
    private bool _pinned; // anh đã mở bảng và chưa bấm đóng
    // Shell (Win+D, trả cửa sổ về) có thể đẩy bảng xuống dưới: đặt lại TOPMOST mỗi giây khi đang ghim
    private readonly System.Windows.Forms.Timer _keepTop = new() { Interval = 1000 };

    public PopupForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Bg;
        DoubleBuffered = true;
        Text = "Claude Usage";
        _keepTop.Tick += (_, _) =>
        {
            if (!_pinned) return;
            if (IsIconic(Handle)) ShowWindow(Handle, 4 /* SW_SHOWNOACTIVATE */);
            SetWindowPos(Handle, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 /* NOSIZE|NOMOVE|NOACTIVATE */);
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _keepTop.Dispose();
        base.Dispose(disposing);
    }

    private float DpiScale => DeviceDpi / 96f;

    // Hiện mà không cướp focus của cửa sổ anh đang làm
    protected override bool ShowWithoutActivation => true;

    public void SetData(UsageData? d)
    {
        _data = d;
        if (Visible) { Relayout(keepBottom: !_userMoved); Invalidate(); }
    }

    public void ShowNearTray()
    {
        Relayout(keepBottom: false);
        if (!_userMoved)
        {
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            var margin = (int)(10 * DpiScale);
            Location = new Point(wa.Right - Width - margin, wa.Bottom - Height - margin);
        }
        _pinned = true;
        _keepTop.Start();
        Show();
        TopMost = true;
        Log.Write($"Shown at {Location} size {Size}");
    }

    public void HidePanel(string why)
    {
        _pinned = false;
        _keepTop.Stop();
        Hide();
        Log.Write("Hidden: " + why);
    }

    private void Relayout(bool keepBottom)
    {
        var w = (int)(BaseWidth * DpiScale);
        var h = Renderer.DrawPanel(null, _data, w, DpiScale, DateTimeOffset.Now);
        if (keepBottom && Visible && h != Height) Top += Height - h; // giữ mép dưới sát taskbar khi bảng cao/thấp đi
        Size = new Size(w, h);
    }

    // Win+D, "Minimize all" hay công cụ khác thu nhỏ bảng: bảng đang ghim thì hiện lại ngay
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_pinned && WindowState == FormWindowState.Minimized)
        {
            Log.Write("Minimized by system, restoring");
            BeginInvoke(() =>
            {
                ShowWindow(Handle, 4 /* SW_SHOWNOACTIVATE: hiện lại mà không lấy focus */);
                TopMost = true;
            });
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Renderer.DrawPanel(e.Graphics, _data, Width, DpiScale, DateTimeOffset.Now);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hover = Renderer.CloseRect(Width, DpiScale).Contains(e.Location);
        if (hover != Renderer.CloseHover)
        {
            Renderer.CloseHover = hover;
            Cursor = hover ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (Renderer.CloseHover) { Renderer.CloseHover = false; Cursor = Cursors.Default; Invalidate(); }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && Renderer.CloseRect(Width, DpiScale).Contains(e.Location))
        {
            HidePanel("nút x");
            return;
        }
        if (e.Button == MouseButtons.Left && e.Y < Renderer.HeaderHeight(DpiScale))
        {
            // Kéo bảng bằng header: giao cho Windows xử lý như kéo thanh tiêu đề
            ReleaseCapture();
            SendMessage(Handle, 0xA1 /* WM_NCLBUTTONDOWN */, 2 /* HTCAPTION */, 0);
            _userMoved = true;
            Log.Write($"Moved to {Location}");
            return;
        }
        base.OnMouseDown(e);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80;       // WS_EX_TOOLWINDOW: không hiện trong Alt+Tab
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE: bấm vào bảng không lấy focus
            cp.ExStyle |= 0x8;        // WS_EX_TOPMOST
            cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
            return cp;
        }
    }

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);
}
