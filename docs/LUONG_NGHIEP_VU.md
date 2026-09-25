# Sơ đồ luồng nghiệp vụ — TTC Hệ thống cân hợp nhất mía và vật tư

---

## 1. Vòng đời chuyến (Trip Lifecycle)

```
┌──────────────┐    ┌────────────┐    ┌────────────┐    ┌──────────────────┐    ┌────────────┐    ┌────────┐    ┌────────┐
│ TempRegistered│───▶│ Registered │───▶│ InProgress │───▶│WeighingCompleted │───▶│ Reconciled │───▶│ Posted │───▶│ Closed │
│  (QR tạm)    │    │ (QR chuyến)│    │            │    │                  │    │            │    │        │    │        │
└──────┬───────┘    └─────┬──────┘    └────────────┘    └──────────────────┘    └────────────┘    └────────┘    └────────┘
       │                  │
       │  ┌───────────┐   │
       └──┤ Cancelled │◀──┘  (chỉ hủy được khi chưa có phiên cân Committed)
          └───────────┘
```

**Trục trạng thái thực thi** (độc lập vòng đời):
```
Active ◄══════════▶ Paused   (PAUSED giữ nguyên bước cuối, không quay về đầu)
```

---

## 2. Luồng chính — Nhập mía (Sugarcane Inbound)

```
 ╔═══════════════════════════════════════════════════════════════════════════════════════════╗
 ║                          LUỒNG NHẬP MÍA ĐẦY ĐỦ                                         ║
 ╚═══════════════════════════════════════════════════════════════════════════════════════════╝

  ┌─────────────────────────────────────────────────────────────────────────────────────────┐
  │ BƯỚC 1: ĐĂNG TÀI (UI-02)                                              TripRegistration │
  │                                                                                         │
  │  Nhập biển số ──▶ Kiểm tra FRM API ──┬──▶ Có kế hoạch ──▶ Đăng tài chính thức         │
  │                   (the-xe, brix...)   │                     ├─ Phát QR Chuyến            │
  │                                       │                     ├─ Gán STT hàng chờ          │
  │                                       │                     ├─ Gán bàn cân + kho         │
  │                                       │                     └─ Chụp camera vào           │
  │                                       │                                                  │
  │                                       └──▶ Không có KH ──▶ Phát QR Tạm                  │
  │                                                             (không đủ ĐK cân chính thức) │
  │                                                                                         │
  │  Handler: CheckPlanHandler → RegisterTripHandler → QueueTripHandler                     │
  │  DB ghi: trips, qr_tokens, checkpoint_events, queue_sequence_trackers                   │
  └─────────────────────────────────────────────────────────────────────────────────────────┘
                                          │
                                          ▼
  ┌─────────────────────────────────────────────────────────────────────────────────────────┐
  │ BƯỚC 2: VÀO CỔNG (UI-11)                                               GateControl     │
  │                                                                                         │
  │  Quét QR ──▶ Tra cứu chuyến ──▶ Kiểm tra điều kiện ──┬──▶ ĐẠT ──▶ Phát lệnh mở barie │
  │                                  ├ QR hợp lệ?         │           (TTL 30 giây)         │
  │                                  ├ Danh tính xe?       │           │                     │
  │                                  ├ Barie sẵn sàng?    │           ▼                     │
  │                                  └ Không có lệnh treo? │    Xác nhận xe qua cổng        │
  │                                                        │                                │
  │                                                        └──▶ TỪ CHỐI ──▶ Hiện lý do     │
  │                                                                                         │
  │  Handler: EvaluateGateHandler → ConfirmVehiclePassageHandler                            │
  │  DB ghi: gate_decisions, gate_commands, checkpoint_events (GateIn)                      │
  └─────────────────────────────────────────────────────────────────────────────────────────┘
                                          │
                                          ▼
  ┌─────────────────────────────────────────────────────────────────────────────────────────┐
  │ BƯỚC 3: CÂN LẦN 1 — GROSS (UI-03)                                   WeighOperation    │
  │                                                                                         │
  │  Quét QR ──▶ Giải mã chuyến ──▶ Mở phiên cân (khóa bàn cân) ──▶ Hiển thị số cân      │
  │                                                                      realtime (250ms)   │
  │                                                                                         │
  │  Checklist điều kiện (kiểm tra mỗi 800ms — DryRun mode):                               │
  │    ☐ QR hợp lệ + khớp chuyến + khớp nhà máy                                           │
  │    ☐ AI vị trí: VALID + confidence đủ + chưa quá cũ                                    │
  │    ☐ Cân ổn định ≥ cửa sổ (ví dụ 5s) + biên dao động đủ nhỏ                           │
  │    ☐ Tín hiệu liên tục + trong tải trọng bàn cân                                      │
  │    ☐ Danh tính xe đã xác thực                                                          │
  │    ☐ Không có giao dịch UNKNOWN treo                                                   │
  │    ☐ Bằng chứng lưu được                                                               │
  │                                                                                         │
  │  Đủ ĐK ──▶ Bấm F5 ──▶ Server chốt cân ──▶ WeighEvent (bất biến)                      │
  │                         ├─ Lưu bằng chứng AI                                           │
  │                         ├─ Trip: Registered → InProgress                                │
  │                         └─ Ghi audit                                                    │
  │                                                                                         │
  │  Handler: OpenWeighSessionHandler → EvaluateAndCommitWeightHandler                      │
  │  DB ghi: weigh_sessions, weigh_events, audit_log_entries                                │
  └─────────────────────────────────────────────────────────────────────────────────────────┘
                                          │
                                          ▼
  ┌─────────────────────────────────────────────────────────────────────────────────────────┐
  │ BƯỚC 4: ĐO TẠP CHẤT (UI-05)                                          Quality           │
  │                                                                                         │
  │  Quét QR ──▶ Hiện thông tin mía ──▶ Cân mẫu tự động 2 bước:                           │
  │                                      │                                                  │
  │                                      ├─ Bước 1: Đặt mẫu trước ──▶ chờ ổn định ──▶ ghi │
  │                                      └─ Bước 2: Đặt mẫu sau   ──▶ chờ ổn định ──▶ ghi │
  │                                                                                         │
  │  Tính: tạp chất % = (trước − sau) / trước × 100                                       │
  │  Bấm "Ghi kết quả" ──▶ QualitySample (type=Impurity)                                  │
  │                                                                                         │
  │  ⚠ MẪU SAU > MẪU TRƯỚC → từ chối (phải lấy lại mẫu)                                  │
  │  ⚠ Mẫu dưới ngưỡng   → cảnh báo vàng (cho phép ghi)                                  │
  │                                                                                         │
  │  Handler: RecordImpuritySampleHandler                                                   │
  │  DB ghi: quality_samples, audit_log_entries                                             │
  └─────────────────────────────────────────────────────────────────────────────────────────┘
                                          │
                               ┌──────────┴──────────┐
                               ▼                      ▼
  ┌──────────────────────────────────┐  ┌──────────────────────────────────┐
  │ BƯỚC 4b: CHỮ ĐƯỜNG (UI-05)      │  │ (Tùy chọn — có thể đến sau     │
  │                                  │  │  khi xe đã ra cổng)             │
  │ Nhập CCS hoặc đánh dấu Pending  │  │                                  │
  │ → QualitySample (type=CCS)      │  │ Thiếu CCS = cảnh báo vàng,     │
  │                                  │  │ KHÔNG chặn cân lần 2             │
  │ Handler: ReceiveCcsResultHandler │  │                                  │
  └──────────────────────────────────┘  └──────────────────────────────────┘
                                          │
                                          ▼
  ┌─────────────────────────────────────────────────────────────────────────────────────────┐
  │ BƯỚC 5: CÂN LẦN 2 — TARE (UI-03)                                   WeighOperation     │
  │                                                                                         │
  │  Quét QR ──▶ Hệ thống tự nhận diện segment 2 ──▶ Kiểm tra:                            │
  │                                                    │                                    │
  │    ├─ Đã đo tạp chất? ──▶ CHƯA ──▶ 🔴 CHẶN ("Phải đo tạp chất trước")               │
  │    │                      ĐÃ   ──▶ ✅ Cho phép                                        │
  │    │                                                                                    │
  │    └─ Có chữ đường?   ──▶ CHƯA ──▶ 🟡 Cảnh báo (cho phép tiếp tục)                   │
  │                           CÓ    ──▶ ✅ OK                                              │
  │                                                                                         │
  │  Gọi FRM đồng bộ thông tin mới nhất (loại mía, giống, chủ mía)                        │
  │  Chờ ổn định ──▶ F5 ──▶ Chốt cân lần 2                                                │
  │  Cả 2 lượt cân đã committed ──▶ Trip: InProgress → WeighingCompleted                   │
  │                                                                                         │
  │  Sau chốt: Bật In phiếu (F6) + Sync ERP                                               │
  │                                                                                         │
  │  Handler: EvaluateAndCommitWeightHandler → AdvanceWhenAllWeightsCommitted               │
  │  DB ghi: weigh_sessions, weigh_events, audit_log_entries                                │
  └─────────────────────────────────────────────────────────────────────────────────────────┘
                                          │
                              ┌───────────┴───────────┐
                              ▼                       ▼
  ┌───────────────────────────────────┐  ┌──────────────────────────────────┐
  │ BƯỚC 6a: IN PHIẾU CÂN (F6)      │  │ BƯỚC 6b: ĐỒNG BỘ ERP            │
  │                                   │  │                                  │
  │ Chọn máy in + mẫu phiếu          │  │ Bấm Sync ERP ──▶ Tạo thông điệp│
  │ (SUGARCANE / MATERIAL)            │  │ ├─ Hợp đồng chưa XM → HELD     │
  │ In lại = "BẢN IN LẠI" + audit    │  │ └─ Hợp đồng XM → NEW           │
  │                                   │  │                                  │
  │ Lỗi in ≠ mất dữ liệu cân        │  │ Handler: SubmitTripResultHandler │
  │                                   │  │ DB: integration_messages          │
  └───────────────────────────────────┘  └──────────────────────────────────┘
                                          │
                                          ▼
  ┌─────────────────────────────────────────────────────────────────────────────────────────┐
  │ BƯỚC 7: ĐỐI SOÁT (UI-04 hoặc tự động)                              Reconciliation     │
  │                                                                                         │
  │  So sánh 4 phép:                                                                       │
  │    ┌────────────────────────────────┬────────────────────────────────────┐              │
  │    │ Phép đối soát                  │ So sánh                            │              │
  │    ├────────────────────────────────┼────────────────────────────────────┤              │
  │    │ VehicleNet vs IoT Mass         │ Chênh lệch cân xe ↔ IoT           │              │
  │    │ AI BagCount vs Warehouse Count │ Đếm bao AI ↔ kho xác nhận         │              │
  │    │ VehicleNet vs Nominal Range    │ Cân xe ↔ BOM quy cách             │              │
  │    │ Trip Actual vs Source Plan     │ Thực tế ↔ kế hoạch nguồn          │              │
  │    └────────────────────────────────┴────────────────────────────────────┘              │
  │                                                                                         │
  │  Kết luận: Pass / Mismatch / MissingData / Pending / NotApplicable                     │
  │  Tất cả phép bắt buộc đạt ──▶ Trip: WeighingCompleted → Reconciled                    │
  │                                                                                         │
  │  Handler: ReconcileTripHandler                                                          │
  │  DB ghi: reconciliations, reconciliation_checks                                         │
  └─────────────────────────────────────────────────────────────────────────────────────────┘
                                          │
                                          ▼
  ┌─────────────────────────────────────────────────────────────────────────────────────────┐
  │ BƯỚC 8: PHÂN BỔ (UI-06 tab Phân bổ)                                 Allocation         │
  │  (khi 1 chuyến có nhiều chủ mía / Shipment)                                            │
  │                                                                                         │
  │  Phương pháp: Theo tỷ lệ duyệt / Số lượng + phần dư / Tỷ lệ trọng lượng đơn         │
  │  Bắt buộc phê duyệt. Tổng phân bổ = tổng net thực tế (bảo toàn).                     │
  │                                                                                         │
  │  Handler: AllocateTripHandler                                                           │
  │  DB ghi: allocations                                                                    │
  └─────────────────────────────────────────────────────────────────────────────────────────┘
                                          │
                                          ▼
  ┌─────────────────────────────────────────────────────────────────────────────────────────┐
  │ BƯỚC 9: RA CỔNG (UI-11)                                               GateControl      │
  │                                                                                         │
  │  Quét QR ──▶ Kiểm tra trip đã qua đủ checkpoint bắt buộc:                              │
  │               (WeighingCompleted / Reconciled / Posted / Closed)                         │
  │                                                                                         │
  │  ĐẠT ──▶ Mở barie 30s ──▶ Xác nhận xe qua ──▶ Thu hồi QR ──▶ Trip → Closed           │
  │                                                                                         │
  │  Handler: EvaluateGateHandler → ConfirmVehiclePassageHandler                            │
  │  DB ghi: gate_decisions, gate_commands, qr_tokens (revoke)                              │
  └─────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Luồng phụ — Vật tư qua kho (Material + Warehouse)

```
  Đăng tài (ERP) ──▶ Vào cổng ──▶ Cân lần 1 ──▶ VÀO KHO ──▶ Phiên đo IoT ──▶ RA KHO
                                                    │              │
                                                    ▼              ▼
                                              Checkpoint      Đếm bao AI
                                              WarehouseIn     + cân IoT
                                                    │              │
                                                    ▼              ▼
                                               Cân lần 2 ──▶ Đối soát ──▶ Ra cổng

  ┌──────────────────────────────────────────────────────────────────────────┐
  │ KHO VẬN (UI-04)                                                         │
  │                                                                         │
  │  Vào kho (F4) ──▶ Mở phiên đo ──▶ Thu thập IoT/đếm bao               │
  │                                     ├─ Delta mode: tổng cộng dồn       │
  │                                     └─ Cumulative: đọc bộ đếm          │
  │                                                                         │
  │  Đóng phiên ──▶ Tổng hợp: iot_mass_kg, net_bag_count                  │
  │                  ├─ Closed (đủ dữ liệu)                                │
  │                  ├─ ClosedIncomplete (thiếu sequence)                   │
  │                  └─ Held (cần xác minh)                                │
  │                                                                         │
  │  Hoàn thành ra kho (F5) ──▶ Checkpoint WarehouseOut                    │
  └──────────────────────────────────────────────────────────────────────────┘
```

---

## 4. Luồng ngoại lệ

```
  ┌──────────────────────────────────────────────────────────────────────────┐
  │ NGOẠI LỆ TRÊN FORM CÂN (UI-03 / UI-07)                                │
  │                                                                         │
  │  ┌─────────────┐     ┌───────────────┐     ┌─────────────────────┐     │
  │  │  Nhập tay    │     │   Cân lại      │     │  Tạm ngưng / Tiếp  │     │
  │  │  (MANUAL)    │     │   (REWEIGH)    │     │  tục (PAUSE/RESUME) │     │
  │  └──────┬───────┘     └───────┬────────┘     └──────────┬──────────┘     │
  │         │                     │                          │               │
  │         ▼                     ▼                          ▼               │
  │  ☑ Quyền manual       ☑ Quyền reweigh          ☑ Quyền pause/resume   │
  │  ☑ Lý do bắt buộc     ☑ Lý do bắt buộc          ☑ Mã sự cố            │
  │  ☑ Phê duyệt          ☑ Tài liệu sự cố          ☑ Xử lý UNKNOWN      │
  │  ☒ Trip ≠ Paused       ☑ Đã gửi ERP?             ☑ Xác nhận kiểm tra  │
  │  ☒ Không có UNKNOWN       → REWEIGH_AFTER_POST                         │
  │         │                     │                          │               │
  │         ▼                     ▼                          ▼               │
  │  WeighEvent             WeighEvent mới           Trip.ExecutionStatus   │
  │  source=MANUAL          supersedes bản cũ        = Paused / Active      │
  │                         (bản cũ giữ nguyên)      + Incident ghi nhận    │
  └──────────────────────────────────────────────────────────────────────────┘

  ┌──────────────────────────────────────────────────────────────────────────┐
  │ HỦY ĐĂNG TÀI                                                           │
  │                                                                         │
  │  Chỉ khi: Lifecycle ∈ {TempRegistered, Registered}                     │
  │           VÀ chưa có WeighSession Committed                             │
  │                                                                         │
  │  Thu hồi QR ──▶ Giải phóng hàng chờ ──▶ Thông báo FRM (nếu nguồn FRM)│
  │  Trip → Cancelled                                                       │
  └──────────────────────────────────────────────────────────────────────────┘
```

---

## 5. Tích hợp hệ thống ngoài

```
  ┌──────────────────────────────────────────────────────────────────────────┐
  │                        TÍCH HỢP FRM / ERP                               │
  │                                                                         │
  │   ┌──────────┐         ┌──────────────┐         ┌──────────────┐       │
  │   │ DigiScale│ ◄──────▶│  App Cân     │ ◄──────▶│  PostgreSQL  │       │
  │   │ FRM API  │  HTTP   │  (WinForms)  │  EF Core│  (local DB)  │       │
  │   └──────────┘         └──────┬───────┘         └──────────────┘       │
  │                               │                                         │
  │   Tra cứu kế hoạch:          │   Gửi kết quả:                          │
  │   POST /api/v1/integration/   │   IntegrationMessage                    │
  │        frm                    │   ├─ business_key chống lặp             │
  │   entities: the-xe, brix...   │   ├─ HELD (chưa XM hợp đồng)          │
  │   Auth: Basic                 │   ├─ NEW (sẵn sàng gửi)               │
  │                               │   ├─ UNKNOWN (cần đối soát)            │
  │   FRM Sync (cân lần 2):      │   └─ Acknowledged / Failed             │
  │   GET /api/trips/{id}/        │                                         │
  │       sugarcane-info          │                                         │
  └──────────────────────────────────────────────────────────────────────────┘
```

---

## 6. Kiến trúc phân lớp

```
  ┌─────────────────────────────────────────────────────────────────────┐
  │                    WinForms (UI Layer)                               │
  │  Forms: MainForm, TripRegistration, WeighOperation, Quality,        │
  │         MeasurementSession, GateControl, TripDetail, Report...      │
  │  Presenters: MVP pattern — Form gọi Presenter, không gọi thẳng DB  │
  │  Controls: WeighDisplay, TripInfo, CameraStatus, Checklist          │
  └─────────────────────────────┬───────────────────────────────────────┘
                                │ gọi
                                ▼
  ┌─────────────────────────────────────────────────────────────────────┐
  │                  Application (Use Cases)                            │
  │  Handlers: RegisterTrip, CheckPlan, OpenWeighSession,              │
  │            EvaluateAndCommitWeight, RecordImpurity, ReceiveCcs,     │
  │            ReconcileTrip, AllocateTrip, EvaluateGate,              │
  │            SubmitTripResult, PauseResume...                         │
  │  Interfaces: IFrmPlanLookup, ITripRepository, IScaleSignalGateway  │
  └─────────────────────────────┬───────────────────────────────────────┘
                                │ implements
                                ▼
  ┌───────────────────────────────┐  ┌──────────────────────────────────┐
  │    Infrastructure             │  │      Integration                  │
  │  EF Core + PostgreSQL         │  │  IFrmAdapter (stub → HELD)       │
  │  FrmPlanLookupAdapter (HTTP)  │  │  IErpAdapter (stub → HELD)       │
  │  FrmSyncAdapter (HTTP)        │  │  IntegrationContracts             │
  │  Simulated devices            │  │                                   │
  │  Repositories                 │  │  (Thay adapter thật khi D-02     │
  │  Queries (read model)         │  │   cung cấp hợp đồng đã XM)      │
  └───────────────────────────────┘  └──────────────────────────────────┘
                                │
                                ▼
  ┌─────────────────────────────────────────────────────────────────────┐
  │                      Domain (Pure Logic)                            │
  │  Entities: Trip, WeighEvent, QualitySample, Reconciliation...      │
  │  Enums: TripLifecycle, WeightRole, CheckpointType...               │
  │  Rules: WeighCommitRules, ReconciliationRules, GateDecisionRules,  │
  │         NetWeightCalculator, ImpurityCalculator, AllocationRules    │
  │  ⚠ Không biết EF, WinForms, HTTP — chỉ business logic thuần       │
  └─────────────────────────────────────────────────────────────────────┘
```

---

## 7. Bảng tham chiếu nhanh: Màn hình ↔ Handler ↔ Checkpoint

| Bước | Màn hình | Handler chính | Checkpoint | Lifecycle chuyển |
|------|----------|---------------|------------|------------------|
| Đăng tài | UI-02 | RegisterTripHandler | Registration | → TempRegistered / Registered |
| Vào cổng | UI-11 | EvaluateGateHandler | GateIn | — |
| Cân lần 1 | UI-03 | EvaluateAndCommitWeightHandler | FirstWeight | → InProgress |
| Đo tạp chất | UI-05 | RecordImpuritySampleHandler | ImpuritySample | — |
| Chữ đường | UI-05 | ReceiveCcsResultHandler | CcsSample | — |
| Cân lần 2 | UI-03 | EvaluateAndCommitWeightHandler | SecondWeight | → WeighingCompleted |
| Kho vào | UI-04 | OpenMeasurementSessionHandler | WarehouseIn | — |
| Kho ra | UI-04 | CloseMeasurementSessionHandler | WarehouseOut | — |
| Đối soát | UI-04/06 | ReconcileTripHandler | Reconciliation | → Reconciled |
| Đồng bộ | UI-03/08 | SubmitTripResultHandler | Sync | → Posted |
| Ra cổng | UI-11 | EvaluateGateHandler | GateOut | → Closed |

---

## 8. Quy tắc nghiệp vụ quan trọng (không vi phạm)

| # | Quy tắc | Hệ quả |
|---|---------|--------|
| 1 | Server quyết định chốt cân | UI chỉ hiển thị, không tự tính |
| 2 | Không dùng Math.Abs() với net âm | Net âm = ngoại lệ, phải xử lý riêng |
| 3 | WeighEvent bất biến | Cân lại → bản mới + supersedes_id |
| 4 | Không cộng cân xe + IoT | Có thể đo cùng lô |
| 5 | QR tạm ≠ cân chính thức | Phải có chứng từ nguồn |
| 6 | 1 bàn cân = 1 phiên sở hữu | Kiểm tra trước khi mở phiên mới |
| 7 | Thiếu tạp chất → CHẶN cân 2 | Thiếu CCS → chỉ cảnh báo |
| 8 | Lệnh barie có command_id + TTL | Không tự xác nhận xe qua từ ACK |
| 9 | UNKNOWN ≠ FAILED | Phải tra cứu/đối soát trước retry |
| 10 | Mọi thao tác có audit | Kể cả lần bị chặn và từ chối |
