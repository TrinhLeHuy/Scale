# Danh sách bảng Database — TTC Weighing

## Bảng đang dùng (có data hoặc sẽ dùng)

| Bảng | Tên gợi nhớ | Mô tả | Records |
|------|-------------|-------|---------|
| `sites` | Nhà máy | Cấu hình nhà máy (1 site = 1 nhà máy) | 1 |
| `lanes` | Làn cân | Làn gắn kết thiết bị | 1 |
| `devices` | Thiết bị | Đầu cân, camera, barcode... | 6 |
| `weighbridges` | Bàn cân | Bàn cân vật lý (min/max capacity) | 2 |
| `workflow_profiles` | Hồ sơ nghiệp vụ | Mía nhập / Vật tư | 2 |
| `trips` | Chuyến đăng tài | Mỗi xe đăng tài = 1 trip | 0 |
| `qr_tokens` | Mã QR | QR gắn với chuyến | 0 |
| `weigh_sessions` | Phiên cân | Khóa bàn cân cho 1 lượt | 0 |
| `weigh_events` | Lượt cân | Số cân đã chốt (bất biến) | 0 |
| `weigh_tickets` | Phiếu cân | Phiếu in | 0 |
| `quality_samples` | Mẫu chất lượng | Tạp chất + CCS | 0 |
| `queue_sequence_tracker` | STT hàng chờ | Số thứ tự theo ngày/loại xe | 0 |
| `audit_log_entries` | Nhật ký audit | Mọi thao tác đều ghi log | 0 |
| `integration_messages` | Thông điệp tích hợp | Push lên DigiScale/FRM | 0 |
| `checkpoint_events` | Mốc checkpoint | Đăng tài, cân vào, tạp chất... | 0 |

## Bảng dự phòng (chưa dùng, giữ cho tương lai)

| Bảng | Tên gợi nhớ | Mô tả |
|------|-------------|-------|
| `reconciliations` | Đối soát | So sánh cân xe vs IoT vs kế hoạch |
| `reconciliation_checks` | Phép đối soát | Từng phép so sánh |
| `allocations` | Phân bổ | Chia lượng cho nhiều chủ mía |
| `external_documents` | Chứng từ nguồn | PO/SO từ FRM/ERP |
| `trip_document_lines` | Dòng chứng từ | Chi tiết dòng hàng |
| `bom_snapshots` | Quy cách BOM | Khối lượng danh nghĩa |
| `device_events` | Sự kiện thiết bị | IoT readings |
| `approvals` | Phê duyệt | Cân lại, nhập tay |
| `incidents` | Sự cố | Tạm ngưng/khôi phục |

## Bảng có thể bỏ (UI đã xóa)

| Bảng | Lý do |
|------|-------|
| `gates` | Đã bỏ UI Cổng |
| `gate_commands` | Đã bỏ UI Cổng |
| `gate_decisions` | Đã bỏ UI Cổng |
| `measurement_sessions` | Đã bỏ UI Kho vận |

> ⚠ **Không nên xóa bảng** vì EF migration tham chiếu. Để trống là OK.

## Sequences

| Sequence | Mô tả |
|----------|-------|
| `trip_no_seq` | Số đăng tài unique toàn cục |
