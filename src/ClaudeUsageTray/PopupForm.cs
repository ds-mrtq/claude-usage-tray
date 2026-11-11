using System.Runtime.InteropServices;

namespace ClaudeUsageTray;

/// <summary>
/// Bảng chi tiết dạng overlay ghim: luôn nằm trên cùng, không lấy focus, không tự ẩn khi bấm ra ngoài
/// hay chuyển cửa sổ. Chỉ ẩn khi bấm nút x hoặc bấm lại icon khay.
/// Kéo header để di chuyển; kéo viền/góc để đổi cỡ (nội dung phóng theo, giữ tỉ lệ);
/// nút ô vuông phóng to tối đa theo chiều cao màn hình. Vị trí, cỡ, trạng thái lưu cho lần chạy sau.
/// </summary>
internal sealed class PopupForm : Form
{
    private const int BaseWidth = 340;
    private const float MinZoom = 0.6f, MaxZoom = 4f;

    private UsageData? _data;
    /// <summary>Anh bấm nút làm mới ở header.</summary>
    public event Action? RefreshRequested;
    /// <summary>Anh chọn một phiên trong danh sách (null = xem tổng).</summary>
    public event Action<string?>? SessionSelected;
    private readonly WindowSettings _s = WindowSettings.Load();
    private float _zoom;
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
        ResizeRedraw = true;
        Text = "Claude Usage";

        _zoom = Math.Clamp(_s.Zoom, MinZoom, MaxZoom);
        Renderer.Maximized = _s.Maximized;
        if (_s.X != null && _s.Y != null)
        {
            Location = new Point(_s.X.Value, _s.Y.Value);
            _userMoved = true;
        }

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
    private float Scale_ => DpiScale * _zoom;
    private int Margin_ => (int)(10 * DpiScale);

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
        // Vị trí đã lưu nằm ngoài mọi màn hình (vd rút màn hình phụ): về góc khay
        if (_userMoved && !Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(new Rectangle(Location, new Size(Width / 2, Height / 2)))))
            _userMoved = false;
        if (!_userMoved && !Renderer.Maximized)
        {
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.Right - Width - Margin_, wa.Bottom - Height - Margin_);
        }
        _pinned = true;
        _keepTop.Start();
        Show();
        TopMost = true;
        Log.Write($"Shown at {Location} size {Size} zoom {_zoom:0.00} max={Renderer.Maximized}");
    }

    /// <summary>Quên vị trí/cỡ/phóng to đã lưu, đưa bảng về cỡ gốc ở góc khay như lần mở đầu tiên.</summary>
    public void ResetLayout()
    {
        _zoom = 1f;
        Renderer.Maximized = false;
        _userMoved = false;
        _s.X = _s.Y = _s.RestoreX = _s.RestoreY = null;
        _s.Zoom = _s.RestoreZoom = 1f;
        _s.Maximized = false;
        _s.Save();
        Log.Write("Layout reset");
        ShowNearTray();
        Invalidate();
    }

    public void HidePanel(string why)
    {
        _pinned = false;
        _keepTop.Stop();
        Hide();
        Log.Write("Hidden: " + why);
    }

    private int ContentHeight(float zoom) =>
        Renderer.DrawPanel(null, _data, (int)(BaseWidth * DpiScale * zoom), DpiScale * zoom, DateTimeOffset.Now);

    private void Relayout(bool keepBottom)
    {
        if (Renderer.Maximized) { FitMaximized(); return; }
        var w = (int)(BaseWidth * Scale_);
        var h = ContentHeight(_zoom);
        if (keepBottom && Visible && h != Height) Top += Height - h; // giữ mép dưới sát taskbar khi bảng cao/thấp đi
        Size = new Size(w, h);
    }

    /// <summary>
    /// Chiếm trọn vùng làm việc của màn hình đang chứa bảng (kể cả màn hình phụ dựng dọc).
    /// Chữ phóng theo chiều còn hẹp hơn; dư ngang thì thanh dài ra, dư dọc thì giãn khoảng giữa các mục.
    /// </summary>
    private void FitMaximized()
    {
        var wa = (Visible || _userMoved ? Screen.FromRectangle(Bounds) : Screen.FromPoint(Cursor.Position)).WorkingArea;
        var m = Margin_;
        var w = wa.Width - 2 * m;
        var h = wa.Height - 2 * m;
        var z = Math.Min(h / (float)ContentHeight(1f), w / (BaseWidth * DpiScale));
        _zoom = Math.Clamp(z, MinZoom, 8f);
        Bounds = new Rectangle(wa.Left + m, wa.Top + m, w, h);
    }

    private void ToggleMaximize()
    {
        if (!Renderer.Maximized)
        {
            _s.RestoreX = Left; _s.RestoreY = Top; _s.RestoreZoom = _zoom;
            Renderer.Maximized = true;
            FitMaximized();
        }
        else
        {
            Renderer.Maximized = false;
            _zoom = Math.Clamp(_s.RestoreZoom, MinZoom, MaxZoom);
            if (_s.RestoreX != null && _s.RestoreY != null) Location = new Point(_s.RestoreX.Value, _s.RestoreY.Value);
            Relayout(keepBottom: false);
        }
        _userMoved = true;
        Invalidate();
        SaveSettings();
        Log.Write($"Maximized={Renderer.Maximized} bounds {Bounds} zoom {_zoom:0.00}");
    }

    private void SaveSettings()
    {
        _s.X = Left; _s.Y = Top;
        _s.Maximized = Renderer.Maximized;
        if (!Renderer.Maximized) _s.Zoom = _zoom;
        _s.Save();
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
        Renderer.DrawPanel(e.Graphics, _data, Width, Scale_, DateTimeOffset.Now, Renderer.Maximized ? Height : 0);
        if (Log.On)
        {
            // Vùng bấm theo toạ độ màn hình, để script smoke bấm chuột thật đúng chỗ
            var hits = string.Join(" ", Renderer.Hits.Select(h =>
            {
                var c = PointToScreen(new Point((int)(h.rect.X + h.rect.Width / 2), (int)(h.rect.Y + h.rect.Height / 2)));
                return $"{(h.key.StartsWith("cc:") ? "cc" : h.key.StartsWith("omp:") ? "omp" : h.key)}@{c.X},{c.Y}";
            }));
            if (hits != _lastHits) { _lastHits = hits; Log.Write("Hits " + hits); }
        }
    }
    private string? _lastHits;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hover = Renderer.HitButton(e.Location, Width, Scale_);
        if (hover != Renderer.Hover)
        {
            Renderer.Hover = hover;
            Invalidate();
        }
        Cursor = hover != Renderer.HeaderButton.None || Renderer.HitSession(e.Location) != null ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (Renderer.Hover != Renderer.HeaderButton.None) { Renderer.Hover = Renderer.HeaderButton.None; Cursor = Cursors.Default; Invalidate(); }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) { base.OnMouseDown(e); return; }
        switch (Renderer.HitButton(e.Location, Width, Scale_))
        {
            case Renderer.HeaderButton.Close: HidePanel("nút x"); return;
            case Renderer.HeaderButton.Max: ToggleMaximize(); return;
            case Renderer.HeaderButton.Refresh: Log.Write("Refresh button"); RefreshRequested?.Invoke(); return;
        }
        switch (Renderer.HitSession(e.Location))
        {
            case null: break;
            case Renderer.ToggleKey:
                Renderer.Expanded = !Renderer.Expanded;
                Log.Write($"Session list expanded={Renderer.Expanded}");
                Relayout(keepBottom: !_userMoved);
                Invalidate();
                return;
            case var key:
                Renderer.Expanded = false;
                Log.Write($"Session selected: {(key == Renderer.TotalKey ? "tổng" : key.Split(':')[0])}");
                SessionSelected?.Invoke(key == Renderer.TotalKey ? null : key);
                return;
        }
        if (e.Y < Renderer.HeaderHeight(Scale_))
        {
            if (Renderer.Maximized)
            {
                // Kéo khi đang phóng to: trả về cỡ thường ngay dưới con trỏ rồi kéo tiếp (như cửa sổ Windows)
                var relX = e.X / (float)Width;
                var oldScale = Scale_;
                Renderer.Maximized = false;
                _zoom = Math.Clamp(_s.RestoreZoom, MinZoom, MaxZoom);
                var w = (int)(BaseWidth * Scale_);
                var h = ContentHeight(_zoom);
                var cur = Cursor.Position;
                Bounds = new Rectangle(cur.X - (int)(relX * w), cur.Y - (int)(e.Y * Scale_ / oldScale), w, h);
                Invalidate();
                Update();
                Log.Write($"Drag from maximized: restored to {Bounds}");
            }
            // Kéo bảng bằng header: giao cho Windows xử lý như kéo thanh tiêu đề (lưu ở WM_EXITSIZEMOVE)
            ReleaseCapture();
            SendMessage(Handle, 0xA1 /* WM_NCLBUTTONDOWN */, 2 /* HTCAPTION */, 0);
            return;
        }
        base.OnMouseDown(e);
    }

    private const int WM_NCHITTEST = 0x84, WM_SIZING = 0x214, WM_EXITSIZEMOVE = 0x232;

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_NCHITTEST when !Renderer.Maximized:
            {
                // Viền mỏng quanh bảng dùng để đổi cỡ như cửa sổ thường
                var p = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
                var g = (int)Math.Max(6, 6 * DpiScale);
                bool l = p.X < g, r = p.X >= Width - g, t = p.Y < g, b = p.Y >= Height - g;
                int hit = t && l ? 13 : t && r ? 14 : b && l ? 16 : b && r ? 17 : l ? 10 : r ? 11 : t ? 12 : b ? 15 : 0;
                if (hit != 0) { m.Result = hit; return; }
                break;
            }
            case WM_SIZING:
            {
                // Giữ tỉ lệ: độ phóng lấy từ chiều rộng (hoặc chiều cao khi kéo cạnh trên/dưới), chiều cao theo nội dung
                var edge = m.WParam.ToInt32();
                var rc = Marshal.PtrToStructure<RECT>(m.LParam);
                float z = edge is 3 or 6
                    ? (rc.Bottom - rc.Top) / (float)ContentHeight(1f)
                    : (rc.Right - rc.Left) / (BaseWidth * DpiScale);
                _zoom = Math.Clamp(z, MinZoom, MaxZoom);
                var w = (int)(BaseWidth * Scale_);
                var h = ContentHeight(_zoom);
                if (edge is 1 or 4 or 7) rc.Left = rc.Right - w; else rc.Right = rc.Left + w;
                if (edge is 3 or 4 or 5) rc.Top = rc.Bottom - h; else rc.Bottom = rc.Top + h;
                Marshal.StructureToPtr(rc, m.LParam, false);
                m.Result = 1;
                return;
            }
            case WM_EXITSIZEMOVE:
                _userMoved = true;
                SaveSettings();
                Log.Write($"Moved/resized to {Bounds} zoom {_zoom:0.00}");
                break;
        }
        base.WndProc(ref m);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

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
