# Development Guidelines — TTC Unified Weighing System

## Stack & Công nghệ

- **Backend:** C# .NET (chọn .NET 8+ LTS)
- **Frontend Desktop:** WinForms (.NET)
- **Database:** PostgreSQL (local dev dùng SQL Server trong Docker; production là PostgreSQL)
- **ORM:** Entity Framework Core với migration có phương án quay lui
- **Version control:** Git — mọi commit phải có mô tả rõ ràng, không commit secrets/token

---

## Đa ngôn ngữ (i18n) — BẮT BUỘC

> **Quy tắc số 0:** Mọi tính năng, mọi văn bản hiển thị cho người dùng phải hỗ trợ **4 ngôn ngữ** ngay từ đầu. Không được hardcode chuỗi tiếng Việt (hay bất kỳ ngôn ngữ nào) trực tiếp trong UI/thông báo.

### 4 ngôn ngữ chuẩn của dự án

| Ngôn ngữ | Culture code | Ghi chú |
|---|---|---|
| Tiếng Việt (Vietnamese) | `vi` | Ngôn ngữ mặc định (fallback) |
| Tiếng Anh (English) | `en` | |
| Tiếng Lào (Lao) | `lo` | |
| Tiếng Khmer (Khmer) | `km` | |

### Ngôn ngữ hiển thị theo BU (Business Unit) — quy tắc chọn ngôn ngữ

- Ngôn ngữ hiển thị **do BU của tài khoản đăng nhập quyết định**, không phải người dùng tự do chọn tất cả ngôn ngữ.
- Mỗi BU gắn với một nhà máy/quốc gia và có tập ngôn ngữ tương ứng. Ví dụ:
  - BU thuộc nhà máy Campuchia → `vi` + `km`
  - BU thuộc nhà máy Lào → `vi` + `lo`
  - BU thuộc nhà máy Việt Nam → `vi` + `en`
- **Nguồn cấu hình BU → ngôn ngữ đến từ API web portal** (sếp sẽ cung cấp sau).
  - Khi đăng nhập, gọi API portal để lấy BU của tài khoản và danh sách ngôn ngữ cho phép của BU đó.
  - **Chưa có API:** đặt sau interface (ví dụ `IBuLanguageProvider`) + cấu hình tạm trong appsettings để dev/test; **không hardcode** mapping vào logic UI. Khi có API thật chỉ cần thay implementation.
  - Ngôn ngữ mặc định luôn là `vi` (fallback) nếu API chưa trả về hoặc BU không xác định.
- Người dùng chỉ được đổi ngôn ngữ trong phạm vi tập ngôn ngữ mà BU cho phép; lưu lựa chọn theo user/máy.

### Nguyên tắc bắt buộc

- Dù chỉ một số BU dùng `lo`/`km`, code và resource vẫn **hỗ trợ đầy đủ cả 4 ngôn ngữ** (`vi`, `en`, `lo`, `km`) — việc bật/tắt ngôn ngữ nào là do cấu hình BU, không phải do thiếu bản dịch.
- **Không hardcode** chuỗi UI trong code. Dùng resource (`.resx`) hoặc `IStringLocalizer`/service dịch — key `PascalCase.DotNotation`, ví dụ `WeighOperation.CommitButton`.
- Nếu chưa có bản dịch chính thức cho `lo`/`km`, tạm để **placeholder có đánh dấu** (ví dụ prefix `[TODO-lo]`) và ghi chú TODO — **không** để trống hoặc fallback im lặng sang tiếng Việt.
- File resource đặt theo culture: `Strings.resx` (vi mặc định), `Strings.en.resx`, `Strings.lo.resx`, `Strings.km.resx`.
- Số/tiền tệ/ngày giờ format theo `CultureInfo` tương ứng, nhưng **số cân (kg) giữ định dạng thống nhất** `N3` để tránh nhầm lẫn nghiệp vụ.
- Font hiển thị phải hỗ trợ ký tự Lào và Khmer (chọn font Unicode đầy đủ như Noto Sans Lao / Noto Sans Khmer) — kiểm tra không bị "tofu" (□).
- Khi tạo docs/spec/tài liệu hướng dẫn mới cho người dùng cuối, cân nhắc cung cấp ngôn ngữ theo BU đích nếu tài liệu hướng đến vận hành thực địa.

---

## Kiến trúc dự án

### Cấu trúc Solution

```
TTC.Weighing.sln
├── src/
│   ├── TTC.Weighing.Domain/          # Thực thể, enum, business rules, không phụ thuộc EF/UI
│   ├── TTC.Weighing.Application/     # Use cases, commands/queries, interfaces
│   ├── TTC.Weighing.Infrastructure/  # EF DbContext, repositories, adapters FRM/ERP/Device
│   ├── TTC.Weighing.WinForms/        # UI project (Forms, Controls, ViewModels)
│   └── TTC.Weighing.Integration/     # Adapters giao tiếp FRM, ERP, thiết bị cân/barie/IoT
├── tests/
│   ├── TTC.Weighing.Domain.Tests/
│   ├── TTC.Weighing.Application.Tests/
│   └── TTC.Weighing.Integration.Tests/
└── docs/                             # Specs DevPack
```

### Nguyên tắc phân lớp

- **Domain** không biết EF, WinForms, hay bất kỳ framework ngoài — chỉ business logic thuần
- **Application** chỉ phụ thuộc Domain và interfaces — không gọi trực tiếp DB hay device
- **Infrastructure** implements interfaces của Application — mọi I/O ở đây
- **WinForms** chỉ gọi Application layer, không gọi thẳng Domain hay Infrastructure
- Mọi quyết định nghiệp vụ (chốt cân, đủ điều kiện, trạng thái) phải ở **server-side (Application/Domain)**, UI chỉ hiển thị kết quả và nhận input

---

## Quy tắc đặt tên

### Database (PostgreSQL)

- Tên bảng: `snake_case`, số nhiều — `trips`, `weigh_events`, `device_events`
- Tên cột: `snake_case` — `trip_id`, `normalized_kg`, `occurred_at`
- Khóa ngoại: `{tên_bảng_tham_chiếu_số_ít}_id` — `trip_id`, `bridge_id`
- Index: `idx_{tên_bảng}_{cột}` — `idx_trips_vehicle_id`
- Constraint: `uq_{bảng}_{cột}`, `chk_{bảng}_{điều_kiện}`

### Code C#

- Class, Interface, Enum, Property: `PascalCase`
- Method: `PascalCase` — `EvaluateAndCommitWeight()`, `OpenWeighSession()`
- Biến cục bộ, tham số: `camelCase`
- Interface prefix: `I` — `IWeighSessionRepository`, `IFrmAdapter`
- Async method suffix: `Async` — `RegisterTripAsync()`, `ResolveQrAsync()`
- Constants: `UPPER_SNAKE_CASE` — `STABILITY_WINDOW_MS`
- WinForms controls: prefix theo loại — `btnConfirm`, `lblWeight`, `dgvTrips`, `pnlStatus`, `txtPlate`

### Naming quan trọng của domain

- Dùng đúng tên trong specs: `trip_id`, `WeighEvent`, `QrToken`, `MeasurementSession`, `ReconciliationResult`
- Không đặt `WeightIn`/`WeightOut` làm tên cột DB — dùng `first_weight_kg`/`second_weight_kg` và `weight_role` (GROSS/TARE)
- `vehicle_net_kg` = chênh lệch cân xe, **không** phải khối lượng sản phẩm tinh

---

## Quy tắc viết SQL / EF Core

- Dùng **parameterized queries** hoặc EF LINQ — tuyệt đối không string concat SQL
- Migration phải có `Up()` và `Down()` — luôn có phương án quay lui
- Không dùng `AnyAsync()` kết hợp `FirstOrDefaultAsync()` riêng lẻ — dùng một query
- Soft delete với `deleted_at` timestamp — không `DELETE` cứng chứng từ nghiệp vụ
- Mọi bảng nghiệp vụ có `created_at`, `updated_at`, `version` (optimistic concurrency)
- `WeighEvent` bất biến sau khi commit — cân lại tạo bản mới liên kết `supersedes_id`
- Dùng kiểu `numeric(15,3)` cho kg, không dùng `float`/`double` tránh sai số làm tròn

---

## Quy tắc WinForms

### Form và Control

- Mỗi Form có một nhiệm vụ duy nhất (Single Responsibility) — không nhét tất cả vào MainForm
- Form lớn tách thành `UserControl` — ví dụ: `WeighDisplayControl`, `TripInfoControl`, `CameraStatusControl`
- Không xử lý business logic trong event handler Form — delegate sang service/presenter
- Dùng pattern **MVP (Model-View-Presenter)** hoặc **Service Layer** — Form là View thuần
- Long-running operations (gọi API, query DB) dùng `async/await` với `BackgroundWorker` hoặc `Task` — không block UI thread

### Hiển thị số cân (UI-03)

- Số cân hiển thị nổi bật (font lớn ≥ 36pt, mono-spaced) — ví dụ `30,050.000 kg`
- Hiển thị rõ trạng thái tín hiệu: `🟢 ỔN ĐỊNH`, `🟡 ĐANG ĐO`, `🔴 MẤT TÍN HIỆU / CŨ`
- Không giữ số cũ như đang trực tuyến khi mất kết nối — hiển thị `---` và thời gian mất tín hiệu
- Thời gian ổn định hiển thị dạng progress/đếm ngược: `Ổn định: 3.2s / 5.0s`
- Từng điều kiện chốt cân hiển thị riêng (checklist): QR ✓, Vị trí AI ✓, Cân ổn định ✓/✗...

### Trạng thái và phản hồi

- Mọi thao tác có feedback rõ: loading indicator khi chờ, thông báo lỗi chi tiết, xác nhận thành công
- Không ẩn nút để bảo vệ nghiệp vụ — ẩn/hiện nút dựa trên quyền, nhưng **server vẫn phải kiểm tra quyền**
- Nút đặc biệt (Nhập tay, Cân lại, Chuyển bàn) chỉ hiện với đúng vai trò — binding với `CanExecute`
- Sau khi gọi API, refresh dữ liệu từ server — không tự sửa local state để "cho nhanh"

### Thread Safety

```csharp
// Đúng: cập nhật UI từ non-UI thread
private void UpdateWeightDisplay(decimal kg)
{
    if (lblWeight.InvokeRequired)
        lblWeight.Invoke(() => lblWeight.Text = $"{kg:N3} kg");
    else
        lblWeight.Text = $"{kg:N3} kg";
}
```

---

## Cấu trúc Project (chi tiết WinForms)

### Tổ chức thư mục trong TTC.Weighing.WinForms

```
Forms/
  MainForm.cs                  # Shell/navigation chính
  WeighOperationForm.cs        # UI-03 — Cân hợp nhất
  TripRegistrationForm.cs      # UI-02 — Đăng tài
  TripDetailForm.cs            # UI-06 — Chi tiết chuyến
  ExceptionQueueForm.cs        # UI-07 — Hàng chờ ngoại lệ
  ReportForm.cs                # UI-10 — Báo cáo
Controls/
  WeighDisplayControl.cs       # Hiển thị số cân + trạng thái
  TripInfoControl.cs           # Thông tin chuyến
  CameraStatusControl.cs       # AI vị trí VALID/INVALID/UNKNOWN
  ChecklistControl.cs          # Điều kiện chốt cân
  IntegrationStatusControl.cs  # Trạng thái FRM/ERP/thiết bị
Presenters/
  WeighOperationPresenter.cs
  TripRegistrationPresenter.cs
Services/
  WeighDisplayService.cs       # Poll/receive tín hiệu cân
  DeviceStatusService.cs       # Monitor trạng thái thiết bị
Helpers/
  PrintHelper.cs               # In phiếu cân
  ExportHelper.cs              # Xuất báo cáo
```

---

## Error Handling

- Phân biệt rõ loại lỗi: lỗi thiết bị cục bộ vs lỗi dịch vụ dùng chung (xem 02_Quy_tac.md mục 7)
- Lỗi nghiệp vụ (QR sai, chuyến đã đóng): hiển thị lý do cụ thể, **không chốt cân / không mở barie**
- Lỗi kết nối FRM/ERP: tạm ngưng quy trình phụ thuộc, **không tự chuyển sang profile không ERP**
- Timeout sau gửi: trạng thái `UNKNOWN` — tra cứu/đối soát trước khi gửi lại, **không tạo giao dịch mới**
- Mọi exception phải log với `correlation_id`, `trip_id`, actor, timestamp
- Không log password, token, secret — che thông tin nhạy cảm trong log
- WinForms: wrap async calls với try/catch, hiển thị lỗi user-friendly — không crash form

```csharp
// Pattern xử lý lỗi trong Form
private async void btnCommitWeight_Click(object sender, EventArgs e)
{
    try
    {
        SetUiBusy(true);
        var result = await _weighPresenter.EvaluateAndCommitWeightAsync(_sessionId);
        if (!result.IsSuccess)
        {
            ShowWeighBlockedMessage(result.BlockReason); // Hiện lý do cụ thể
            return;
        }
        RefreshFromServer();
    }
    catch (IntegrationException ex)
    {
        _logger.LogError(ex, "Weight commit failed. TripId={TripId}", _currentTripId);
        // KHÔNG dùng MessageBox — dùng khuôn thông báo chung (xem mục "Thông báo / Popup")
        DigiScalePopup.ShowError(this, Loc.T("weigh.commit_failed_title"),
            DigiScaleErrors.Describe(ex),
            [(Loc.T("alert.fact_trip"), _txtRegNo.Text), (Loc.T("alert.fact_plate"), _txtPlate.Text)]);
    }
    finally
    {
        SetUiBusy(false);
    }
}
```

---

## Thông báo / Popup — BẮT BUỘC dùng template chung

Mọi hộp thông báo cho người dùng phải cùng một format: **dải tiêu đề màu + biểu tượng → Thông tin → Lý do → Cách xử lý → nút lớn**. Format này do `Helpers/DigiScalePopup.cs` vẽ.

### Không được dùng

- ❌ `MessageBox.Show(...)` của Windows. Ngoại lệ duy nhất là `Program.cs` (lỗi khởi tạo app, lỗi làm sập app), vì lúc đó giao diện có thể chưa dựng xong.
- ❌ Tự tạo `Form` thông báo riêng, hoặc tự vẽ hộp lỗi / xác nhận.
- ❌ Hiện JSON thô, HTTP status, stack trace hay mã kỹ thuật (`INTEGRATION.RULE.DT_06...`) cho người vận hành. Phải dịch qua `DigiScaleErrors.Describe(...)`. Nội dung gốc đã có trong Nhật ký API.
- ❌ Emoji trong tiêu đề hoặc nội dung popup (GDI vẽ thành ô vuông). `DigiScalePopup` có tự lọc, nhưng đừng cố tình đưa vào.

### Chọn hàm theo tình huống

| Tình huống | Hàm | Màu |
|---|---|---|
| Thao tác có đẩy DigiScale (thành công / lỗi) | `DigiScalePopup.Show(owner, title, details, digiOk, digiStatus)` | Xanh lá / đỏ |
| Lỗi đã dịch (bị gì + cần làm gì) | `DigiScalePopup.ShowError(owner, title, DigiScaleErrors.Describe(x), facts)` | Đỏ |
| Chặn nghiệp vụ (không đăng tài / không lưu được...) | `DigiScalePopup.ShowWarning(owner, title, reason)` | Đỏ |
| Thông báo đầy đủ tự chọn mức độ | `DigiScalePopup.ShowAlert(owner, AlertKind.X, title, reason, action, facts, message)` | Theo `AlertKind` |
| Hỏi Có / Không | `DigiScalePopup.ShowConfirm(owner, title, yesText, noText, facts, message, highlights, defaultNo)` | Xanh teal |
| Thông báo ngắn kiểu MessageBox (code cũ, việc nhỏ) | `Helpers.AppMessageBox.Show(...)` — cùng chữ ký và kết quả trả về như `MessageBox.Show` | Theo icon |

`AlertKind`: `Success` (xanh lá ✓), `Info` (xanh dương i), `Warning` (cam !), `Error` (đỏ ✕), `Question` (teal ?).

`AppMessageBox` ánh xạ icon như sau: `Error` → đỏ, nội dung vào "Lý do"; `Warning` → cam, vào "Lý do"; `Information`/không icon → xanh dương; `YesNo` → hộp xác nhận (`MessageBoxDefaultButton.Button2` = mặc định Không). Code mới nên gọi thẳng `DigiScalePopup` để có đủ Thông tin / Lý do / Cách xử lý.

### Quy tắc nội dung

- **Tiêu đề**: ngắn, nói kết quả, ví dụ "XE ĐÃ ĐĂNG TÀI", "GHI CCS THÀNH CÔNG", "Chuyến đã cân". Không để trống.
- **Thông tin (`facts`)**: định danh để người vận hành đối chiếu, gồm Số đăng tài, Biển số, Lệnh đốn, Trọng lượng... Dùng nhãn có sẵn `alert.fact_*`.
- **Lý do (`reason`)**: bị gì, chỉ đúng đối tượng lỗi, ví dụ "Thẻ xe T260000121 chưa duyệt", không viết "Lỗi không xác định".
- **Cách xử lý (`action`)**: người dùng cần làm gì tiếp, ví dụ "Hủy phiếu cũ trên DigiScale Portal rồi đăng tài lại". Không viết chung chung "Xử lý theo lý do trên rồi thử lại" khi đã biết cách xử lý cụ thể.
- **Đa ngôn ngữ**: mọi chuỗi (tiêu đề, lý do, cách xử lý, nhãn nút) lấy qua `Loc.T(...)`, có đủ `vi`, `en`, `lo`, `km`. Không viết thẳng tiếng Việt trong code.
- **Xác nhận thao tác rủi ro** (ghi đè, bỏ qua cảnh báo, dùng số không mới nhất, xóa/hủy): `defaultNo: true`, để Enter = Không.
- **Lỗi DigiScale mới** (mã `DT-xx`, `CR-xx`...): thêm bản dịch Lý do + Cách xử lý vào `DigiScaleErrors`, không xử lý chuỗi lỗi rải rác trong form.
- Popup chỉ dùng cho việc cần người dùng chú ý / quyết định. Trạng thái tiến trình ("Đang đẩy...", "Đã đọc máy đo") ghi lên dải trạng thái / khung thông báo của màn hình, không bật popup.

### Checklist khi thêm thông báo

- [ ] Không có `MessageBox.Show` mới (trừ `Program.cs`)
- [ ] Dùng đúng hàm `DigiScalePopup` theo bảng trên
- [ ] Có tiêu đề, Lý do cụ thể, Cách xử lý (với lỗi / chặn)
- [ ] Có `facts` định danh chuyến khi thông báo gắn với một chuyến
- [ ] Mọi chuỗi qua `Loc.T`, đủ 4 ngôn ngữ
- [ ] Không lộ JSON / mã kỹ thuật / stack trace

---

## Quy tắc nghiệp vụ quan trọng (không được vi phạm)

Các quy tắc này trích từ specs — vi phạm sẽ dẫn đến sai số nghiệp vụ:

1. **Chốt cân**: Server quyết định — UI KHÔNG tự tính toán điều kiện. Gọi `EvaluateAndCommitWeight` và nhận kết quả.
2. **Trọng lượng âm**: Không dùng `Math.Abs()` che net âm — đây là ngoại lệ cần xử lý riêng.
3. **Số cân**: Lưu `numeric`, không `double`. Lưu số gốc + đơn vị gốc + kg quy đổi.
4. **Cân lại**: Tạo `WeighEvent` mới liên kết `supersedes_id` — không UPDATE bản cũ.
5. **IoT + Cân xe**: KHÔNG cộng `vehicle_net_kg + iot_mass_kg` — có thể đo cùng một lô.
6. **QR tạm**: Không đủ điều kiện cân chính thức — phải kiểm tra loại QR trước mọi thao tác.
7. **Bàn cân**: Một bàn chỉ có một phiên đang sở hữu (`WeighSession` lock) — kiểm tra trước khi tạo phiên mới.
8. **Mía cân lần 2**: Chặn nếu chưa đo tạp chất — KHÔNG chặn vì chưa có chữ đường.
9. **Mở barie**: Chỉ sau khi mọi điều kiện đủ VÀ có `command_id` — không tự xác nhận xe đã qua từ ACK.
10. **Retry**: Chỉ retry khi API đã chứng minh idempotency — trạng thái `UNKNOWN` phải tra cứu trước.

---

## Quy tắc Git

- Branch: `feature/R-xxx-mo-ta-ngan`, `fix/mo-ta-loi`, `release/vx.x`
- Không commit trực tiếp lên `main`/`master` — dùng PR
- Commit message: `[R-007] Add auto-weight-commit condition evaluation`
- Không commit: `.env`, `appsettings.*.json` có secret, `*.user`, `bin/`, `obj/`
- Tag release: `vMAJOR.MINOR.PATCH`

---

## Checklist trước khi commit

- [ ] Không có secret/password trong code hoặc config
- [ ] Migration có cả `Up()` và `Down()`
- [ ] Không dùng `Math.Abs()` với trọng lượng
- [ ] Mọi thay đổi trạng thái nghiệp vụ có audit log
- [ ] Không gọi DB/API trực tiếp trong event handler WinForms
- [ ] Số kg dùng kiểu `decimal`, không `double`
- [ ] Async UI operations dùng `InvokeRequired` nếu cần
- [ ] Lỗi/exception được log với đủ context (trip_id, actor, correlation_id)
- [ ] Quyền kiểm tra ở server, không chỉ ẩn UI
- [ ] Mọi chuỗi hiển thị dùng resource, có đủ 4 ngôn ngữ (vi, en, lo, km) — không hardcode
- [ ] Thông báo dùng template `DigiScalePopup` / `AppMessageBox` — không `MessageBox.Show` (trừ `Program.cs`)

---

## Màn hình và R-ID tương ứng

| Form/Control | R-ID liên quan | Ghi chú |
|---|---|---|
| TripRegistrationForm (UI-02) | R-003, R-004, R-005 | Đăng tài, QR, phân loại xe |
| WeighOperationForm (UI-03) | R-007, R-008, R-010, R-011 | Cân hợp nhất, 2 bàn |
| MeasurementSessionForm (UI-04) | R-017, R-018, R-019, R-020 | IoT, AI đếm bao, đối soát |
| QualityForm (UI-05) | R-013, R-014, R-016 | Tạp chất, chữ đường, chủ mía |
| TripDetailForm (UI-06) | R-003, R-009, R-026, R-027 | Lịch sử cân, audit, in phiếu |
| ExceptionQueueForm (UI-07) | R-009, R-010, R-025 | Cân lại, nhập tay, ngoại lệ |
| IntegrationMonitorForm (UI-08) | R-022, R-023, R-024, R-037 | FRM/ERP, UNKNOWN/FAILED |
| ConfigForm (UI-09) | R-001, R-002, R-011 | Cấu hình thiết bị, profile |
| DashboardForm (UI-01) | R-028 | Tổng quan, báo cáo |

---

## Lưu ý đặc thù dự án Scale (Cân mía/vật tư)

- **Một lõi, nhiều profile**: không tạo hai nhánh code riêng cho mía và vật tư — dùng `WorkflowProfile` config
- **D-chưa duyệt**: các điểm D (D-01 đến D-12) trong specs chưa được chốt — **không tự điền giá trị hardcode** vào production
- **Số cân vs số phân bổ**: phiếu phải phân biệt `vehicle_net_kg` (cân xe) với `allocated_kg` (phân bổ theo chủ mía/Shipment)
- **Chữ đường (CCS)**: có thể đến **sau** khi xe đã ra — thiết kế DB và UI phải hỗ trợ cập nhật hậu kỳ mà không mở lại QR/barie
- **Môi trường test**: dữ liệu test không được gửi FRM/ERP thật, không mở barie thật — cờ `IsTestMode` bắt buộc
- **Cutover**: khi go-live, một thời điểm chỉ có **một hệ thống** ghi chứng từ chính thức


---

## Chế độ cân Offline và đồng bộ ngược (Offline Weighing & Upstream Sync)

### Nguyên tắc hoạt động

Hệ thống cân hoạt động **offline-first**: mọi dữ liệu (đăng tài, lượt cân, tạp chất, đối soát...) luôn **lưu DB cục bộ trước**. Việc đẩy ngược lên hệ thống chính (FRM/ERP/DigiScale) diễn ra **sau** theo khung giờ hoặc khi có kết nối.

### Flow hiện tại

1. **Đăng tài (UI-02)**: Bấm "Kiểm tra" biển số → gọi `IFrmPlanLookup` (hiện là stub trả rỗng)
   - Nếu **tìm thấy kế hoạch**: điền thông tin từ FRM/ERP → đăng tài chính thức
   - Nếu **không tìm thấy** (hoặc mất kết nối): hỏi user có muốn phát QR tạm → Yes → tự động đăng tài tạm + lưu DB
2. **Cân (UI-03)**: QR tạm vẫn cho phép cân bình thường (không chặn). Dữ liệu cân lưu DB đầy đủ.
3. **Toàn bộ flow**: QR tạm đi qua tất cả bước (cổng, cân, tạp chất, kho, đối soát) như QR chính thức.

### Đồng bộ ngược — TODO khi có API

> **Chờ sếp cung cấp API đẩy dữ liệu lên hệ thống chính.** Khi có, implement:

- **Interface**: `IUpstreamSyncService` (Application layer)
  ```csharp
  public interface IUpstreamSyncService
  {
      /// <summary>Đẩy chuyến cân offline lên hệ thống chính.</summary>
      Task<SyncResult> PushTripAsync(Guid tripId, CancellationToken ct);
      
      /// <summary>Đẩy batch theo khung giờ.</summary>
      Task<BatchSyncResult> PushPendingAsync(Guid siteId, CancellationToken ct);
  }
  ```
- **Adapter**: `DigiScaleUpstreamSyncAdapter` (Infrastructure layer) — gọi API thật
- **Scheduler**: Background service chạy theo khung giờ (cấu hình trong appsettings)
- **Trạng thái đồng bộ**: Thêm cột `sync_status` vào bảng `trips`:
  - `LOCAL` — chưa đẩy
  - `PENDING` — đang chờ đẩy
  - `SYNCED` — đã đẩy thành công
  - `FAILED` — đẩy lỗi, cần thử lại
- **UI**: Cột "Đồng bộ" trên lưới Dashboard/Report hiển thị trạng thái sync
- **Idempotency**: Mỗi lần đẩy phải có `idempotency_key` để tránh trùng khi retry

### Stub hiện tại cần thay thế khi có API

| Interface | File stub | Mô tả |
|---|---|---|
| `IFrmPlanLookup` | `Infrastructure/Repositories/FrmPlanLookupAdapter.cs` | Tra cứu kế hoạch biển số — stub trả rỗng |
| `IFrmAdapter` | `Integration/Adapters/UnverifiedContractFrmAdapter.cs` | Gửi kết quả FRM — stub trả HELD |
| `IErpAdapter` | `Integration/Adapters/UnverifiedContractErpAdapter.cs` | Gửi kết quả ERP — stub trả HELD |
| *(chưa có)* | *(tạo mới)* | `IUpstreamSyncService` — đẩy dữ liệu offline lên hệ thống chính |

### Cấu hình dự kiến trong appsettings

```json
{
  "UpstreamSync": {
    "Enabled": false,
    "BaseUrl": "https://digiscale-uat.agris.com.vn",
    "CronSchedule": "0 */2 * * *",
    "_comment": "Đẩy mỗi 2 giờ. Bật khi có API thật."
  }
}
```


---

## Đóng gói & cập nhật bản cài (Installer) — BẮT BUỘC

Bộ cài một file `.exe` (Inno Setup 6) nằm trong `installer\`. Người dùng nhà máy chỉ chọn thư mục cài rồi Next. App, PostgreSQL 16 và VC++ runtime đều đóng gói sẵn.

### Build

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 -Version 1.0.0
```

- Kết quả: `installer\out\TTC-Weighing-Setup-<Version>.exe`.
- **LUÔN build lại bộ cài sau mỗi thay đổi.** Trong lượt làm việc, nếu có sửa bất kỳ tệp nào dưới `src/`, `db/`, `installer/` hoặc `appsettings*.json`, thì trước khi báo xong phải:
  1. Chạy `build-installer.ps1 -Version 1.0.0` và kiểm tra build thành công (dòng `== Done`, không có lỗi).
  2. Báo lại đường dẫn file `.exe` mới cho người dùng.
  - Build lỗi thì sửa cho tới khi build được. Không được báo xong khi file `.exe` vẫn là bản cũ.
  - Chỉ sửa tài liệu (`docs/`, `.kiro/`, `*.md`) thì không cần build lại.
- **Giữ version `1.0.0`** cho tới khi người dùng yêu cầu nâng version. Không tự tăng.
- Sửa `installer\TTCWeighing.iss` thì giữ mã hóa **UTF-8 có BOM**, nếu không tiếng Việt trong bộ cài bị lỗi font.
- `installer\bundle-settings.json` chứa tài khoản FRM và URL API, được đóng vào bộ cài và ghi thành `appsettings.Local.json` trên máy trạm. **Không commit** (đã gitignore). Muốn đổi UAT → production thì sửa tệp này rồi build lại.

### Cài đè = cập nhật, KHÔNG được mất dữ liệu

Cài bản mới đè lên bản cũ phải chạy được mà không cần gỡ cài đặt. Mọi thay đổi ở bộ cài phải giữ các điều sau:

- `AppId` trong `.iss` **không được đổi**. Đổi là Windows coi như app khác và cài thành thư mục mới.
- **Không xóa hoặc ghi đè** các mục sau:
  - `<thư mục cài>\data\` (pgdata, `db-secrets.json`, backups, logs);
  - `appsettings.Local.json`;
  - `evidence\`, `outbox\`.
- **Không nạp lại** `schema.sql`/`seed.sql` khi cơ sở dữ liệu đã có bảng.
- Gỡ cài đặt chỉ xóa app và dịch vụ `TTCWeighingDB`. Thư mục `data` được giữ lại.
- Bản build đầu tiên để dữ liệu ở `C:\ProgramData\TTCWeighing`. `configure.ps1` phải tiếp tục nhận diện vị trí cũ này.

### Thay đổi schema cơ sở dữ liệu

Mọi thay đổi bảng hoặc cột đều phải làm đủ **cả hai** việc, nếu không máy đã cài sẽ lỗi khi cập nhật:

1. Thêm script `db/upgrades/YYYYMMDD_NN_mo_ta.sql`. Bộ cài chạy mỗi script đúng một lần theo thứ tự tên, ghi nhận vào bảng `installer_schema_upgrades`, và tự `pg_dump` sao lưu trước khi chạy.
2. Cập nhật `db/schema.sql` cho máy cài mới (xem `docs/SETUP_MAY_MOI.md`).

Quy tắc cho script nâng cấp (chi tiết ở `db/upgrades/README.md`):
- Không làm mất dữ liệu. Dùng `ADD COLUMN ... NULL`, `CREATE TABLE IF NOT EXISTS`... và ghi cách quay lui trong chú thích đầu tệp.
- Không sửa hoặc đổi tên script đã phát hành.
- Tên tệp chỉ gồm chữ, số và `_ . -`.

### Giao diện trên máy khác (màn hình nhỏ / Scale 125–150%)

- App chạy ở `ApplicationHighDpiMode=DpiUnawareGdiScaled` (csproj). Windows phóng cả cửa sổ nên bố cục pixel giữ đúng tỉ lệ. **Không đổi** sang SystemAware/PerMonitor nếu chưa chuyển toàn bộ form sang `AutoScaleMode.Dpi`.
- Popup và cửa sổ phụ tính kích thước theo `Screen.WorkingArea` (tối đa ~92% chiều cao), không đặt cứng chiều cao lớn.

#### Thu phóng toàn bộ giao diện theo màn hình (`Theme/UiScale.cs`) — BẮT BUỘC

Giao diện được **thiết kế ở 1920x1080**. Máy có màn hình nhỏ hơn thấy đúng bố cục đó, chỉ nhỏ hơn: chữ, logo, kích thước, khoảng cách, lưới, menu, tab đều co **cùng một tỉ lệ**.

- **Hệ số** = min(vùng làm việc / 1920x1080), trong khoảng 0.5..1, không bao giờ phóng to. Cấu hình `App:UiScale` = `auto` (mặc định) hoặc số cố định; `1` = tắt.
- **Cơ chế** (giống WinForms tự scale theo DPI):
  1. Form được dựng trong `UiScale.DesignScope()`. Trong scope, `UiScale.Current` = 1, nên font `AgrisTheme` và `UiScale.Px()` ra cỡ thiết kế, khớp với số px viết trong code.
  2. `UiScale.Apply(form)` co cả cây control một lần: dùng `Control.Scale` cho vị trí, kích thước, Padding/Margin, MinimumSize và cột TableLayoutPanel; thêm font riêng từng control, lưới (cột, dòng, tiêu đề), `TabControl.ItemSize` và chiều cao các ô theo font.
  3. Ngoài scope, `Current` = hệ số, nên chữ vẽ lúc chạy và control tạo thêm sau đó cũng ra đúng tỉ lệ.
- **Điểm vào đã nối sẵn, không làm khác:**
  - Splash, Login, MainForm: tạo bằng `UiScale.Create(() => new XxxForm())` trong `Program.cs`.
  - Tab nghiệp vụ: mở qua `MainForm.CreateAndHost` (`OpenTab` / `ReopenTabByType` / `OpenScreenForDocs` đều đi qua đây).
  - Form / màn hình mới phải đi qua một trong các đường này. Không `new` form rồi `Show` thẳng.
- **Quy tắc khi viết UI mới:**
  - Font chỉ lấy qua `AgrisTheme.Body/Heading/Mono/Display/Caption/Ui/Icon`. Không `new Font(...)` với cỡ cố định trong code chạy lúc vẽ / lúc chạy.
  - Số px dùng **lúc chạy** (OnPaint, Resize/Layout handler, đổi trạng thái, control tạo sau khi form đã hiện) phải qua `UiScale.Px(n)`. Hằng số px của control tự vẽ khai báo `static int X => Theme.UiScale.Px(n);`, không dùng `const int`.
  - Số px đặt trong constructor (Size, Height, Padding...) viết bình thường theo cỡ thiết kế: `Apply` tự co.
  - Hàng nút / ô cạnh nhau dùng `TableLayoutPanel` chia %, không dùng `FlowLayoutPanel` với nút rộng cố định.
  - Không đặt `MinimumSize` của `MainForm` lớn hơn 1024x600 (cỡ thiết kế).
- **Kiểm tra bố cục trước khi phát hành:**
  - Chạy `TTC.Weighing.WinForms.exe --layout` với `TTC_WEIGHING_App__UiScale` = `1`, `0.71` (1366x768), `0.65` (1280x720) và `0.5`. Vùng kiểm tra tự nhân theo hệ số.
  - Kết quả ở mọi hệ số phải giống ở hệ số `1` (không phát sinh FAIL mới).

### Logo / icon

- Icon app là `src/TTC.Weighing.WinForms/Assets/logo.ico`, dùng cho `.exe`, shortcut, taskbar, thanh tiêu đề và file Setup.
- Đổi `logo.png` thì chạy lại `installer\scripts\make-icon.ps1`.
- Form chính mới đặt `Icon = LogoHelper.GetAppIcon()`.
