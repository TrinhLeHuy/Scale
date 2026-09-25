# TTC — Hệ thống cân hợp nhất mía và vật tư

Triển khai theo bộ khung giao DEV `TTC_Unified_Weighing_DevPack_v0_1`. Một lõi chung cho chuyến, QR,
phiên cân, bằng chứng, checkpoint, đối soát, tích hợp và quyền. Mía và vật tư là **profile cấu hình**,
không phải hai nhánh code (R-001).

## Cấu trúc

| Project | Trách nhiệm |
|---|---|
| `TTC.Weighing.Domain` | Thực thể, enum trạng thái, rule engine. Không phụ thuộc EF/UI |
| `TTC.Weighing.Application` | Use case, abstraction repository/gateway, read model. Quyết định nghiệp vụ ở đây |
| `TTC.Weighing.Infrastructure` | EF Core + PostgreSQL, repository, query, migration |
| `TTC.Weighing.Integration` | Adapter FRM/ERP. Mặc định trả `HELD` vì hợp đồng chưa xác minh |
| `TTC.Weighing.WinForms` | UI vận hành. Form là View, logic ở Presenter/Application |

## Chạy trên PostgreSQL thật

```powershell
# 1. Khởi động database
docker compose up -d

# 2. Áp migration
$env:TTC_WEIGHING_ConnectionStrings__WeighingDb =
  "Host=localhost;Port=5432;Database=ttc_weighing;Username=ttc_dev;Password=ttc_dev_local_only"
dotnet ef database update --project src/TTC.Weighing.Infrastructure

# 3. Nạp danh mục nền và chạy kịch bản nhập mía đầu cuối
dotnet run --project tools/TTC.Weighing.Scenario

# 4. Mở giao diện trên chính dữ liệu đó
dotnet run --project src/TTC.Weighing.WinForms
```

`appsettings.Development.json` điều khiển chế độ chạy:

| Cờ | Ý nghĩa |
|---|---|
| `Preview:Enabled = true` | Dữ liệu trong bộ nhớ, không cần database |
| `Preview:Enabled = false` | Dùng PostgreSQL thật qua `ConnectionStrings:WeighingDb` và `Site:Code` |
| `Devices:UseSimulated = true` | Đầu cân, AI vị trí và barie giả lập. Bắt buộc bật khi chưa có adapter thật |

## Kịch bản kiểm chứng đầu cuối

`tools/TTC.Weighing.Scenario` chạy một chuyến nhập mía hoàn chỉnh trên database thật, 20 bước, mỗi
bước có khẳng định gắn với mã yêu cầu trong specs. Nó kiểm chứng cả các trường hợp phải bị chặn:

- QR tạm không đủ điều kiện cân chính thức (R-004)
- Chưa xác thực danh tính xe thì không mở barie và không phát lệnh nào (R-039)
- Một bàn cân chỉ có một phiên đang sở hữu (B-003)
- Chưa đủ cửa sổ ổn định thì không chốt cân (B-001.6)
- Mía chưa đo tạp chất thì chặn cân lần 2 (S02 tr.21)
- Chữ đường đang chờ chỉ cảnh báo, không chặn cân (R-014)
- Chưa đạt đối soát thì không cho ra cổng (R-006)
- Hợp đồng API chưa xác minh thì thông điệp giữ HELD, chuyến không chuyển POSTED (D-02)
- Gửi lại cùng business key không tạo bản trùng (R-024)

Sau đó nó đọc lại dữ liệu từ database để xác nhận: hai lượt cân đúng giá trị, mỗi lượt có bằng chứng,
audit ghi cả lần bị chặn và lần bị từ chối, chữ đường đang chờ không lưu số 0, bốn phép đối soát được
lưu độc lập và không gắn mã chính sách dung sai vì D-06 chưa duyệt.

## Kiểm tra không cần mở cửa sổ

```powershell
# Cấu hình DI của toàn bộ 12 form
src/TTC.Weighing.WinForms/bin/Debug/net8.0-windows/TTC.Weighing.WinForms.exe --smoke

# Bố cục: phát hiện control bị cắt, chồng nhau hoặc kích thước 0, ở ba kích thước màn hình
src/TTC.Weighing.WinForms/bin/Debug/net8.0-windows/TTC.Weighing.WinForms.exe --layout
```

## Rule engine

| Engine | Nội dung | Nguồn |
|---|---|---|
| `WeighCommitRules` | 8 nhóm điều kiện chốt cân, server quyết định | B-001, R-007 |
| `NetWeightCalculator` | Net theo hướng nghiệp vụ, giữ nguyên net âm/bằng 0 làm ngoại lệ | 02_Quy_tac 4.1 |
| `ImpurityCalculator` | Tỷ lệ tạp chất, ngưỡng mẫu nhỏ là cảnh báo | S02 tr.18, R-013 |
| `MeasurementAggregationRules` | DELTA/CUMULATIVE, đếm bao, phát hiện thiếu sequence | B-011, B-012, B-013 |
| `ReconciliationRules` | 4 phép so sánh độc lập, không trộn nguồn | R-020 |
| `AllocationRules` | Phân bổ bảo toàn tổng, phần dư minh bạch | R-016, R-021 |
| `GateDecisionRules` | Điều kiện mở barie tự động | R-006, R-039 |

## Màn hình

| Màn hình | Nội dung |
|---|---|
| UI-01 Tổng quan | Xe chờ, trong nhà máy, cân 1 chưa cân 2, PAUSED, thiết bị. Double-click mở UI-06 |
| UI-02 Đăng tài | Chọn hồ sơ, phát QR chuyến hoặc QR tạm, tra cứu QR |
| UI-03 Cân hợp nhất | Số cân lớn, trạng thái tín hiệu, tiến trình ổn định, checklist 8 điều kiện |
| UI-04 Phiên kho và đối soát | Mở/đóng phiên đo, kg IoT, đếm bao, chạy đối soát |
| UI-05 Mía và chất lượng | Cân tạp chất, tiếp nhận chữ đường, lịch sử mẫu |
| UI-06 Chi tiết chuyến | 8 tab: checkpoint, các lần cân, chất lượng, phiên đo, đối soát, phân bổ, tích hợp, audit |
| UI-07 Hàng chờ ngoại lệ | Cân lại, nhập tay có duyệt, tạm ngưng, cho tiếp tục |
| UI-08 Giám sát tích hợp | Thông điệp và trạng thái, giải quyết UNKNOWN bằng đối soát |
| UI-09 Thiết bị và cấu hình | Hồ sơ, bàn cân, ánh xạ thiết bị, ngưỡng đã duyệt hay chưa |
| UI-10 Báo cáo và phiếu | Lọc, xuất CSV, xem trước phiếu cân |
| UI-11 Cổng và barie | Đánh giá quyền qua cổng, phát lệnh có thời hạn, xác nhận xe đã qua |

## Cơ sở dữ liệu

Migration `InitialWeighingSchema` tạo 26 bảng, sequence `trip_no_seq` và các ràng buộc nghiệp vụ ở
tầng DB:

- `uq_weigh_sessions_bridge_open` — một bàn cân chỉ có một phiên đang sở hữu (B-003)
- `uq_weigh_events_trip_role_effective` — một bản cân hiệu lực cho mỗi vai trò
- `uq_quality_samples_trip_type_effective` — một mẫu hiệu lực cho mỗi loại
- `uq_integration_messages_business_key` — chống gửi trùng (R-024)
- `uq_device_events_dedup` — device + boot session + sequence (B-013)

Mọi cột kg dùng `numeric(15,3)`, không `float`.

## Chưa triển khai — chờ quyết định

Các mục sau **không được tự điền giá trị** để bật vận hành:

| Điểm | Nội dung chờ | Hành vi hiện tại |
|---|---|---|
| D-02 | Hợp đồng API FRM/ERP đã xác minh | Adapter và payload builder trả `HELD` |
| D-03 | Cách xác thực danh tính xe cho auto-open | `IVehicleIdentityVerifier` chưa có bản vận hành |
| D-04 | Biên dao động, độ mới tín hiệu cân | Ngưỡng null làm `WeighCommitRules` chặn chốt cân |
| D-05 | Ngưỡng AI vị trí và đếm bao, hiện trạng mô hình | Ngưỡng null làm chặn chốt cân |
| D-06 | Bảng dung sai đối soát từng loại hàng | `ReconciliationRules` trả `Pending` |
| D-08 | Vai trò, phân quyền, thời hạn lưu trữ | `ICurrentUser` chưa có bản vận hành |
| D-11 | Mẫu phiếu và số bản in | Phiếu đánh dấu chưa duyệt |

Chế độ vận hành còn thiếu adapter thiết bị thật (đầu cân, AI vị trí, IoT, barie), nguồn xác thực
người dùng và mapping hợp đồng tích hợp. Danh sách đầy đủ ghi trong `Program.AddProductionServices`.

## Kiểm thử

91 unit test cho rule engine, chạy bằng `dotnet test`. Đây là test logic nghiệp vụ, **không** thay
thế kiểm thử đầu cân, camera, IoT, FRM, ERP, barie hay nghiệm thu hiện trường.

Chưa có test cho lớp Application handler và chưa có integration test với PostgreSQL thật.

## Nguyên tắc không được vi phạm

Xem `.kiro/steering/dev-guidelines.md` mục "Quy tắc nghiệp vụ quan trọng". Tóm tắt: không `Math.Abs`
với net âm; không cộng `vehicle_net_kg` với `iot_mass_kg`; không dùng `double` cho kg; không ghi đè
`WeighEvent`; không retry khi chưa xác minh idempotency; QR tạm không cân chính thức; không dùng lớp
`Preview*` trong đường đi vận hành.
