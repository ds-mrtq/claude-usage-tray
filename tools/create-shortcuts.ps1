# Tạo shortcut "Claude Usage" trên Desktop và Start Menu, trỏ tới exe bản Release, chạy với --show (mở bảng ngay;
# app đang chạy thì chỉ mở bảng của bản đang chạy). Build trước: dotnet build src/ClaudeUsageTray -c Release
$exe = Join-Path $PSScriptRoot '..\src\ClaudeUsageTray\bin\Release\net9.0-windows\ClaudeUsageTray.exe' | Resolve-Path -ErrorAction Stop
$ws = New-Object -ComObject WScript.Shell
foreach ($dir in [Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs')) {
    $path = Join-Path $dir 'Claude Usage.lnk'
    $l = $ws.CreateShortcut($path)
    $l.TargetPath = "$exe"; $l.Arguments = '--show'; $l.WorkingDirectory = Split-Path "$exe"
    $l.IconLocation = "$exe,0"; $l.Description = 'Mở bảng hạn mức Claude (Claude Usage)'
    $l.Save()
    "Đã tạo $path"
}
