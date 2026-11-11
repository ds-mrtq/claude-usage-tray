# claude-usage-tray: Widget khay hệ thống hiển thị hạn mức Claude Code

Luật chung nằm ở `~/.omp/agent/AGENTS.md`, không chép lại ở đây. File này chỉ ghi điều riêng của repo.

## Bối cảnh

- Mantis: (không có, công cụ cá nhân)
- Người dùng / đơn vị: chủ máy, Windows 11
- Hệ thống liên quan: mod Claude Code `usage-meter` ở `mod/usage-meter` trong repo (marketplace `claude-usage-tray` ở `.claude-plugin/marketplace.json`) ghi `%USERPROFILE%\.claude\usage-meter.json`; app khay chỉ đọc file đó. Sửa mod: chạy `claude plugin test mod/usage-meter`, phiên đang mở cần `/reload-plugins`
- Mã nguồn: C# .NET 9 WinForms, `src/ClaudeUsageTray`
- GitHub: https://github.com/ds-mrtq/claude-usage-tray (công khai). Repo công khai: không ghi tên thật, email, đường dẫn `C:\Users\<tên>`, tên công ty/hệ thống nội bộ vào file hay message commit; commit bằng email noreply đã đặt trong `git config` của repo

## Facts

Trạng thái hiện hành của repo, tối đa 15 dòng; chi tiết và lịch sử ở `README.md`. Luật ghi và đối chiếu: mục "Facts của repo" trong `~/.omp/agent/AGENTS.md`.

Định dạng: `- [active|open|dispute|cần xác nhận] nội dung | as_of YYYY-MM-DD | nguồn (file output, commit, Mantis #) | tin cậy 1/2/3`

- [active] App đọc `~/.claude/usage-meter.json` (schema 1) do mod usage-meter ghi sau mỗi lượt và mỗi lần đo; file là của phiên Claude Code ghi gần nhất | as_of 2026-11-11 | hooks/register.tsx (exportJson) của mod | tin cậy 1
- [active] Mod cài scope user từ repo: `usage-meter@claude-usage-tray` 0.2.0, đọc tại chỗ `mod/usage-meter` (đã gỡ bản cài cũ từ marketplace cá nhân ở `~/.claude/mods`); kho `$.store` gắn với nguồn cài nên sổ chi phí ngày bắt đầu lại sau lần đổi này | as_of 2026-11-11 | `claude plugin list`; phiên `claude -p` mới cho today=week=phiên=$0.0119 | tin cậy 1
- [active] Sổ chi phí: khoá `d:<ngày>:<sid>:<startedAt>` = chi phí hiện tại − mốc `b:` đầu ngày, ghi đè (không cộng delta); khoá cũ `d:<ngày>:<sid>` bị bỏ khi đã có khoá mới. Lỗi cũ: hôm nay $14.64 < phiên $27.07 do mốc lastCost $12.43 sống qua lần cài lại còn kho thì trống | as_of 2026-11-11 | snapshot mod lúc 11:06, test "sổ chi phí" trong tests/pane.test.tsx | tin cậy 1
- [active] API mod Claude Code chỉ có five_hour, seven_day; Tuần · Fable lấy từ OMP | as_of 2026-11-11 | đối chiếu get_usage của app với snapshot mod | tin cậy 1
- [active] Nguồn thứ hai: OMP ghi hạn mức vào `~/.omp/agent/agent.db` bảng `usage_history` (limit_id anthropic:5h, anthropic:7d, anthropic:7d:fable, anthropic:extra) mỗi lần OMP lấy hạn mức; app đọc chỉ-đọc qua winsqlite3.dll, mỗi hạn mức lấy bản đo mới hơn giữa hai nguồn; context/chi phí/model chỉ có từ phiên Claude Code | as_of 2026-11-11 | truy vấn agent.db + `--render` ra "CC+OMP" và "OMP" | tin cậy 1
- [active] anthropic:extra (Extra Usage) của OMP đo lần cuối đầu tháng 10/2026 nên bị lọc (chỉ giữ hạn mức đo trong 6 giờ quanh bản mới nhất) | as_of 2026-11-11 | max(recorded_at)=1791167113939 trong usage_history | tin cậy 1
- [active] stats.db của OMP (chi phí theo message) chỉ cập nhật khi chạy `omp stats`, bản mới nhất tháng 9/2026: chưa dùng làm nguồn chi phí | as_of 2026-11-11 | max(timestamp) bảng messages | tin cậy 1
- [active] Bảng là overlay ghim: TOPMOST, WS_EX_NOACTIVATE, không ẩn khi mất focus; chỉ ẩn khi bấm nút x hoặc bấm lại icon khay; kéo header để di chuyển | as_of 2026-11-11 | PopupForm.cs | tin cậy 1
- [active] Smoke GUI PASS bằng chuột/phím thật (SetCursorPos/mouse_event/keybd_event): mở bằng icon; bấm ra Explorer, Win+D hai lần bảng vẫn nổi trên cùng; nút x đóng; kéo header (2210,980)→(1710,680); bấm lại icon → ẩn | as_of 2026-11-11 | %TEMP%\ClaudeUsageTray.log, scratchpad smoke.ps1 | tin cậy 1
- [active] Vị trí, độ phóng (zoom 0.6–4, kéo viền/góc, nội dung phóng theo, giữ tỉ lệ) và trạng thái phóng to tối đa (chiếm trọn vùng làm việc của màn hình đang chứa bảng, kể cả màn hình phụ dựng dọc 900x1440; chữ phóng theo chiều hẹp hơn, dư dọc thì giãn khoảng giữa các mục; kéo khi đang phóng to thì tự trả về cỡ thường) lưu ở `%LOCALAPPDATA%\ClaudeUsageTray\window.json`, khôi phục khi chạy lại; vị trí nằm ngoài mọi màn hình thì về góc khay; menu chuột phải "Đặt lại vị trí và cỡ" xoá các giá trị này, về cỡ gốc ở góc khay (smoke chuột thật PASS) | as_of 2026-11-11 | PopupForm.cs, WindowSettings.cs | tin cậy 1
- [active] Smoke PASS lưu vị trí/đổi cỡ/phóng to bằng chuột thật có chặn bấm nhầm (UI Automation tìm icon, WindowFromPoint kiểm cửa sổ): kéo header (2210,980)→(1710,580); kéo góc 340x402→510x603 (zoom 1.5); phóng to 1160x1373 tại (700,10), trả về đúng chỗ; tắt/chạy lại app mở đúng (1710,580) zoom 1.5; phóng to rồi tắt, chạy lại vẫn phóng to; nút x đóng. Smoke hai màn hình: phóng to trên màn phụ (-890,270) 880x1372, màn chính (10,10) 2540x1372, kéo lúc phóng to từ chính sang phụ → về 510x603 rồi phóng to lại đúng màn phụ | as_of 2026-11-11 | %TEMP%\ClaudeUsageTray.log, scratchpad smoke2.ps1, smoke3.ps1, max-secondary.png, max-primary.png | tin cậy 1
- [open] Bảng ở góc phải dưới đè lên vùng icon ẩn (^) của Windows; nên kéo icon ra taskbar hoặc kéo bảng đi chỗ khác (vị trí được lưu) | as_of 2026-11-11 | ảnh chụp flyout.png khi smoke | tin cậy 1
- [active] Đã bật chạy cùng Windows: HKCU\Software\Microsoft\Windows\CurrentVersion\Run, giá trị ClaudeUsageTray trỏ tới exe bản Release trong repo; build lại khi app đang chạy sẽ lỗi khoá file, phải Thoát app trước | as_of 2026-11-11 | đọc lại registry sau khi ghi | tin cậy 1

## Chạy và kiểm chứng

- Build: `dotnet build src/ClaudeUsageTray -c Release -nologo` (0 warning)
- Chạy: `src/ClaudeUsageTray/bin/Release/net9.0-windows/ClaudeUsageTray.exe [--data <file json>]`; chỉ một bản chạy (mutex `Local\ClaudeUsageTray`)
- Kiểm không giao diện: `ClaudeUsageTray.exe --render <thư mục>` xuất `panel.png`, `icon.png`, in tooltip; exit 2 nếu không đọc được JSON
- Log chẩn đoán: đặt `CLAUDE_USAGE_TRAY_LOG=1` trước khi chạy, log ở `%TEMP%\ClaudeUsageTray.log` (click, hiện/ẩn bảng)
- Smoke GUI: computer-use che cửa sổ app tự build khỏi ảnh chụp, nên bấm icon bằng chuột thật rồi xác nhận qua log + chụp vùng màn hình bằng `Graphics.CopyFromScreen`
