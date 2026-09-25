# Ánh xạ Màn hình ↔ Database — TTC Hệ thống cân hợp nhất

> Tài liệu giúp kiểm tra nhanh: mỗi màn hình đọc/ghi bảng nào, repository nào xử lý.

---

## 1. Danh sách bảng Database (30 bảng)

### Cấu hình hạ tầng

| Entity (C#) | Bảng (snake_case) | Cột chính | Mô tả |
|---|---|---|---|
| `Site` | `sites` | id, code, name, time_zone_id | Nhà máy |
| `Gate` | `gates` | id, site_id, code, name | Cổng ra/vào |
| `Lane` | `lanes` | id, site_id, gate_id, weighbridge_id, code | Làn — gắn kết thiết bị |
| `Weighbridge` | `weighbridges` | id, site_id, code, min/max_capacity_kg, resolution_kg | Bàn cân |
| `Device` | `devices` | id, site_id, lane_id, code, device_class, is_enabled | Thiết bị: SCALE, QR, AI, IOT, BARRIER |
| `WorkflowProfile` | `workflow_profiles` | id, code, profile_version, checkpoints, weight_role_sequence | Hồ sơ nghiệp vụ (mía/vật tư) |

### Chuyến và Cân

| Entity | Bảng | Cột chính | Mô tả |
|---|---|---|---|
| `Trip` | `trips` | id, trip_no, site_id, lifecycle, execution_status, tractor_plate | Chuyến vận chuyển |
| `CheckpointEvent` | `checkpoint_events` | id, trip_id, type, status, warehouse_id | Mốc checkpoint (cổng, cân, kho...) |
| `QrToken` | `qr_tokens` | id, token_hash, type, status, trip_id | Mã QR gắn chuyến |
| `WeighSession` | `weigh_sessions` | id, trip_id, weighbridge_id, role, segment_index, status | Phiên cân sở hữu bàn cân |
| `WeighEvent` | `weigh_events` | id, trip_id, revision, role, normalized_kg, source, is_effective | Lượt cân đã chốt (bất biến) |
| `WeighTicket` | `weigh_tickets` | id, trip_id, ticket_number, is_reprint, snapshot_json | Phiếu cân in |
| `QueueSequenceTracker` | `queue_sequence_trackers` | site_id, vehicle_type, operating_date, last_value | STT hàng chờ theo loại xe |

### Chứng từ nguồn (FRM/ERP)

| Entity | Bảng | Cột chính | Mô tả |
|---|---|---|---|
| `ExternalDocument` | `external_documents` | id, source_system, document_type, reference_no | Chứng từ nguồn PO/SO/TO |
| `TripDocumentLine` | `trip_document_lines` | id, trip_id, document_id, item_code, planned_quantity | Dòng hàng của chuyến |
| `BomSnapshot` | `bom_snapshots` | id, item_code, nominal_kg_per_unit, mass_basis | Quy cách BOM có version |

### Chất lượng

| Entity | Bảng | Cột chính | Mô tả |
|---|---|---|---|
| `QualitySample` | `quality_samples` | id, trip_id, type, impurity_percent, ccs_value, source | Mẫu tạp chất/chữ đường |
| `MeasurementSession` | `measurement_sessions` | id, trip_id, warehouse_id, iot_mass_kg, net_bag_count, status | Phiên đo IoT/đếm bao |
| `DeviceEvent` | `device_events` | id, device_id, sequence, event_type, measurement_session_id | Sự kiện thiết bị chuẩn hóa |

### Đối soát và Phân bổ

| Entity | Bảng | Cột chính | Mô tả |
|---|---|---|---|
| `Reconciliation` | `reconciliations` | id, trip_id, revision, is_effective | Phiên đối soát |
| `ReconciliationCheck` | `reconciliation_checks` | id, reconciliation_id, check_type, verdict, observed_value | Phép so sánh đối soát |
| `Allocation` | `allocations` | id, trip_id, owner_ref, allocated_kg, method, ratio | Phân bổ trọng lượng cho chủ mía |

### Tích hợp và Cổng

| Entity | Bảng | Cột chính | Mô tả |
|---|---|---|---|
| `IntegrationMessage` | `integration_messages` | id, trip_id, destination, operation, business_key, status | Sổ thông điệp FRM/ERP |
| `GateDecision` | `gate_decisions` | id, trip_id, direction, allowed, deny_reason | Quyết định cho xe qua cổng |
| `GateCommand` | `gate_commands` | id, trip_id, command_id, state, passage_confirmed_at | Lệnh mở barie |

### Quản trị và Audit

| Entity | Bảng | Cột chính | Mô tả |
|---|---|---|---|
| `Approval` | `approvals` | id, trip_id, action_type, requested_by, approved_by, reason | Phê duyệt ngoại lệ |
| `AuditLogEntry` | `audit_log_entries` | id, trip_id, action_type, actor, before_json, after_json | Nhật ký audit |
| `Incident` | `incidents` | id, trip_id, site_id, issue_code, scope, resolved_at | Sự cố và khôi phục |

---

## 2. Ánh xạ Màn hình → Bảng

### UI-01 — Tổng quan (`DashboardForm`)
| Thao tác | Bảng liên quan |
|---|---|
| **Đọc** | `trips`, `weigh_events`, `devices`, `lanes`, `integration_messages`, `incidents`, `reconciliations` |
| **Ghi** | *(không ghi — chỉ đọc)* |
| **Query** | `IDashboardQueries` → `GetOverviewAsync`, `ListTripsAsync` |

### UI-02 — Đăng tài (`TripRegistrationForm`)
| Thao tác | Bảng liên quan |
|---|---|
| **Đọc** | `trips`, `workflow_profiles`, `external_documents`, `qr_tokens`, `queue_sequence_trackers` |
| **Ghi (Đăng tài)** | `trips` ✏️, `qr_tokens` ✏️, `checkpoint_events` ✏️, `queue_sequence_trackers` ✏️ |
| **Ghi (Hủy)** | `trips` ✏️ (lifecycle→Cancelled), `qr_tokens` ✏️ (revoke), `audit_log_entries` ✏️ |
| **Ghi (In phiếu)** | `weigh_tickets` ✏️ |
| **Handler** | `RegisterTripHandler`, `CancelTripHandler`, `CheckPlanHandler` |

### UI-03 — Cân hợp nhất (`WeighOperationForm`)
| Thao tác | Bảng liên quan |
|---|---|
| **Đọc** | `trips`, `qr_tokens`, `weigh_sessions`, `weigh_events`, `devices`, `workflow_profiles`, `quality_samples`, `weighbridges` |
| **Ghi (Mở phiên)** | `weigh_sessions` ✏️ |
| **Ghi (Chốt cân)** | `weigh_events` ✏️, `trips` ✏️ (lifecycle), `audit_log_entries` ✏️ |
| **Ghi (Cân lại)** | `weigh_events` ✏️ (supersedes_id), `approvals` ✏️, `audit_log_entries` ✏️ |
| **Ghi (Nhập tay)** | `weigh_events` ✏️ (source=MANUAL), `approvals` ✏️, `audit_log_entries` ✏️ |
| **Ghi (Sync ERP)** | `integration_messages` ✏️ |
| **Ghi (In phiếu)** | `weigh_tickets` ✏️ |
| **Handler** | `OpenWeighSessionHandler`, `EvaluateAndCommitWeightHandler`, `RequestReweighHandler`, `ApproveManualWeightHandler`, `SubmitTripResultHandler` |

### UI-04 — Kho vận (`MeasurementSessionForm`)
| Thao tác | Bảng liên quan |
|---|---|
| **Đọc** | `trips`, `qr_tokens`, `checkpoint_events`, `measurement_sessions`, `device_events`, `reconciliations`, `reconciliation_checks` |
| **Ghi (Vào kho)** | `checkpoint_events` ✏️ (type=WarehouseIn), `audit_log_entries` ✏️ |
| **Ghi (Mở phiên đo)** | `measurement_sessions` ✏️ |
| **Ghi (Đóng phiên)** | `measurement_sessions` ✏️ (status, iot_mass_kg, net_bag_count) |
| **Ghi (Ra kho)** | `checkpoint_events` ✏️ (type=WarehouseOut), `audit_log_entries` ✏️ |
| **Ghi (Đối soát)** | `reconciliations` ✏️, `reconciliation_checks` ✏️ |
| **Handler** | `OpenMeasurementSessionHandler`, `CloseMeasurementSessionHandler`, `ReconcileTripHandler` |

### UI-05 — Mía và chất lượng (`QualityForm`)
| Thao tác | Bảng liên quan |
|---|---|
| **Đọc** | `trips`, `qr_tokens`, `quality_samples` |
| **Ghi (Tạp chất)** | `quality_samples` ✏️ (type=Impurity), `audit_log_entries` ✏️ |
| **Ghi (Chữ đường)** | `quality_samples` ✏️ (type=CCS), `audit_log_entries` ✏️ |
| **Handler** | `RecordImpuritySampleHandler`, `ReceiveCcsResultHandler` |

### UI-06 — Chi tiết chuyến (`TripDetailForm`)
| Thao tác | Bảng liên quan |
|---|---|
| **Đọc** | `trips`, `checkpoint_events`, `weigh_events`, `quality_samples`, `measurement_sessions`, `reconciliations`, `reconciliation_checks`, `allocations`, `integration_messages`, `audit_log_entries` |
| **Ghi (Phân bổ)** | `allocations` ✏️ |
| **Handler** | `ITripDetailQueries`, `AllocateTripHandler` |

### UI-07 — Hàng chờ ngoại lệ (`ExceptionQueueForm`)
| Thao tác | Bảng liên quan |
|---|---|
| **Đọc** | `trips`, `weigh_events`, `approvals`, `incidents`, `weighbridges` |
| **Ghi (Cân lại)** | `weigh_events` ✏️, `approvals` ✏️, `audit_log_entries` ✏️ |
| **Ghi (Nhập tay)** | `weigh_events` ✏️, `approvals` ✏️, `audit_log_entries` ✏️ |
| **Ghi (Tạm ngưng)** | `trips` ✏️ (execution_status=Paused), `incidents` ✏️ |
| **Ghi (Tiếp tục)** | `trips` ✏️ (execution_status=Active), `incidents` ✏️ |
| **Handler** | `RequestReweighHandler`, `ApproveManualWeightHandler`, `PauseResumeHandler` |

### UI-08 — Giám sát tích hợp (`IntegrationMonitorForm`)
| Thao tác | Bảng liên quan |
|---|---|
| **Đọc** | `integration_messages`, `trips` |
| **Ghi (Tạo thông điệp)** | `integration_messages` ✏️ |
| **Ghi (Kết luận UNKNOWN)** | `integration_messages` ✏️ (status→Acknowledged/Failed) |
| **Handler** | `SubmitTripResultHandler`, `ResolveUnknownMessageHandler` |

### UI-09 — Thiết bị và cấu hình (`ConfigurationForm`)
| Thao tác | Bảng liên quan |
|---|---|
| **Đọc** | `workflow_profiles`, `weighbridges`, `devices`, `lanes` |
| **Ghi** | *(không ghi — chỉ đọc)* |

### UI-10 — Báo cáo (`ReportForm`)
| Thao tác | Bảng liên quan |
|---|---|
| **Đọc** | `trips`, `weigh_events`, `quality_samples`, `reconciliations`, `integration_messages` |
| **Ghi** | *(không ghi — chỉ đọc, xuất CSV)* |
| **Query** | `IReportQueries` → `ListWeighReportAsync` |

### UI-11 — Cổng Đăng Tài (`GateControlForm`)
| Thao tác | Bảng liên quan |
|---|---|
| **Đọc** | `trips`, `qr_tokens`, `checkpoint_events`, `gate_decisions`, `gate_commands` |
| **Ghi (Cho xe qua)** | `gate_decisions` ✏️, `gate_commands` ✏️, `checkpoint_events` ✏️ (GateIn/GateOut) |
| **Ghi (Xác nhận passage)** | `gate_commands` ✏️ (passage_confirmed), `qr_tokens` ✏️ (revoke), `trips` ✏️ (lifecycle→Closed) |
| **Ghi** | `audit_log_entries` ✏️ |
| **Handler** | `EvaluateGateHandler`, `ConfirmVehiclePassageHandler` |

---

## 3. Repository → Entity → Bảng

| Repository Interface | Entity | Bảng |
|---|---|---|
| `ITripRepository` | Trip, CheckpointEvent | `trips`, `checkpoint_events` |
| `IQrTokenRepository` | QrToken | `qr_tokens` |
| `IWeighSessionRepository` | WeighSession, WeighEvent | `weigh_sessions`, `weigh_events` |
| `IWorkflowProfileRepository` | WorkflowProfile | `workflow_profiles` |
| `IWeighbridgeRepository` | Weighbridge | `weighbridges` |
| `IQualitySampleRepository` | QualitySample | `quality_samples` |
| `IMeasurementSessionRepository` | MeasurementSession | `measurement_sessions` |
| `IDeviceEventRepository` | DeviceEvent | `device_events` |
| `IReconciliationRepository` | Reconciliation, ReconciliationCheck | `reconciliations`, `reconciliation_checks` |
| `IAllocationRepository` | Allocation | `allocations` |
| `IIntegrationMessageRepository` | IntegrationMessage | `integration_messages` |
| `IGateRepository` | GateDecision, GateCommand | `gate_decisions`, `gate_commands` |
| `IExternalDocumentRepository` | ExternalDocument, TripDocumentLine | `external_documents`, `trip_document_lines` |
| `IBomSnapshotRepository` | BomSnapshot | `bom_snapshots` |
| `IApprovalRepository` | Approval | `approvals` |
| `IIncidentRepository` | Incident | `incidents` |
| `IWeighTicketRepository` | WeighTicket | `weigh_tickets` |

---

## 4. Kiểm tra nhanh bằng SQL

```sql
-- Xem chuyến mới nhất
SELECT id, trip_no, lifecycle, execution_status, tractor_plate, created_at
FROM trips ORDER BY created_at DESC LIMIT 10;

-- Xem lượt cân của chuyến
SELECT we.id, we.trip_id, we.role, we.normalized_kg, we.source, we.is_effective, we.measured_at
FROM weigh_events we WHERE we.trip_id = '...' ORDER BY we.revision;

-- Xem mẫu chất lượng
SELECT id, trip_id, type, impurity_percent, ccs_value, status, source
FROM quality_samples WHERE trip_id = '...';

-- Xem đối soát
SELECT rc.check_type, rc.verdict, rc.observed_value, rc.expected_value, rc.difference
FROM reconciliation_checks rc
JOIN reconciliations r ON r.id = rc.reconciliation_id
WHERE r.trip_id = '...' AND r.is_effective = true;

-- Xem thông điệp tích hợp
SELECT destination, operation, business_key, status, attempts, last_attempt_at
FROM integration_messages WHERE trip_id = '...';

-- Xem audit trail
SELECT occurred_at, action_type, actor, reason
FROM audit_log_entries WHERE trip_id = '...' ORDER BY occurred_at;
```
