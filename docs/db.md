
Mở file `src/TTC.Weighing.WinForms/appsettings.Development.json`, đặt:

```json
{
  "Preview": { "Enabled": true }
}
```

App chạy hoàn toàn trong bộ nhớ với dữ liệu mẫu và thiết bị mô phỏng. Không cần Docker hay PostgreSQL.

### Chạy trên PostgreSQL thật

```powershell
# 1. Khởi động database
docker compose up -d

# 2. Áp cấu trúc bảng
$env:TTC_WEIGHING_ConnectionStrings__WeighingDb = `
  "Host=localhost;Port=5432;Database=ttc_weighing;Username=ttc_dev;Password=ttc_dev_local_only"
dotnet ef database update --project src/TTC.Weighing.Infrastructure

# 3. Nạp danh mục nền và chạy kịch bản mẫu (tùy chọn)
dotnet run --project tools/TTC.Weighing.Scenario

# 4. Mở giao diện
dotnet run --project src/TTC.Weighing.WinForms
```

Đặt `"Preview": { "Enabled": false }` trong `appsettings.Development.json` để app đọc database thật.

### Bảng cấu hình trong appsettings.Development.json

| Cờ | Ý nghĩa |
|---|---|
| `Preview:Enabled = true` | Dữ liệu trong bộ nhớ, không cần database |
| `Preview:Enabled = false` | Dùng PostgreSQL thật qua `ConnectionStrings:WeighingDb` |
| `Devices:UseSimulated = true` | Đầu cân, AI vị trí, cân mẫu và barie giả lập |

---
