using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ClaudeUsageTray;

/// <summary>Vẽ icon khay và bảng chi tiết. Không phụ thuộc Form, nên xuất được ra PNG để kiểm tra.</summary>
internal static class Renderer
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    // ---------------- Icon khay ----------------
    public static Bitmap DrawIcon(UsageData? d, int size, DateTimeOffset now)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        var five = d?.FiveHour;
        var stale = d == null || now - d.WrittenAt > StaleAfter;
        var r = new RectangleF(0, 0, size, size);
        using (var bg = new SolidBrush(Theme.Bg)) FillRounded(g, bg, r, size * 0.2f);

        // Thanh mức dùng ở đáy icon
        var barH = Math.Max(2f, size * 0.16f);
        var barRect = new RectangleF(size * 0.08f, size - barH - size * 0.06f, size * 0.84f, barH);
        using (var tr = new SolidBrush(Theme.Track)) g.FillRectangle(tr, barRect);
        if (five != null)
        {
            var w = barRect.Width * (float)Math.Clamp(five.PercentUsed / 100, 0, 1);
            using var fb = new SolidBrush(stale ? Theme.Faint : Theme.BarColor(five.PercentUsed));
            g.FillRectangle(fb, barRect.X, barRect.Y, w, barRect.Height);
        }

        // Số %
        var text = five == null ? "--" : five.PercentUsed >= 100 ? "!!" : Math.Round(five.PercentUsed).ToString();
        var color = stale ? Theme.Dim : five == null ? Theme.Dim : Theme.Heat(five.PercentUsed);
        using var font = new Font("Segoe UI", size * (text.Length >= 3 ? 0.36f : 0.5f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var tb = new SolidBrush(color);
        var area = new RectangleF(0, -size * 0.04f, size, size - barH - size * 0.06f);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, tb, area, sf);
        return bmp;
    }

    public static string Tooltip(UsageData? d, DateTimeOffset now)
    {
        if (d == null) return "Claude Usage: chưa có dữ liệu (mở một phiên Claude Code hoặc OMP)";
        var parts = new List<string>();
        foreach (var l in d.Limits)
        {
            var name = l.Kind switch { "five_hour" => "5h", "seven_day" => "7d", "seven_day_fable" => "Fable", "extra" => "Extra", _ => l.Label };
            parts.Add($"{name} {Math.Round(l.PercentUsed)}%");
        }
        var five = d.FiveHour;
        if (five?.ResetsAt != null) parts.Add("reset " + Fmt.Dur(five.ResetsAt.Value - now));
        if (d.Cost != null && !double.IsNaN(d.Cost.Today)) parts.Add("hôm nay " + Fmt.Usd(d.Cost.Today));
        var s = "Claude · " + string.Join(" · ", parts);
        if (now - d.WrittenAt > StaleAfter) s += " (cũ " + Fmt.Ago(now - d.WrittenAt) + ")";
        return s.Length > 127 ? s[..127] : s;
    }

    // ---------------- Bảng chi tiết ----------------
    /// <summary>Vùng nút đóng ở góc phải header (pixel đã nhân scale).</summary>
    public static RectangleF CloseRect(int width, float scale) => new(width - 16 * scale - 14 * scale, 13 * scale, 14 * scale, 14 * scale);

    /// <summary>Chiều cao vùng header dùng để kéo bảng.</summary>
    public static float HeaderHeight(float scale) => 34 * scale;

    /// <summary>Nút phóng to tối đa, nằm bên trái nút đóng.</summary>
    public static RectangleF MaxRect(int width, float scale) { var c = CloseRect(width, scale); return new(c.X - 18 * scale, c.Y, c.Width, c.Height); }

    /// <summary>Nút làm mới (lấy hạn mức mới nhất từ API), bên trái nút phóng to.</summary>
    public static RectangleF RefreshRect(int width, float scale) { var c = MaxRect(width, scale); return new(c.X - 18 * scale, c.Y, c.Width, c.Height); }

    public enum HeaderButton { None, Refresh, Max, Close }
    public static HeaderButton Hover;
    public static bool Maximized;
    /// <summary>Đang gọi API làm mới.</summary>
    public static bool Busy;
    /// <summary>Thông báo ngắn ở header (lỗi làm mới...), thay cho dòng "x trước".</summary>
    public static string? Status;
    /// <summary>Danh sách chọn phiên đang mở ra.</summary>
    public static bool Expanded;
    /// <summary>Khoá nút mở danh sách phiên trong <see cref="Hits"/>.</summary>
    public const string ToggleKey = "__toggle";
    /// <summary>Khoá chọn xem tổng (mọi phiên).</summary>
    public const string TotalKey = "__total";
    /// <summary>Vùng bấm (pixel trong bảng) vẽ ở lần vẽ gần nhất: nút danh sách, từng dòng phiên.</summary>
    public static readonly List<(RectangleF rect, string key)> Hits = [];

    /// <summary>Vùng có chú thích khi rê chuột (vạch nhịp, vạch auto-compact) ở lần vẽ gần nhất.</summary>
    public static readonly List<(RectangleF rect, string text)> Tips = [];

    public static string? TipAt(Point p)
    {
        foreach (var (r, t) in Tips) if (r.Contains(p)) return t;
        return null;
    }

    /// <summary>
    /// Vẽ khung chú thích ngay trong bảng, cạnh con trỏ, giữ trong mép bảng. Không dùng ToolTip của WinForms:
    /// bảng là cửa sổ WS_EX_NOACTIVATE nên cửa sổ tooltip được tạo nhưng không bao giờ hiện.
    /// </summary>
    public static void DrawTip(Graphics g, string text, Point at, int width, int height, float scale)
    {
        float S(float v) => v * scale;
        using var f = Theme.Mono(S(8f));
        var maxW = width - S(16);
        var sz = g.MeasureString(text, f, (int)(maxW - S(16)));
        var w = sz.Width + S(16);
        var h = sz.Height + S(12);
        var x = Math.Clamp(at.X - w / 2, S(6), width - w - S(6));
        var y = at.Y + S(16);
        if (y + h > height - S(4)) y = at.Y - h - S(10);
        var r = new RectangleF(x, y, w, h);
        using (var sh = new SolidBrush(Color.FromArgb(110, 0, 0, 0))) FillRounded(g, sh, new RectangleF(r.X + S(2), r.Y + S(3), r.Width, r.Height), S(5));
        using (var bg = new SolidBrush(ColorTranslator.FromHtml("#3c3f41"))) FillRounded(g, bg, r, S(5));
        using (var pen = new Pen(ColorTranslator.FromHtml("#5e6366"), Math.Max(1, S(1))))
            g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        using var tb = new SolidBrush(ColorTranslator.FromHtml("#d0d0d0"));
        g.DrawString(text, f, tb, new RectangleF(x + S(8), y + S(6), sz.Width + S(2), sz.Height + S(2)));
    }

    public static string? HitSession(Point p)
    {
        foreach (var (r, k) in Hits) if (r.Contains(p)) return k;
        return null;
    }

    public static HeaderButton HitButton(Point p, int width, float scale) =>
        CloseRect(width, scale).Contains(p) ? HeaderButton.Close
        : MaxRect(width, scale).Contains(p) ? HeaderButton.Max
        : RefreshRect(width, scale).Contains(p) ? HeaderButton.Refresh
        : HeaderButton.None;

    /// <summary>Vẽ bảng; g == null chỉ đo chiều cao. Đơn vị: pixel đã nhân scale DPI.</summary>
    public static int DrawPanel(Graphics? g, UsageData? d, int width, float scale, DateTimeOffset now, int stretchTo = 0)
    {
        float S(float v) => v * scale;
        var P = S(16);
        var iw = width - P * 2;
        float y = P;
        // Phóng to tối đa: chia đều khoảng dư theo chiều dọc vào giữa các mục để bảng lấp đầy màn hình
        float extra = 0;
        if (stretchTo > 0 && d != null)
        {
            var natural = DrawPanel(null, d, width, scale, now);
            var total = d.Selected == null && d.Sessions.Count > 0;
            var gaps = d.Limits.Count + (d.Cost != null ? 1 : 0) + (total ? 1
                : (d.Context?.Percent != null ? 1 : 0) + (d.OmpContext != null ? 1 : 0));
            if (gaps > 0 && stretchTo > natural) extra = (stretchTo - natural) / gaps;
        }

        using var fCaps = Theme.Mono(S(8.5f), FontStyle.Bold);
        using var fMeta = Theme.Mono(S(8f));
        using var fBig = Theme.Mono(S(17f), FontStyle.Bold);
        using var fMid = Theme.Mono(S(12f), FontStyle.Bold);
        using var fName = Theme.Mono(S(9f), FontStyle.Bold);
        using var fChip = Theme.Mono(S(7f), FontStyle.Bold);

        if (g != null)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Bg);
            using var pen = new Pen(Theme.Border);
            g.DrawRectangle(pen, 0, 0, width - 1, (int)(g.VisibleClipBounds.Height) - 1);
        }

        void Text(string s, Font f, Color c, float x, float yy, bool right = false)
        {
            if (g == null) return;
            using var b = new SolidBrush(c);
            if (right)
            {
                var sz = g.MeasureString(s, f);
                g.DrawString(s, f, b, x - sz.Width, yy);
            }
            else g.DrawString(s, f, b, x, yy);
        }
        float TextW(string s, Font f) => g?.MeasureString(s, f).Width ?? s.Length * f.Size * 0.62f;
        void Rule()
        {
            if (g == null) return;
            using var b = new SolidBrush(Theme.Track);
            g.FillRectangle(b, P, y, iw, Math.Max(1, S(1)));
        }

        // Header
        if (g != null)
        {
            using var dot = new SolidBrush(Theme.Green);
            g.FillEllipse(dot, P, y + S(3), S(8), S(8));
        }
        Text("CLAUDE USAGE", fCaps, Theme.Dim, P + S(13), y);
        var metaRight = width - P - S(56);
        if (Busy) Text("đang lấy…", fMeta, Theme.Blue, metaRight, y, right: true);
        else if (Status != null) Text(Status, fMeta, Theme.Orange, metaRight, y, right: true);
        else if (d != null) Text(Fmt.Ago(now - d.WrittenAt) + " trước · " + d.Sources, fMeta, now - d.WrittenAt > StaleAfter ? Theme.Orange : Theme.Faint, metaRight, y, right: true);
        if (g != null)
        {
            var m = S(4);
            // Nút làm mới: mũi tên vòng tròn
            var rr = RefreshRect(width, scale);
            if (Hover == HeaderButton.Refresh) { using var hb = new SolidBrush(Theme.Track); FillRounded(g, hb, rr, S(3)); }
            using (var rp = new Pen(Busy ? Theme.Blue : Hover == HeaderButton.Refresh ? Theme.Text : Theme.Dim, Math.Max(1, S(1.3f))))
            {
                var arc = RectangleF.Inflate(rr, -m + S(0.5f), -m + S(0.5f));
                g.DrawArc(rp, arc, 30, 290);
                // đầu mũi tên tam giác đặc ở cuối cung (góc 320°), chỉ theo chiều kim đồng hồ
                var ang = 320 * Math.PI / 180;
                float R = arc.Width / 2, cx = arc.X + R, cy = arc.Y + arc.Height / 2;
                float ex = cx + R * (float)Math.Cos(ang), ey = cy + R * (float)Math.Sin(ang);
                float tx = -(float)Math.Sin(ang), ty = (float)Math.Cos(ang); // tiếp tuyến
                float nx = (float)Math.Cos(ang), ny = (float)Math.Sin(ang);  // pháp tuyến hướng ra ngoài
                using var ab = new SolidBrush(rp.Color);
                g.FillPolygon(ab, new[]
                {
                    new PointF(ex + tx * S(2.6f), ey + ty * S(2.6f)),
                    new PointF(ex + nx * S(2.4f) - tx * S(0.8f), ey + ny * S(2.4f) - ty * S(0.8f)),
                    new PointF(ex - nx * S(2.4f) - tx * S(0.8f), ey - ny * S(2.4f) - ty * S(0.8f)),
                });
            }
            // Nút phóng to tối đa / trả lại: ô vuông, hoặc hai ô chồng khi đang phóng to
            var mr = MaxRect(width, scale);
            if (Hover == HeaderButton.Max) { using var hb = new SolidBrush(Theme.Track); FillRounded(g, hb, mr, S(3)); }
            using (var mp = new Pen(Hover == HeaderButton.Max ? Theme.Text : Theme.Dim, Math.Max(1, S(1.2f))))
            {
                if (Maximized)
                {
                    g.DrawRectangle(mp, mr.X + m + S(2), mr.Y + m - S(1), mr.Width - 2 * m - S(1), mr.Height - 2 * m - S(1));
                    using var fill = new SolidBrush(Hover == HeaderButton.Max ? Theme.Track : Theme.Bg);
                    var inner = new RectangleF(mr.X + m - S(1), mr.Y + m + S(1), mr.Width - 2 * m - S(1), mr.Height - 2 * m - S(1));
                    g.FillRectangle(fill, inner);
                    g.DrawRectangle(mp, inner.X, inner.Y, inner.Width, inner.Height);
                }
                else g.DrawRectangle(mp, mr.X + m, mr.Y + m, mr.Width - 2 * m, mr.Height - 2 * m);
            }
            // Nút đóng: dấu x, sáng lên khi rê chuột
            var cr = CloseRect(width, scale);
            if (Hover == HeaderButton.Close) { using var hb = new SolidBrush(Theme.Track); FillRounded(g, hb, cr, S(3)); }
            using var xp = new Pen(Hover == HeaderButton.Close ? Theme.Text : Theme.Dim, Math.Max(1, S(1.4f)));
            g.DrawLine(xp, cr.X + m, cr.Y + m, cr.Right - m, cr.Bottom - m);
            g.DrawLine(xp, cr.Right - m, cr.Y + m, cr.X + m, cr.Bottom - m);
        }
        y += S(20);
        if (g != null) { Hits.Clear(); Tips.Clear(); }
        var totalView = d != null && d.Selected == null && d.Sessions.Count > 0;
        if (d != null && (d.Sessions.Count > 0 || d.Selected != null))
        {
            // Ô chọn phiên: bấm để mở danh sách các phiên đang chạy
            var sel = d.Selected;
            var label = sel == null
                ? $"Tất cả phiên ({d.Sessions.Count})"
                : $"{sel.Kind} · {sel.Name}" + (d.MainModel != null ? " · " + Pretty(d.MainModel) : "") + (d.MainEffort != null ? " · " + d.MainEffort : "");
            var maxW = iw - S(16);
            while (label.Length > 6 && TextW(label, fChip) > maxW) label = label[..^2] + "…";
            var pill = new RectangleF(P, y, TextW(label, fChip) + S(18), S(15));
            if (g != null)
            {
                using var pb = new SolidBrush(sel == null ? Theme.Track : sel.Kind == "OMP" ? Color.FromArgb(0x5c, 0x4a, 0x6b) : Theme.Effort(d.MainEffort));
                FillRounded(g, pb, pill, pill.Height / 2);
                using var tb = new SolidBrush(ColorTranslator.FromHtml("#bbbbbb"));
                g.DrawString(label, fChip, tb, pill.X + S(4), pill.Y + S(1.5f));
                // tam giác xổ xuống / thu lên
                var tx = pill.Right - S(9); var ty = pill.Y + pill.Height / 2;
                var tri = Expanded
                    ? new[] { new PointF(tx - S(3), ty + S(1.5f)), new PointF(tx + S(3), ty + S(1.5f)), new PointF(tx, ty - S(2)) }
                    : new[] { new PointF(tx - S(3), ty - S(1.5f)), new PointF(tx + S(3), ty - S(1.5f)), new PointF(tx, ty + S(2)) };
                g.FillPolygon(tb, tri);
                Hits.Add((pill, ToggleKey));
            }
            y += S(20);
            if (Expanded)
            {
                var rows = new List<(string key, string tag, Color tagColor, string name, string right)>
                {
                    (TotalKey, "∑", Theme.Text, "Tất cả phiên (tổng)", ""),
                };
                foreach (var e in d.Sessions)
                    rows.Add((e.Key, e.Kind, e.Kind == "OMP" ? Theme.Purple : Theme.Blue, e.Name + (e.Legacy ? " (mod cũ)" : ""),
                        (e.Percent != null ? $"{e.Percent}% · " : "") + (e.Tokens != null ? Fmt.Tok(e.Tokens.Value) : "")));
                foreach (var row in rows)
                {
                    var rr = new RectangleF(P - S(4), y - S(2), iw + S(8), S(17));
                    var on = row.key == (d.Selected?.Key ?? TotalKey);
                    if (g != null)
                    {
                        if (on) { using var hb = new SolidBrush(Theme.Track); FillRounded(g, hb, rr, S(3)); }
                        Hits.Add((rr, row.key));
                    }
                    Text(row.tag, fChip, row.tagColor, P, y + S(1.5f));
                    var rightW = TextW(row.right, fMeta);
                    var name = row.name;
                    while (name.Length > 4 && TextW(name, fMeta) > iw - S(30) - rightW - S(6)) name = name[..^2] + "…";
                    Text(name, fMeta, on ? Theme.Text : Theme.Dim, P + S(28), y);
                    Text(row.right, fMeta, Theme.Dim, width - P, y, right: true);
                    y += S(17);
                }
                y += S(6);
            }
        }

        if (d == null)
        {
            Text("Chưa có dữ liệu.", fMeta, Theme.Dim, P, y);
            Text("Mở một phiên Claude Code hoặc OMP.", fMeta, Theme.Faint, P, y + S(14));
            return (int)(y + S(34) + P);
        }

        // Hạn mức
        foreach (var l in d.Limits)
        {
            Text(l.Label, fCaps, Theme.Dim, P, y + S(8));
            if (d.Sources.Contains('+') && l.Source != "CC")
                Text(l.Source.ToLowerInvariant(), fMeta, l.Source == "API" ? Theme.Blue : Theme.Purple, P + TextW(l.Label, fCaps) + S(4), y + S(8));
            Text(Math.Round(l.PercentUsed) + "%", fBig, Theme.Heat(l.PercentUsed), width - P + S(4), y - S(2), right: true);
            y += S(28);
            string? paceTip = null;
            if (l.ExpectedPercent is { } ex)
            {
                var diff = (int)Math.Round(l.PercentUsed - ex);
                paceTip = $"Mốc nhịp ▼ {ex:0}%: đã qua {ex:0}% thời gian của kỳ {l.Label.ToLowerInvariant()}.\n"
                        + "Dùng đều tay thì lúc này đã dùng đúng tới mốc này.\n"
                        + (diff > 0 ? $"Phần tô vượt mốc {diff}%: đang dùng nhanh hơn nhịp."
                           : diff < 0 ? $"Phần tô còn cách mốc {-diff}%: đang dùng chậm hơn nhịp."
                           : "Phần tô đúng bằng mốc: dùng đúng nhịp.")
                        + "\n(Không phải mốc compact: compact là vạch cam nét đứt trên thanh context.)";
            }
            Bar(g, P, y, iw, S(8), l.PercentUsed, Theme.BarColor(l.PercentUsed), new[] { 25.0, 50, 75 }, l.ExpectedPercent, null, scale, paceTip);
            y += S(13);
            TimeSpan? resetIn = l.ResetsAt != null ? l.ResetsAt.Value - now : null;
            var warn = l.FullInMs != null && resetIn != null && TimeSpan.FromMilliseconds(l.FullInMs.Value) < resetIn;
            // Mỗi mục kèm mức ưu tiên giữ lại (cao = giữ lâu nhất) khi dòng không đủ chỗ
            var parts = new List<(string text, int keep)>();
            if (resetIn != null)
            {
                parts.Add(("reset " + Fmt.Dur(resetIn.Value), 9));
                parts.Add(("(" + Fmt.When(l.ResetsAt!.Value, now) + ")", 6));
            }
            if (warn) parts.Add(("đầy sau " + Fmt.Dur(TimeSpan.FromMilliseconds(l.FullInMs!.Value)), 8));
            if (l.RatePerUnit != null) parts.Add(($"{l.RatePerUnit.Value.ToString(l.RatePerUnit < 10 ? "0.0" : "0")}%/{l.Unit}", 2));
            if (l.ExpectedPercent != null)
            {
                var dd = (int)Math.Round(l.PercentUsed - l.ExpectedPercent.Value);
                parts.Add(($"{(dd >= 0 ? "+" : "")}{dd}% nhịp", 4));
            }
            string Join() => string.Join(" · ", parts.Select(p => p.text)).Replace(" · (", " (");
            var line = Join();
            while (parts.Count > 1 && TextW(line, fMeta) > iw)
            {
                parts.RemoveAt(parts.IndexOf(parts.MinBy(p => p.keep)));
                line = Join();
            }
            Text(line, fMeta, warn ? Theme.Orange : Theme.Dim, P, y);
            y += S(22) + extra;
        }

        if (totalView)
        {
            Rule();
            y += S(10);
            Text("PHIÊN ĐANG CHẠY", fCaps, Theme.Dim, P, y + S(2));
            y += S(18);
            foreach (var e in d.Sessions)
            {
                var top = y;
                Text(e.Kind, fChip, e.Kind == "OMP" ? Theme.Purple : Theme.Blue, P, y + S(1.5f));
                var right = e.Percent != null ? $"{e.Percent}%" : "";
                var sub = (e.Tokens != null ? Fmt.Tok(e.Tokens.Value) + (e.Window != null ? " / " + Fmt.Tok(e.Window.Value) : "") : "")
                          + (e.Model != null ? " · " + Pretty(e.Model) : "") + (e.CostUsd != null ? " · " + Fmt.Usd(e.CostUsd.Value) : "");
                var name = e.Name + (e.Legacy ? " (mod cũ)" : "");
                while (name.Length > 4 && TextW(name, fMeta) > iw - S(28) - S(40)) name = name[..^2] + "…";
                Text(name, fMeta, Theme.Text, P + S(28), y);
                var hot = e.AutoCompactAt is > 0 && e.Tokens != null ? e.Tokens.Value / e.AutoCompactAt.Value * 100 : e.Percent ?? 0;
                Text(right, fMeta, Theme.Heat(hot), width - P, y, right: true);
                y += S(14);
                double? flag = e.AutoCompactAt != null && e.Window is > 0 ? e.AutoCompactAt / e.Window * 100 : null;
                Bar(g, P + S(28), y, iw - S(28), S(4), e.Percent ?? 0, e.Kind == "OMP" ? Theme.Purple : Theme.Blue, [], null, flag, scale,
                    flagTip: e.AutoCompactAt != null && e.Window != null ? CompactTip(e.Kind, e.AutoCompactAt.Value, e.Window.Value, e.Tokens) : null);
                y += S(8);
                Text(sub, fMeta, Theme.Faint, P + S(28), y);
                y += S(16);
                if (g != null) Hits.Add((new RectangleF(P - S(4), top - S(2), iw + S(8), y - top), e.Key));
            }
            y += extra;
        }

        // Context
        if (!totalView && d.Context?.Percent != null)
        {
            Rule();
            y += S(10);
            var cp = d.Context.Percent.Value;
            Text("CONTEXT", fCaps, Theme.Dim, P, y + S(2));
            Text(Math.Round(cp) + "%", fMid, Theme.Heat(cp), width - P + S(3), y - S(2), right: true);
            y += S(20);
            double? ac = d.Context.AutoCompactAt != null && d.Context.Window > 0 ? d.Context.AutoCompactAt / d.Context.Window * 100 : null;
            Bar(g, P, y, iw, S(6), cp, cp >= 85 ? Theme.Red : Theme.Blue, [], null, ac, scale,
                flagTip: d.Context.AutoCompactAt != null ? CompactTip("CC", d.Context.AutoCompactAt.Value, d.Context.Window, d.Context.Tokens) : null);
            y += S(11);
            Text($"{Fmt.Tok(d.Context.Tokens ?? 0)} / {Fmt.Tok(d.Context.Window)}", fMeta, Theme.Dim, P, y);
            if (d.Context.AutoCompactAt != null) Text("auto-compact " + Fmt.Tok(d.Context.AutoCompactAt.Value), fMeta, Theme.Orange, width - P, y, right: true);
            y += S(20) + extra;
        }

        // Context phiên OMP gần nhất: thanh tím, vạch auto-compact theo ngưỡng của OMP
        if (!totalView && d.OmpContext is { } oc)
        {
            Rule();
            y += S(10);
            var op = oc.Percent;
            Text("CONTEXT · OMP", fCaps, Theme.Dim, P, y + S(2));
            Text(Math.Round(op) + "%", fMid, Theme.Heat(oc.AutoCompactAt > 0 ? oc.Tokens / oc.AutoCompactAt * 100 : op), width - P + S(3), y - S(2), right: true);
            y += S(20);
            Bar(g, P, y, iw, S(6), op, oc.Tokens >= oc.AutoCompactAt * 0.95 ? Theme.Red : Theme.Purple, [], null, oc.AutoCompactAt / oc.Window * 100, scale,
                flagTip: CompactTip("OMP", oc.AutoCompactAt, oc.Window, oc.Tokens));
            y += S(11);
            Text($"{Fmt.Tok(oc.Tokens)} / {Fmt.Tok(oc.Window)}", fMeta, Theme.Dim, P, y);
            Text("auto-compact " + Fmt.Tok(oc.AutoCompactAt), fMeta, Theme.Orange, width - P, y, right: true);
            y += S(13);
            var info = Fmt.Ago(now - oc.At) + " trước" + (oc.Title is { Length: > 0 } t ? " · " + t : "");
            while (info.Length > 8 && TextW(info, fMeta) > iw) info = info[..^2] + "…";
            Text(info, fMeta, now - oc.At > StaleAfter ? Theme.Faint : Theme.Dim, P, y);
            y += S(20) + extra;
        }

        // Chi phí
        if (d.Cost != null)
        {
            Rule();
            y += S(10);
            var tiles = new List<(string v, string l)>();
            if (totalView)
            {
                // Tổng: chi phí các phiên đang chạy (CC + OMP); hôm nay/7 ngày là sổ của mod (chỉ Claude Code)
                tiles.Add((Fmt.Usd(d.Sessions.Sum(e => e.CostUsd ?? 0)), "đang chạy"));
                tiles.Add((Fmt.Usd(d.Cost.Today), "hôm nay CC"));
                tiles.Add((Fmt.Usd(d.Cost.Week), "7 ngày CC"));
            }
            else
            {
                tiles.Add((Fmt.Usd(d.Cost.Session), "phiên"));
                tiles.Add((Fmt.Usd(d.Cost.PerHour), "/ giờ"));
                if (!double.IsNaN(d.Cost.Today)) tiles.Add((Fmt.Usd(d.Cost.Today), "hôm nay"));
                if (!double.IsNaN(d.Cost.Week)) tiles.Add((Fmt.Usd(d.Cost.Week), "7 ngày"));
            }
            var tw = iw / 4;
            for (var i = 0; i < tiles.Count; i++)
            {
                Text(tiles[i].v, fName, Theme.Text, P + i * tw, y);
                Text(tiles[i].l, fMeta, Theme.Faint, P + i * tw, y + S(15));
            }
            y += S(36) + extra;
        }

        // Model (chỉ khi xem một phiên, hoặc chưa có danh sách phiên)
        if (!totalView && d.Models.Count > 0)
        {
            Rule();
            y += S(10);
            foreach (var m in d.Models.Take(5))
            {
                if (g != null)
                {
                    using var b = new SolidBrush(Theme.Blue);
                    g.FillEllipse(b, P, y + S(4), S(6), S(6));
                }
                Text(m.Name, fName, Theme.Text, P + S(10), y);
                var x = P + S(12) + TextW(m.Name, fName);
                if (m.Effort != null) Chip(g, m.Effort, fChip, Theme.Effort(m.Effort), x, y + S(1), scale);
                Text($"{Math.Round(m.Share * 100)}% · {Fmt.Usd(m.Usd)}", fMeta, Theme.Blue, width - P, y + S(2), right: true);
                y += S(16);
                Bar(g, P + S(10), y, iw - S(10), S(4), m.Share * 100, Theme.Blue, [], null, null, scale);
                y += S(12);
            }
        }

        return (int)Math.Ceiling(y + P - S(4));
    }

    private static string Pretty(string id)
    {
        var m = System.Text.RegularExpressions.Regex.Match(id, @"(fable|mythos|opus|sonnet|haiku)[-_]?(\d+(?:[-.]\d+)?)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return id.Replace("claude-", "");
        var fam = char.ToUpper(m.Groups[1].Value[0]) + m.Groups[1].Value[1..].ToLower();
        return m.Groups[2].Success ? $"{fam} {m.Groups[2].Value.Replace('-', '.')}" : fam;
    }

    private static void Chip(Graphics? g, string text, Font f, Color bg, float x, float y, float scale)
    {
        if (g == null) return;
        var sz = g.MeasureString(text, f);
        var r = new RectangleF(x, y, sz.Width + 6 * scale, sz.Height + 1 * scale);
        using var b = new SolidBrush(bg);
        FillRounded(g, b, r, r.Height / 2);
        using var tb = new SolidBrush(ColorTranslator.FromHtml("#bbbbbb"));
        g.DrawString(text, f, tb, x + 3 * scale, y + 0.5f * scale);
    }

    private static string CompactTip(string kind, double at, double window, double? tokens)
    {
        var s = $"Mốc auto-compact (vạch cam nét đứt): {Fmt.Tok(at)} = {at / window * 100:0}% cửa sổ {Fmt.Tok(window)}.\n"
              + $"Context chạm vạch này thì {(kind == "OMP" ? "OMP" : "Claude Code")} tự nén hội thoại.";
        if (tokens is { } t) s += t < at ? $"\nCòn {Fmt.Tok(at - t)} tokens nữa." : "\nĐã chạm mốc: lượt tới sẽ nén.";
        return s;
    }

    private static void Bar(Graphics? g, float x, float y, float w, float h, double pct, Color fill, double[] marks, double? pace, double? flag, float scale,
        string? paceTip = null, string? flagTip = null)
    {
        if (g == null) return;
        var r = h / 2;
        using (var tb = new SolidBrush(Theme.Track)) FillRounded(g, tb, new RectangleF(x, y, w, h), r);
        var f = (float)Math.Clamp(pct / 100, 0, 1);
        if (f > 0)
        {
            var fr = new RectangleF(x, y, Math.Max(h, w * f), h);
            using var lb = new LinearGradientBrush(new RectangleF(x - 1, y, fr.Width + 2, h), ControlPaint.Dark(fill, 0.15f), fill, LinearGradientMode.Horizontal);
            FillRounded(g, lb, fr, r);
        }
        using (var mb = new SolidBrush(Theme.Tick))
            foreach (var m in marks)
                g.FillRectangle(mb, x + (float)(m / 100) * w - 0.5f * scale, y + 2 * scale, 1 * scale, Math.Max(1, h - 4 * scale));
        if (pace != null)
        {
            // Mốc nhịp: tam giác ▼ màu chữ trên thanh + đường mảnh liền xuyên thanh (khác hẳn vạch cam nét đứt của compact)
            var px = x + (float)(Math.Clamp(pace.Value, 0, 100) / 100) * w;
            using var pb = new SolidBrush(Theme.Text);
            var tw = 4.5f * scale;
            g.FillPolygon(pb, new[] { new PointF(px - tw, y - 6 * scale), new PointF(px + tw, y - 6 * scale), new PointF(px, y - 0.5f * scale) });
            g.FillRectangle(pb, px - 0.75f * scale, y, 1.5f * scale, h + 2 * scale);
            if (paceTip != null) Tips.Add((new RectangleF(px - 8 * scale, y - 9 * scale, 16 * scale, h + 14 * scale), paceTip));
        }
        if (flag != null)
        {
            using var pen = new Pen(Theme.Orange, 2 * scale) { DashPattern = [2f, 1.5f] };
            var fx = x + (float)(Math.Min(100, flag.Value) / 100) * w;
            g.DrawLine(pen, fx, y - 4 * scale, fx, y + h + 4 * scale);
            if (flagTip != null) Tips.Add((new RectangleF(fx - 8 * scale, y - 7 * scale, 16 * scale, h + 14 * scale), flagTip));
        }
    }

    private static void FillRounded(Graphics g, Brush b, RectangleF r, float radius)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
        if (radius < 0.5f) { g.FillRectangle(b, r); return; }
        using var path = new GraphicsPath();
        var dd = radius * 2;
        path.AddArc(r.X, r.Y, dd, dd, 180, 90);
        path.AddArc(r.Right - dd, r.Y, dd, dd, 270, 90);
        path.AddArc(r.Right - dd, r.Bottom - dd, dd, dd, 0, 90);
        path.AddArc(r.X, r.Bottom - dd, dd, dd, 90, 90);
        path.CloseFigure();
        g.FillPath(b, path);
    }
}
