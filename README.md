# claude-usage-tray

Icon khay hệ thống (Windows) hiển thị % hạn mức phiên 5 giờ của Claude Code. Bấm vào icon mở bảng chi tiết theo theme Darcula: phiên 5 giờ, tuần (nhịp dùng, dự báo đầy), context so với mốc auto-compact, chi phí quy ra $ (phiên, /giờ, hôm nay, 7 ngày), model và effort.

Nguồn số liệu, mỗi hạn mức lấy bản đo mới hơn:
- mod Claude Code `usage-meter` ghi `%USERPROFILE%\.claude\usage-meter.json` (hạn mức, context, chi phí, model);
- OMP ghi `%USERPROFILE%\.omp\agent\agent.db`, bảng `usage_history` (5 giờ, tuần, tuần Fable) mỗi lần OMP lấy hạn mức.

Header ghi nguồn đang dùng (CC, OMP, CC+OMP); dòng lấy từ OMP có nhãn tím "omp". Quá 15 phút không có bản đo mới thì icon mờ đi.

- Mantis: (không có)
- Hướng dẫn cho agent: `AGENTS.md`

## Thành phần

| Thư mục | Là gì |
|---|---|
| `mod/usage-meter` | Mod Claude Code: pane hạn mức trong Claude Code (desktop vẽ SVG, terminal/VS Code/mobile dạng chữ), lệnh `/usage-pane`, tool `snapshot`; ghi `~/.claude/usage-meter.json` sau mỗi lượt |
| `src/ClaudeUsageTray` | App khay Windows đọc file JSON trên và hạn mức của OMP |
| `.claude-plugin/marketplace.json` | Marketplace để cài mod từ repo này |

## Cài mod usage-meter

Trong Claude Code:

```
/plugin marketplace add ds-mrtq/claude-usage-tray
/plugin install usage-meter@claude-usage-tray
```

Hoặc từ bản clone: `claude plugin marketplace add <thư mục repo>` rồi `claude plugin install usage-meter@claude-usage-tray --scope user`. Phiên đang mở chạy `/reload-plugins` để nạp. Test: `claude plugin test mod/usage-meter`.

Ý nghĩa các ô chi phí: **phiên** là chi phí lần chạy hiện tại của phiên (app báo cộng dồn từ lúc tiến trình khởi động); **/ giờ** = phiên chia thời gian chạy; **hôm nay** và **7 ngày** cộng mọi phiên Claude Code theo ngày giờ máy. Luôn có hôm nay ≥ phần của phiên hiện tại trong ngày và 7 ngày ≥ hôm nay; "/ giờ" là tốc độ nên có thể lớn hơn "hôm nay" khi phiên mới chạy chưa tới một giờ. Sổ chi phí lưu trong kho của mod, gắn với nguồn cài: đổi marketplace thì lịch sử ngày bắt đầu lại.

## Dùng app khay

1. Build: `dotnet build src/ClaudeUsageTray -c Release -nologo`
2. Chạy `src/ClaudeUsageTray/bin/Release/net9.0-windows/ClaudeUsageTray.exe`. Windows 11 đặt icon mới vào vùng ẩn (mũi tên ^); kéo icon ra thanh taskbar để luôn thấy.
3. Chuột trái vào icon: mở bảng. Bảng ghim nổi trên mọi cửa sổ, không tắt khi bấm ra ngoài hay đổi cửa sổ; tắt bằng nút x góc phải hoặc bấm lại icon. Kéo phần đầu bảng để di chuyển; kéo viền hoặc góc để đổi cỡ (chữ và biểu đồ phóng theo); nút ô vuông cạnh nút x phóng bảng chiếm trọn màn hình đang chứa nó (màn hình chính hay phụ), bấm lại hoặc kéo phần đầu bảng để trả về. Vị trí, cỡ và trạng thái phóng to được nhớ cho lần chạy sau (`%LOCALAPPDATA%\ClaudeUsageTray\window.json`; xoá file này để về mặc định).
4. Chuột phải: Mở bảng, Làm mới, Đặt lại vị trí và cỡ (về góc khay, cỡ gốc, xoá vị trí đã lưu), Chạy cùng Windows, Mở thư mục dữ liệu, Thoát.

Màu số trên icon: vàng < 65%, cam ≥ 65%, đỏ ≥ 85%; "!!" khi 100%, "--" khi chưa có dữ liệu.
5. Làm mới (nút mũi tên vòng ở header hoặc menu): lấy hạn mức mới nhất thẳng từ Anthropic (`api.anthropic.com/api/oauth/usage`, endpoint không công bố) bằng token OAuth Claude Code để ở `~/.claude/.credentials.json`. App chỉ đọc token, không tự làm mới token, không ghi token ra log, chỉ gửi token tới api.anthropic.com; gửi User-Agent `claude-code/<phiên bản>` lấy từ `claude --version`; giãn cách tối thiểu 30 giây, gặp 429 thì chờ theo `retry-after`. Hạn mức lấy theo cách này gắn nhãn `api`. Chi phí và context vẫn lấy từ file của mod. Kiểm không giao diện: `--render <thư mục> --api`.
6. Shortcut: `powershell -ExecutionPolicy Bypass -File tools/create-shortcuts.ps1` tạo "Claude Usage" trên Desktop và Start Menu (chạy exe với `--show`). App chưa chạy thì khởi động và mở bảng; đang chạy thì bản đang chạy mở bảng, không sinh bản thứ hai.

## Nhật ký

- 2026-11-11: Tạo repo. Viết app tray C# .NET 9 WinForms (icon vẽ GDI+, bảng popup Darcula, FileSystemWatcher + debounce 300ms, làm mới 30s, HKCU Run). Smoke GUI bằng chuột thật trên máy anh: bảng hiện đúng số trong JSON, bấm ra ngoài thì ẩn. Thêm log chẩn đoán bật bằng `CLAUDE_USAGE_TRAY_LOG=1`, bỏ qua mất focus trong 400ms đầu sau khi hiện bảng.
- 2026-11-11: Bật chạy cùng Windows (HKCU Run, trỏ exe bản Release trong repo).
- 2026-11-11: Bảng thành overlay ghim (không tự ẩn, nút x, kéo header, tự hiện lại khi bị thu nhỏ hay bị đẩy xuống). Thêm nguồn OMP từ `~/.omp/agent/agent.db` (`usage_history`), có thêm dòng Tuần · Fable; tham số `--omp <đường dẫn | none>`.
- 2026-11-11: Chỉ nạp winsqlite3.dll từ System32. Lưu vị trí/cỡ/trạng thái phóng to vào `window.json`; kéo viền đổi cỡ (zoom 0.6–4, giữ tỉ lệ); nút phóng to tối đa. Smoke bằng chuột thật có chặn bấm nhầm.
- 2026-11-11: Rà thông tin cá nhân trước khi công khai: ghi lại lịch sử bằng email noreply GitHub, bỏ tên người dùng và đường dẫn hồ sơ Windows khỏi tài liệu. Tạo repo công khai https://github.com/ds-mrtq/claude-usage-tray và push nhánh main.
- 2026-11-11: Sửa phóng to trên màn hình phụ dựng dọc (trước chỉ cao 1041/1392 vì giữ tỉ lệ): nay chiếm trọn vùng làm việc, giãn khoảng dọc giữa các mục. Kéo khi đang phóng to thì trả về cỡ thường dưới con trỏ.
- 2026-11-11: Thêm LICENSE MIT. Đưa mod usage-meter vào `mod/usage-meter` (bản 0.2.0), marketplace `claude-usage-tray` ở gốc repo; cài lại mod từ repo thay cho bản ở `~/.claude/mods`. Sửa sổ chi phí: "hôm nay" từng nhỏ hơn "phiên" ($14.64 so với $27.07) vì sổ cộng dồn delta theo mốc lần đo trước, mốc này sống sót qua lần cài lại còn kho lưu thì trống nên hụt $12.43 đầu phiên. Nay mỗi lần chạy (phiên + startedAt) ghi thẳng chi phí trong ngày = chi phí hiện tại − mốc đầu ngày; thêm test.
- 2026-11-11: Thêm lệnh "Đặt lại vị trí và cỡ" ở menu chuột phải icon khay: bỏ vị trí/zoom/phóng to đã lưu, hiện bảng cỡ gốc ở góc khay. Smoke bằng chuột thật: từ (-655,556) 510x603 về (2210,980) 340x402; `window.json` về null/1; chạy lại app vẫn mở ở chỗ mặc định.
- 2026-11-11: Thêm làm mới theo yêu cầu (nút ở header và menu "Làm mới"): gọi endpoint usage của Anthropic bằng token OAuth của Claude Code (anh đồng ý cách này), ghép thành nguồn thứ ba `API` theo bản đo mới nhất; nhãn nguồn chỉ kể nguồn còn đứng sau số đang hiện. Smoke chuột thật: bấm nút ra 5h 10%/tuần 39%/Fable 29% nhãn `api`; bấm liền lần hai báo đợi 27s; menu Làm mới sau 30s lấy lại được.
- 2026-11-11: Thêm shortcut "Claude Usage" (Desktop, Start Menu) qua `tools/create-shortcuts.ps1`, cờ `--show`, và icon cho exe (`app.ico`, vẽ bằng `make-icon.ps1`). Lần chạy thứ hai báo bản đang chạy mở bảng qua event `Local\ClaudeUsageTray.Show`. Smoke chuột/phím thật: double-click Desktop khi app tắt → khởi động và mở bảng; bấm x rồi double-click lại → cùng tiến trình mở bảng; Start → gõ tên → Enter → mở bảng.
- 2026-11-11: Thêm ô "CONTEXT · OMP": context của phiên OMP ghi gần nhất (đọc file phiên jsonl), vạch auto-compact theo cách OMP tính (mặc định 85% cửa sổ, Opus 5.5 là 850k/1M; tôn trọng `compaction.thresholdPercent`/`thresholdTokens`/`reserveTokens` trong `config.yml` của OMP), dòng dưới ghi tuổi và tiêu đề phiên. Đối chiếu: app 407k, tính tay từ jsonl 407153. Smoke: mở bảng bằng chuột thật trên icon khay, ô hiện đúng.
