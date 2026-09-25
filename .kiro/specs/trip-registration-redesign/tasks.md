# Implementation Plan: Trip Registration Redesign

## Overview

Thiết kế lại module đăng tài (UI-02) theo đúng quy trình thực tế nhà máy mía TTC. Triển khai theo thứ tự phụ thuộc: Domain → Application → Infrastructure → WinForms → Tests. Mỗi task là một đơn vị code có thể review độc lập, xây dựng tăng dần trên các task trước đó.

## Tasks

- [x] 1. Domain layer — Enum, entity mở rộng
  - [x] 1.1 Thêm VehicleType enum và mở rộng Trip entity
    - Thêm `VehicleType` enum (Container, Truck, Trailer) vào `Domain/Enums/DomainEnums.cs`
    - Thêm các property mới vào `Domain/Entities/Trip.cs`: `VehicleType?`, `QueueSequenceNumber?`, `AssignedWeighbridgeId?`, `AssignedWarehouseId?`, `GateId?`, `SugarcaneType?`, `SugarcaneVariety?`, `SugarcaneOwner?`, `HarvestMethod?`, `Station?`, `CropSeason?`
    - Tất cả trường mới nullable để tương thích ngược với Trip cũ
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6_

  - [x] 1.2 Thêm EncodedContent vào QrToken entity
    - Thêm property `EncodedContent` (string?) vào `Domain/Entities/QrToken.cs`
    - Đây là nội dung JSON Base64 mã hóa trong mã QR (token prefix + số thứ tự + bàn cân + kho cho QR_chuyến; chỉ token prefix cho QR_tạm)
    - _Requirements: 8.2, 11.6_

- [x] 2. Application layer — Interfaces và handlers mới
  - [x] 2.1 Tạo interface IFrmPlanLookup và ICameraService
    - Tạo `IFrmPlanLookup` trong `Application/Abstractions/` với `LookupAsync` và `NotifyCancellationAsync`
    - Tạo các record `PlanLookupResult`, `PlanEntry`, `IntegrationSendResult`
    - Tạo `ICameraService` trong `Application/Abstractions/` với `CaptureSnapshotAsync` và `GetStatusAsync`
    - Tạo record `CameraStatus`
    - _Requirements: 1.1, 1.2, 1.4, 10.2, 10.3_

  - [x] 2.2 Tạo interface IQueueSequenceGenerator
    - Tạo `IQueueSequenceGenerator` trong `Application/UseCases/Trips/` (cùng file ITripNumberGenerator hoặc file riêng)
    - Method `NextAsync(Guid siteId, VehicleType vehicleType, CancellationToken ct)` → `Task<int>`
    - Số thứ tự tách riêng per-site, per-vehicle-type, per-operating-date
    - _Requirements: 2.2, 2.3_

  - [x] 2.3 Tạo CheckPlanHandler
    - Tạo `CheckPlanHandler` trong `Application/UseCases/Trips/CheckPlanHandler.cs`
    - Định nghĩa `CheckPlanCommand` (SiteId, Plate, CorrelationId) và `CheckPlanResult`
    - Kiểm tra quyền `trip.register`, gọi `IFrmPlanLookup.LookupAsync`
    - Trả lỗi kết nối cụ thể khi FRM/ERP không phản hồi (Req 1.4)
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_

  - [x] 2.4 Tạo QueueTripHandler
    - Tạo `QueueTripHandler` trong `Application/UseCases/Trips/QueueTripHandler.cs`
    - Định nghĩa `QueueTripCommand` và `QueueTripResult`
    - Logic phân bổ bàn cân: chọn bàn cân có ít chuyến chờ nhất (load balancing)
    - Nếu không có bàn cân khả dụng → vẫn thành công, `AssignedWeighbridgeId = null`
    - Gán kho đích từ kế hoạch nguồn hoặc cấu hình mặc định
    - _Requirements: 9.1, 9.2, 9.3, 9.5_

  - [x] 2.5 Redesign RegisterTripHandler
    - Mở rộng `RegisterTripCommand` thêm: GateId, PlanSource, CommodityType, OwnerOrCustomer, WarehouseCode, VehicleType, SugarcaneType/Variety/Owner, HarvestMethod, Station, CropSeason, IssueTemporaryQrOnly
    - Mở rộng `RegisterTripResult` thêm: QueueSequenceNumber, AssignedWeighbridgeId, AssignedWarehouseId
    - Validate VehicleType bắt buộc cho mía nhập (profile Inbound sugarcane)
    - Luồng Registered: gọi `IQueueSequenceGenerator.NextAsync` → gọi `QueueTripHandler` → mã hóa QR_chuyến
    - Luồng TempRegistered: tạo QR_tạm chỉ chứa token prefix
    - Mở rộng `QrTokenFactory` thêm method `Encode`/`Decode` cho EncodedContent (JSON Base64)
    - Chụp ảnh camera vào (không chặn nếu lỗi)
    - _Requirements: 2.1, 2.4, 2.5, 3.1, 3.2, 3.3, 3.4, 3.5, 4.1, 4.2_

  - [x] 2.6 Tạo CancelTripHandler
    - Tạo `CancelTripHandler` trong `Application/UseCases/Trips/CancelTripHandler.cs`
    - Định nghĩa `CancelTripCommand` (TripId, ExpectedVersion, Reason, ConfirmUserId, CorrelationId) và `CancelTripResult`
    - Kiểm tra quyền `trip.cancel`, version check (optimistic concurrency)
    - Chỉ cho phép hủy khi Lifecycle ∈ {TempRegistered, Registered} và chưa có WeighSession Committed
    - Chuyển Lifecycle → Cancelled, thu hồi QrToken → Revoked, giải phóng hàng chờ
    - Nếu nguồn FRM → gọi `NotifyCancellationAsync`, xử lý UNKNOWN theo R-024
    - Ghi audit log đầy đủ
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6_

  - [x] 2.7 Tạo ITripRegistrationQueries và models
    - Tạo `ITripRegistrationQueries` trong `Application/Queries/TripRegistrationQueries.cs`
    - Định nghĩa `TripRegistrationRow`, `TripRegistrationFilter`, `TripGridTab` enum (InFactory, Exited, Cancelled)
    - Method `ListAsync(Guid siteId, TripRegistrationFilter filter, CancellationToken ct)`
    - _Requirements: 7.1, 7.2, 7.3_

- [x] 3. Checkpoint — Đảm bảo Domain và Application layer biên dịch thành công
  - Đảm bảo tất cả tests hiện tại vẫn pass, hỏi user nếu có vấn đề phát sinh.

- [x] 4. Infrastructure layer — Migration, repositories, queries
  - [x] 4.1 Tạo EF migration AddTripRegistrationRedesign
    - Tạo migration trong `Infrastructure/Persistence/Migrations/`
    - ALTER TABLE trips: thêm 11 cột mới (vehicle_type, queue_sequence_number, assigned_weighbridge_id, assigned_warehouse_id, gate_id, sugarcane_type, sugarcane_variety, sugarcane_owner, harvest_method, station, crop_season) — tất cả nullable
    - ALTER TABLE qr_tokens: thêm cột encoded_content (text, nullable)
    - CREATE TABLE queue_sequence_tracker (site_id uuid, vehicle_type varchar(20), operating_date date, last_value int DEFAULT 0, PRIMARY KEY composite)
    - Tạo indexes: idx_trips_vehicle_type, idx_trips_gate_id, idx_trips_queue_seq
    - Cập nhật EF entity configuration trong `ModuleConfigurations.cs`
    - Migration phải có cả `Up()` và `Down()` theo dev guidelines
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6_

  - [x] 4.2 Implement SequentialQueueSequenceGenerator
    - Tạo `SequentialQueueSequenceGenerator` trong `Infrastructure/Repositories/`
    - Implement `IQueueSequenceGenerator` sử dụng bảng `queue_sequence_tracker`
    - Dùng `INSERT ... ON CONFLICT DO UPDATE SET last_value = last_value + 1 RETURNING last_value` (PostgreSQL) để đảm bảo atomic, không trùng số khi nhiều trạm đồng thời
    - Reset hằng ngày bằng operating_date trong composite key
    - _Requirements: 2.2, 2.3_

  - [x] 4.3 Tạo FrmPlanLookupAdapter (stub)
    - Tạo `FrmPlanLookupAdapter` trong `Infrastructure/Repositories/` implement `IFrmPlanLookup`
    - Stub ban đầu trả danh sách rỗng cho `LookupAsync` và Success cho `NotifyCancellationAsync`
    - Cấu hình timeout từ appsettings (HttpClient)
    - _Requirements: 1.1, 1.4, 5.4_

  - [x] 4.4 Tạo CameraServiceAdapter (stub)
    - Tạo `CameraServiceAdapter` trong `Infrastructure/Repositories/` implement `ICameraService`
    - Stub ban đầu: `CaptureSnapshotAsync` trả null, `GetStatusAsync` trả Online
    - _Requirements: 10.2, 10.3_

  - [x] 4.5 Implement EfTripRegistrationQueries
    - Tạo `EfTripRegistrationQueries` trong `Infrastructure/Queries/`
    - Implement `ITripRegistrationQueries` bằng EF LINQ
    - Tab InFactory: Lifecycle ∈ {TempRegistered, Registered, InProgress, WeighingCompleted, Reconciled, Posted}
    - Tab Exited: Lifecycle = Closed
    - Tab Cancelled: Lifecycle = Cancelled
    - Lọc theo khoảng ngày [from, to) trên CreatedAt
    - _Requirements: 7.1, 7.2, 7.3_

  - [x] 4.6 Đăng ký DI cho các service mới
    - Cập nhật `Infrastructure/DependencyInjection.cs` đăng ký: IFrmPlanLookup, ICameraService, IQueueSequenceGenerator, ITripRegistrationQueries
    - Đăng ký CheckPlanHandler, QueueTripHandler, CancelTripHandler
    - _Requirements: (cross-cutting)_

- [x] 5. Checkpoint — Đảm bảo Infrastructure layer biên dịch và migration chạy đúng
  - Đảm bảo tất cả tests hiện tại vẫn pass, hỏi user nếu có vấn đề phát sinh.

- [x] 6. WinForms layer — Form, presenter, controls
  - [x] 6.1 Tạo CameraViewControl
    - Tạo `CameraViewControl` UserControl trong `WinForms/Controls/`
    - Hiển thị khung hình camera (placeholder image khi offline)
    - Property: GateId, CameraPosition (In/Out)
    - Method: `UpdateStatusAsync()` để kiểm tra và hiển thị trạng thái Online/Offline
    - _Requirements: 10.1, 10.3, 10.4_

  - [x] 6.2 Tạo TripRegistrationPresenter
    - Tạo `TripRegistrationPresenter` trong `WinForms/Presenters/`
    - Inject: CheckPlanHandler, RegisterTripHandler, CancelTripHandler, ITripRegistrationQueries, ICameraService, ISiteContext, ICurrentUser
    - Methods: `CheckPlanAsync`, `RegisterAsync`, `RegisterTempAsync`, `CancelAsync`, `LoadGridAsync`, `GetCameraStatusAsync`
    - Form gọi Presenter, Presenter gọi Application handlers — đúng pattern MVP
    - _Requirements: 1.1, 3.1, 4.1, 5.1, 7.1, 10.1_

  - [x] 6.3 Viết lại TripRegistrationForm — Layout và form fields
    - Viết lại `TripRegistrationForm` trong `WinForms/Forms/TripRegistrationForm.cs`
    - Bố cục theo wireframe design: panel trái (form đăng tài), panel phải trên (2 camera), panel phải dưới (panel thông báo), panel dưới (lưới chuyến), thanh trạng thái
    - Các trường: Cổng, Biển_số + nút Kiểm tra, Số đăng tài (auto), Lệnh đốn/Shipment, Ngày giờ, Loại_xe (ComboBox), Loại mía, Giống mía, Chủ mía, Hình thức thu hoạch, Trạm, Vụ mùa, Ghi chú
    - Các nút: Lưu phiếu (F4), In phiếu (F5), Xác nhận (F6), Làm mới (Esc)
    - Tự động điền các trường từ kế hoạch FRM/ERP khi chọn (Req 6.2)
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5_

  - [x] 6.4 TripRegistrationForm — Lưới chuyến và bộ lọc
    - Thêm DataGridView (`dgvTrips`) với các cột: Số đăng tài, Trip number, Shipment, Biển số, Số thứ tự, Tài xế, Thời gian vào, Thời gian ra, Nhà vận tải, Khách hàng/NCC, Hàng hóa
    - Thêm tabs lọc nhanh: Trong nhà máy, Đã ra, Phiếu đã hủy
    - Thêm bộ lọc khoảng ngày (DateTimePicker from/to)
    - Chọn dòng trong lưới → điền thông tin lên form (Req 7.4)
    - Tự động refresh lưới khi có thay đổi (timer hoặc event) (Req 7.5)
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5_

  - [x] 6.5 TripRegistrationForm — Phím tắt và event handlers
    - Bind phím tắt: F4 → Lưu phiếu, F5 → In phiếu, F6 → Xác nhận, Esc → Làm mới form
    - Event handlers gọi Presenter (không xử lý business logic trong form)
    - F4: gọi `_presenter.RegisterAsync(...)` hoặc `RegisterTempAsync(...)` tùy context
    - F5: in lại phiếu với nhãn "BẢN IN LẠI", ghi audit log (Req 8.3)
    - Esc: reset form về trạng thái trống
    - Xử lý luồng hủy: yêu cầu xác nhận đăng nhập + lý do trước khi gọi `CancelAsync`
    - Panel thông báo ghi nhận sự kiện trong phiên (Req 6.4)
    - Thanh trạng thái: Server, Barcode, LPR Camera, Camera chung (Req 6.5)
    - Wrap async calls với try/catch, hiển thị lỗi user-friendly
    - _Requirements: 5.2, 6.3, 6.4, 6.5, 8.3, 8.5_

- [x] 7. Checkpoint — Đảm bảo toàn bộ solution biên dịch thành công
  - Đảm bảo tất cả tests hiện tại vẫn pass, hỏi user nếu có vấn đề phát sinh.

- [x] 8. Unit tests cho handlers
  - [x] 8.1 Unit tests cho CheckPlanHandler
    - Tạo test class trong `tests/TTC.Weighing.Application.Tests/`
    - Test cases: quyền thiếu → trả lỗi, lookup thành công trả danh sách kế hoạch, lookup trả rỗng (không tìm thấy), FRM lỗi kết nối → trả IsSuccess = false với ErrorMessage, nhiều kế hoạch khớp
    - Mock IFrmPlanLookup, IClock, ICurrentUser
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_

  - [x] 8.2 Unit tests cho RegisterTripHandler
    - Test luồng có kế hoạch: tạo Trip Registered, gọi QueueSequenceGenerator, gọi QueueTripHandler, mã hóa QR_chuyến
    - Test luồng QR tạm: tạo Trip TempRegistered, QR_tạm chỉ có token prefix
    - Test VehicleType null cho mía nhập → từ chối
    - Test VehicleType null cho vật tư → cho phép
    - Test profile chưa duyệt/hết hiệu lực → từ chối
    - Mock tất cả dependencies
    - _Requirements: 2.1, 2.4, 3.1, 4.1_

  - [x] 8.3 Unit tests cho CancelTripHandler
    - Test hủy thành công: Lifecycle → Cancelled, QrToken → Revoked
    - Test từ chối khi đã có WeighSession Committed
    - Test version mismatch → concurrency error
    - Test thiếu lý do → từ chối
    - Test nguồn FRM → gọi NotifyCancellationAsync, xử lý UNKNOWN
    - _Requirements: 5.1, 5.3, 5.4, 5.5, 5.6_

  - [x] 8.4 Unit tests cho QueueTripHandler
    - Test gán bàn cân có ít chuyến chờ nhất
    - Test không có bàn cân → thành công với AssignedWeighbridgeId = null
    - Test gán kho đích từ kế hoạch nguồn
    - _Requirements: 9.1, 9.2, 9.5_

  - [x] 8.5 Unit tests cho QrTokenFactory (Encode/Decode)
    - Test encode QR_chuyến chứa đủ token prefix, số thứ tự, bàn cân, kho
    - Test encode QR_tạm chỉ có token prefix
    - Test decode → round-trip chính xác
    - _Requirements: 8.2_

- [x] 9. Property-based tests với FsCheck
  - [x]* 9.1 Property test: VehicleType bắt buộc cho mía nhập
    - **Property 1: Quy tắc bắt buộc VehicleType theo loại hồ sơ**
    - **Validates: Requirements 2.1, 2.4**
    - Với mọi RegisterTripCommand mía nhập + VehicleType null → IsSuccess = false
    - Với mọi command không phải mía nhập + VehicleType null → IsSuccess = true (điều kiện khác hợp lệ)

  - [x]* 9.2 Property test: Số thứ tự hàng chờ tăng đơn điệu
    - **Property 2: Queue sequence monotonic**
    - **Validates: Requirements 2.2**
    - Với mọi chuỗi N lần gọi NextAsync cùng (siteId, vehicleType, operatingDate) → dãy số tăng nghiêm ngặt

  - [x]* 9.3 Property test: QR content round-trip
    - **Property 3: Nội dung mã hóa QR round-trip**
    - **Validates: Requirements 2.5, 3.3, 8.2**
    - Với mọi QR_chuyến: decode(encode(S, B, W)) chứa đúng S, B, W
    - Với mọi QR_tạm: decode(encode()) chỉ chứa token prefix, không có S/B/W

  - [x]* 9.4 Property test: Lifecycle đúng theo luồng đăng tài
    - **Property 4: Registration lifecycle**
    - **Validates: Requirements 3.1, 4.1**
    - Với mọi command có kế hoạch + IssueTemporaryQrOnly = false → Lifecycle = Registered
    - Với mọi command không có kế hoạch hoặc IssueTemporaryQrOnly = true → Lifecycle = TempRegistered

  - [x]* 9.5 Property test: Xếp hàng tự động cho trip Registered
    - **Property 5: Queue assignment**
    - **Validates: Requirements 3.2, 9.1, 9.2**
    - Với mọi trip Registered + VehicleType != null + có bàn cân khả dụng → AssignedWeighbridgeId != null, QueueSequenceNumber > 0

  - [x]* 9.6 Property test: Chuyển TempRegistered sang Registered
    - **Property 6: Temp-to-Registered promotion**
    - **Validates: Requirements 4.3, 4.5**
    - Với mọi trip TempRegistered khi nhận xác nhận FRM → Lifecycle = Registered, QR cũ Revoked, QR mới Issued có Số_thứ_tự

  - [x]* 9.7 Property test: Điều kiện và hệ quả hủy đăng tài
    - **Property 7: Cancellation logic**
    - **Validates: Requirements 5.1, 5.3, 5.5, 9.3**
    - Lifecycle ∈ {TempRegistered, Registered} + không có WeighSession Committed → hủy thành công, Lifecycle = Cancelled, QrToken.Status = Revoked
    - Có WeighSession Committed → hủy thất bại, Lifecycle giữ nguyên

  - [x]* 9.8 Property test: Bộ lọc lưới chuyến đúng logic
    - **Property 8: Grid filter correctness**
    - **Validates: Requirements 7.2, 7.3**
    - InFactory → chỉ trips Lifecycle ∈ {TempRegistered, Registered, InProgress, WeighingCompleted, Reconciled, Posted}
    - Cancelled → chỉ trips Lifecycle = Cancelled
    - Exited → chỉ trips Lifecycle = Closed
    - Khoảng ngày [from, to) → mọi trip có CreatedAt ∈ [from, to)

  - [x]* 9.9 Property test: Trip entity persistence round-trip
    - **Property 9: Trip entity persistence round-trip**
    - **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5, 11.6**
    - Với mọi Trip hợp lệ với bất kỳ tổ hợp giá trị trường mới (bao gồm null) → lưu vào DB rồi đọc lại → mọi trường giữ nguyên
    - Trip cũ với tất cả trường mới null vẫn load và IsOperable trả đúng kết quả

- [x] 10. Checkpoint cuối — Đảm bảo tất cả tests pass
  - Đảm bảo tất cả tests pass, hỏi user nếu có vấn đề phát sinh.

## Notes

- Tasks đánh dấu `*` là tùy chọn (property-based tests) và có thể bỏ qua cho MVP nhanh
- Mỗi task tham chiếu requirements cụ thể để truy xuất nguồn gốc
- Checkpoints (task 3, 5, 7, 10) đảm bảo kiểm tra tăng dần sau từng lớp
- Property tests sử dụng FsCheck (NuGet: FsCheck, FsCheck.Xunit) — tối thiểu 100 iterations mỗi property
- Unit tests và property tests bổ trợ cho nhau: unit tests kiểm tra ví dụ cụ thể, property tests kiểm tra bất biến tổng quát
- Stub adapters (FrmPlanLookupAdapter, CameraServiceAdapter) sẽ được thay thế bằng implementation thật trong sprint tích hợp tiếp theo
- Ngôn ngữ implementation: C# (.NET 8+) — đúng với stack dự án hiện tại

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2"] },
    { "id": 1, "tasks": ["2.1", "2.2", "2.7"] },
    { "id": 2, "tasks": ["2.3", "2.4"] },
    { "id": 3, "tasks": ["2.5", "2.6"] },
    { "id": 4, "tasks": ["4.1", "4.3", "4.4"] },
    { "id": 5, "tasks": ["4.2", "4.5", "4.6"] },
    { "id": 6, "tasks": ["6.1", "6.2"] },
    { "id": 7, "tasks": ["6.3"] },
    { "id": 8, "tasks": ["6.4", "6.5"] },
    { "id": 9, "tasks": ["8.1", "8.2", "8.3", "8.4", "8.5"] },
    { "id": 10, "tasks": ["9.1", "9.2", "9.3", "9.4", "9.5", "9.6", "9.7", "9.8", "9.9"] }
  ]
}
```
