# Vẽ app.ico (16..256 px, PNG trong ICO) cho exe và shortcut: nền Darcula, ba thanh hạn mức. Chạy lại khi đổi kiểu icon.
Add-Type -AssemblyName System.Drawing
$sizes = 16, 24, 32, 48, 64, 256
$pngs = foreach ($n in $sizes) {
    $b = New-Object Drawing.Bitmap $n, $n
    $g = [Drawing.Graphics]::FromImage($b)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([Drawing.Color]::Transparent)
    $r = [single]($n * 0.2); $p = New-Object Drawing.Drawing2D.GraphicsPath
    $p.AddArc(0, 0, 2 * $r, 2 * $r, 180, 90); $p.AddArc($n - 2 * $r - 1, 0, 2 * $r, 2 * $r, 270, 90)
    $p.AddArc($n - 2 * $r - 1, $n - 2 * $r - 1, 2 * $r, 2 * $r, 0, 90); $p.AddArc(0, $n - 2 * $r - 1, 2 * $r, 2 * $r, 90, 90); $p.CloseFigure()
    $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#2b2b2b'))), $p)
    $track = New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#3c3f41'))
    $bars = @(@(0.35, '#6a8759'), @(0.62, '#ffc66d'), @(0.85, '#cc7832'))
    $x = $n * 0.16; $w = $n * 0.68; $h = [Math]::Max(2, $n * 0.13)
    for ($i = 0; $i -lt 3; $i++) {
        $y = $n * (0.24 + $i * 0.21)
        $g.FillRectangle($track, $x, $y, $w, $h)
        $g.FillRectangle((New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml($bars[$i][1]))), $x, $y, $w * $bars[$i][0], $h)
    }
    $g.Dispose()
    $ms = New-Object IO.MemoryStream; $b.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
    , $ms.ToArray()
}
$out = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$off = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i] % 256
    $bw.Write([byte]$s); $bw.Write([byte]$s); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$off)
    $off += $pngs[$i].Length
}
foreach ($d in $pngs) { $bw.Write($d) }
[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'app.ico'), $out.ToArray())
