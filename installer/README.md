# Bộ cài TTC Weighing

## Build

Cần .NET 8 SDK và [Inno Setup 6](https://jrsoftware.org/isdl.php).

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 -Version 1.0.0
```

Kết quả: `installer\out\TTC-Weighing-Setup-1.0.0.exe`. Lần đầu script tải PostgreSQL 16 (~370 MB) và
VC++ runtime vào `installer\.cache` (đã gitignore).

`installer\bundle-settings.json` (gitignore, **không commit**) chứa mã site, địa chỉ API và tài khoản FRM.
Nội dung này được đóng vào bộ cài và ghi thành `appsettings.Local.json` trên máy trạm.
Đổi sang production: sửa các `BaseUrl` / tài khoản trong tệp này rồi build lại.

## Người cài làm gì

Chạy Setup → chọn thư mục (mặc định `C:\TTC Weighing`, đổi sang ổ khác được) → Next → xong.
Bộ cài tự:

- Chép app self-contained (không cần cài .NET) và PostgreSQL 16.
- Cài VC++ runtime.
- Tạo CSDL trong `<thư mục cài>\data\pgdata`, chạy thành dịch vụ `TTCWeighingDB` (tự khởi động cùng Windows,
  chỉ nghe `localhost`, cổng trống đầu tiên từ 5432), nạp `db/schema.sql` + `db/seed.sql`.
- Sinh mật khẩu DB ngẫu nhiên, lưu ở `<thư mục cài>\data\db-secrets.json` (chỉ Administrators đọc được).
- Ghi `<thư mục cài>\appsettings.Local.json`.

Nhật ký cài đặt: `<thư mục cài>\data\logs`.

## Nâng cấp / gỡ

- Cài bản mới đè lên: tự dùng lại thư mục cũ, giữ dữ liệu và `appsettings.Local.json`, không nạp lại schema.
- Gỡ cài đặt: xóa app và dịch vụ, **giữ** thư mục `data` (xóa tay nếu thật sự muốn bỏ dữ liệu).
- Cài im lặng: `TTC-Weighing-Setup-1.0.0.exe /VERYSILENT /DIR="D:\TTC Weighing"`

## Hạn chế

- Nâng cấp **không** tự chạy migration schema. Khi `db/schema.sql` đổi, phải viết script nâng cấp riêng.
- Giao diện bộ cài chỉ có tiếng Việt.
