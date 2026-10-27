# Cài đặt trên máy mới (clone code + tạo DB Docker)

## 1. Cài sẵn

- Git
- .NET 8 SDK: `winget install Microsoft.DotNet.SDK.8`
- Docker Desktop (đang chạy)

## 2. Clone code

```powershell
git clone https://github.com/TrinhLeHuy/Scale.git
cd Scale
```

## 3. Tạo database PostgreSQL trên Docker

```powershell
# Khởi động container ttc-weighing-db (Postgres 16, cổng 5432)
docker compose up -d

# Chờ tới khi STATUS hiện (healthy)
docker ps --filter name=ttc-weighing-db

# Nạp schema + danh mục nền (nhà máy TTC-01, 2 bàn cân, 2 profile, lịch sử migration)
docker cp db/schema.sql ttc-weighing-db:/tmp/schema.sql
docker cp db/seed.sql   ttc-weighing-db:/tmp/seed.sql
docker exec ttc-weighing-db psql -U ttc_dev -d ttc_weighing -v ON_ERROR_STOP=1 -f /tmp/schema.sql -f /tmp/seed.sql
```

Lưu ý:

- Dùng `docker cp` + `psql -f` như trên. Không pipe file qua PowerShell (`Get-Content ... | docker exec -i`)
  vì PowerShell 5 đổi tiếng Việt thành `?`.
- Không dùng `dotnet ef database update` trên DB trống: migration trong code chưa tạo đủ bảng
  (`api_call_logs`, `frm_lenh_don`, `frm_vehicles`, `erp_order_lines`, `trip_policies` và vài cột của
  `trips`, `allocations`). `db/schema.sql` là bản dump đầy đủ từ DB dev.
- Nếu đã nạp sai và muốn làm lại từ đầu (**xóa toàn bộ dữ liệu trong DB Docker**):
  `docker compose down -v` rồi chạy lại bước 3.

## 4. Cấu hình cục bộ

Tạo `src/TTC.Weighing.WinForms/appsettings.Development.json` (file này bị gitignore, không commit):

```json
{
  "ConnectionStrings": {
    "WeighingDb": "Host=localhost;Port=5432;Database=ttc_weighing;Username=ttc_dev;Password=ttc_dev_local_only;Include Error Detail=true"
  },
  "Site": { "Code": "TTC-01" },
  "Devices": { "UseSimulated": true },
  "FrmPlanLookup": { "Username": "<hỏi trưởng nhóm>", "Password": "<hỏi trưởng nhóm>" },
  "FrmPolicyApi":  { "Username": "<hỏi trưởng nhóm>", "Password": "<hỏi trưởng nhóm>" }
}
```

Tài khoản FRM gửi riêng qua kênh nội bộ, không đưa lên Git.

## 5. Build và chạy

```powershell
dotnet build TTC.Weighing.sln
dotnet run --project src/TTC.Weighing.WinForms
```

Dữ liệu lệnh đốn / xe FRM không có trong `seed.sql`. Sau khi đăng nhập, dùng màn hình đồng bộ FRM/ERP
để kéo về.

## Cập nhật `db/schema.sql` khi schema thay đổi

Chạy trên máy dev có DB mới nhất:

```powershell
docker exec ttc-weighing-db pg_dump -U ttc_dev -d ttc_weighing --schema-only --no-owner --no-privileges -f /tmp/schema.sql
docker cp ttc-weighing-db:/tmp/schema.sql db/schema.sql
```
