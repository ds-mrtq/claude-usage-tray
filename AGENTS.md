# claude-usage-tray: Widget khay hệ thống hiển thị hạn mức Claude Code

Luật chung nằm ở `~/.omp/agent/AGENTS.md`, không chép lại ở đây. File này chỉ ghi điều riêng của repo.

## Bối cảnh

- Mantis: (không có, công cụ cá nhân)
- Người dùng / đơn vị: chủ máy, Windows 11
- Hệ thống liên quan: mod Claude Code `usage-meter` (`%USERPROFILE%\.claude\mods\usage-meter`, ngoài repo) ghi `%USERPROFILE%\.claude\usage-meter.json`; app này chỉ đọc file đó
- Mã nguồn: C# .NET 9 WinForms, `src/ClaudeUsageTray`

## Facts

Trạng thái hiện hành của repo, tối đa 15 dòng; chi tiết và lịch sử ở `README.md`. Luật ghi và đối chiếu: mục "Facts của repo" trong `~/.omp/agent/AGENTS.md`.

Định dạng: `- [active|open|dispute|cần xác nhận] nội dung | as_of YYYY-MM-DD | nguồn (file output, commit, Mantis #) | tin cậy 1/2/3`

- [active] App đọc `~/.claude/usage-meter.json` (schema 1) do mod usage-meter ghi sau mỗi lượt và mỗi lần đo; file là của phiên Claude Code ghi gần nhất | as_of 2026-11-11 | hooks/register.tsx (exportJson) của mod | tin cậy 1
- [active] Không có Weekly·Fable và usage credits: API mod chỉ có five_hour, seven_day | as_of 2026-11-11 | đối chiếu get_usage của app với snapshot mod | tin cậy 1
- [active] Phiên OMP không ghi file (OMP không nạp mod Claude Code); số chỉ cập nhật khi có phiên Claude Code chạy | as_of 2026-11-11 | suy luận từ cơ chế mod | tin cậy 2
- [active] Smoke GUI PASS: bấm chuột thật vào icon khay → bảng hiện tại (2210,1043) 340x339, số khớp JSON; bấm ra ngoài → ẩn | as_of 2026-11-11 | %TEMP%\ClaudeUsageTray.log + ảnh chụp vùng màn hình | tin cậy 1
- [active] Đã bật chạy cùng Windows: HKCU\Software\Microsoft\Windows\CurrentVersion\Run, giá trị ClaudeUsageTray trỏ tới exe bản Release trong repo; build lại khi app đang chạy sẽ lỗi khoá file, phải Thoát app trước | as_of 2026-11-11 | đọc lại registry sau khi ghi | tin cậy 1

## Chạy và kiểm chứng

- Build: `dotnet build src/ClaudeUsageTray -c Release -nologo` (0 warning)
- Chạy: `src/ClaudeUsageTray/bin/Release/net9.0-windows/ClaudeUsageTray.exe [--data <file json>]`; chỉ một bản chạy (mutex `Local\ClaudeUsageTray`)
- Kiểm không giao diện: `ClaudeUsageTray.exe --render <thư mục>` xuất `panel.png`, `icon.png`, in tooltip; exit 2 nếu không đọc được JSON
- Log chẩn đoán: đặt `CLAUDE_USAGE_TRAY_LOG=1` trước khi chạy, log ở `%TEMP%\ClaudeUsageTray.log` (click, hiện/ẩn bảng)
- Smoke GUI: computer-use che cửa sổ app tự build khỏi ảnh chụp, nên bấm icon bằng chuột thật rồi xác nhận qua log + chụp vùng màn hình bằng `Graphics.CopyFromScreen`
