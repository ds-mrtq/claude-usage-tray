# claude-usage-tray

Icon khay hệ thống (Windows) hiển thị % hạn mức phiên 5 giờ của Claude Code. Bấm vào icon mở bảng chi tiết theo theme Darcula: phiên 5 giờ, tuần (nhịp dùng, dự báo đầy), context so với mốc auto-compact, chi phí quy ra $ (phiên, /giờ, hôm nay, 7 ngày), model và effort.

Nguồn số liệu, mỗi hạn mức lấy bản đo mới hơn:
- mod Claude Code `usage-meter` ghi `%USERPROFILE%\.claude\usage-meter.json` (hạn mức, context, chi phí, model);
- OMP ghi `%USERPROFILE%\.omp\agent\agent.db`, bảng `usage_history` (5 giờ, tuần, tuần Fable) mỗi lần OMP lấy hạn mức.

Header ghi nguồn đang dùng (CC, OMP, CC+OMP); dòng lấy từ OMP có nhãn tím "omp". Quá 15 phút không có bản đo mới thì icon mờ đi.

- Mantis: (không có)
- Hướng dẫn cho agent: `AGENTS.md`

## Dùng

1. Build: `dotnet build src/ClaudeUsageTray -c Release -nologo`
2. Chạy `src/ClaudeUsageTray/bin/Release/net9.0-windows/ClaudeUsageTray.exe`. Windows 11 đặt icon mới vào vùng ẩn (mũi tên ^); kéo icon ra thanh taskbar để luôn thấy.
3. Chuột trái vào icon: mở bảng. Bảng ghim nổi trên mọi cửa sổ, không tắt khi bấm ra ngoài hay đổi cửa sổ; tắt bằng nút x góc phải hoặc bấm lại icon. Kéo phần đầu bảng để di chuyển; kéo viền hoặc góc để đổi cỡ (chữ và biểu đồ phóng theo); nút ô vuông cạnh nút x phóng bảng chiếm trọn màn hình đang chứa nó (màn hình chính hay phụ), bấm lại hoặc kéo phần đầu bảng để trả về. Vị trí, cỡ và trạng thái phóng to được nhớ cho lần chạy sau (`%LOCALAPPDATA%\ClaudeUsageTray\window.json`; xoá file này để về mặc định).
4. Chuột phải: Mở bảng, Chạy cùng Windows, Mở thư mục dữ liệu, Thoát.

Màu số trên icon: vàng < 65%, cam ≥ 65%, đỏ ≥ 85%; "!!" khi 100%, "--" khi chưa có dữ liệu.

## Nhật ký

- 2026-11-11: Tạo repo. Viết app tray C# .NET 9 WinForms (icon vẽ GDI+, bảng popup Darcula, FileSystemWatcher + debounce 300ms, làm mới 30s, HKCU Run). Smoke GUI bằng chuột thật trên máy anh: bảng hiện đúng số trong JSON, bấm ra ngoài thì ẩn. Thêm log chẩn đoán bật bằng `CLAUDE_USAGE_TRAY_LOG=1`, bỏ qua mất focus trong 400ms đầu sau khi hiện bảng.
- 2026-11-11: Bật chạy cùng Windows (HKCU Run, trỏ exe bản Release trong repo).
- 2026-11-11: Bảng thành overlay ghim (không tự ẩn, nút x, kéo header, tự hiện lại khi bị thu nhỏ hay bị đẩy xuống). Thêm nguồn OMP từ `~/.omp/agent/agent.db` (`usage_history`), có thêm dòng Tuần · Fable; tham số `--omp <đường dẫn | none>`.
- 2026-11-11: Chỉ nạp winsqlite3.dll từ System32. Lưu vị trí/cỡ/trạng thái phóng to vào `window.json`; kéo viền đổi cỡ (zoom 0.6–4, giữ tỉ lệ); nút phóng to tối đa. Smoke bằng chuột thật có chặn bấm nhầm.
- 2026-11-11: Sửa phóng to trên màn hình phụ dựng dọc (trước chỉ cao 1041/1392 vì giữ tỉ lệ): nay chiếm trọn vùng làm việc, giãn khoảng dọc giữa các mục. Kéo khi đang phóng to thì trả về cỡ thường dưới con trỏ.
