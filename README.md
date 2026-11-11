# claude-usage-tray

Icon khay hệ thống (Windows) hiển thị % hạn mức phiên 5 giờ của Claude Code. Bấm vào icon mở bảng chi tiết theo theme Darcula: phiên 5 giờ, tuần (nhịp dùng, dự báo đầy), context so với mốc auto-compact, chi phí quy ra $ (phiên, /giờ, hôm nay, 7 ngày), model và effort.

Nguồn số liệu: mod Claude Code `usage-meter` ghi `%USERPROFILE%\.claude\usage-meter.json`. Không có phiên Claude Code chạy thì số đứng yên; quá 15 phút không cập nhật thì icon mờ đi.

- Mantis: (không có)
- Hướng dẫn cho agent: `AGENTS.md`

## Dùng

1. Build: `dotnet build src/ClaudeUsageTray -c Release -nologo`
2. Chạy `src/ClaudeUsageTray/bin/Release/net9.0-windows/ClaudeUsageTray.exe`. Windows 11 đặt icon mới vào vùng ẩn (mũi tên ^); kéo icon ra thanh taskbar để luôn thấy.
3. Chuột trái: mở/đóng bảng. Chuột phải: Mở bảng, Chạy cùng Windows, Mở thư mục dữ liệu, Thoát.

Màu số trên icon: vàng < 65%, cam ≥ 65%, đỏ ≥ 85%; "!!" khi 100%, "--" khi chưa có dữ liệu.

## Nhật ký

- 2026-11-11: Tạo repo. Viết app tray C# .NET 9 WinForms (icon vẽ GDI+, bảng popup Darcula, FileSystemWatcher + debounce 300ms, làm mới 30s, HKCU Run). Smoke GUI bằng chuột thật trên máy anh: bảng hiện đúng số trong JSON, bấm ra ngoài thì ẩn. Thêm log chẩn đoán bật bằng `CLAUDE_USAGE_TRAY_LOG=1`, bỏ qua mất focus trong 400ms đầu sau khi hiện bảng.
