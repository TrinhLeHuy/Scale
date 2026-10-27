-- Reset toàn bộ DB cho test lại từ đầu
-- Chạy: docker exec -i ttc-weighing-db psql -U ttc_dev -d ttc_weighing -f /dev/stdin < reset-db.sql

-- 1. Dọn dữ liệu nghiệp vụ
TRUNCATE TABLE 
  audit_log_entries, gate_commands, gate_decisions, integration_messages,
  allocations, reconciliation_checks, reconciliations, device_events,
  measurement_sessions, quality_samples, weigh_tickets, weigh_events,
  weigh_sessions, checkpoint_events, trip_document_lines, external_documents,
  qr_tokens, approvals, incidents, trips
CASCADE;
ALTER SEQUENCE trip_no_seq RESTART WITH 1;

-- 2. Dọn FRM/ERP cache (kéo lại từ API)
TRUNCATE TABLE frm_vehicles, frm_lenh_don, erp_order_lines;

-- 3. Đảm bảo cột mới tồn tại
ALTER TABLE frm_lenh_don ADD COLUMN IF NOT EXISTS loai_xe_code text;
ALTER TABLE frm_lenh_don ADD COLUMN IF NOT EXISTS uu_tien text;
ALTER TABLE frm_lenh_don ADD COLUMN IF NOT EXISTS khdn_name text;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS frm_lenh_don_id text;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS frm_ma_lenh_don text;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS frm_lenh_don_bu text;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS frm_loai_xe_code text;
-- Số đọc CCS (Bx / Pol / Pol cải chính). Quay lui: DROP COLUMN brix_value, pol_value, pol_corrected.
ALTER TABLE quality_samples ADD COLUMN IF NOT EXISTS brix_value numeric(9,4);
ALTER TABLE quality_samples ADD COLUMN IF NOT EXISTS pol_value numeric(9,4);
ALTER TABLE quality_samples ADD COLUMN IF NOT EXISTS pol_corrected numeric(9,4);

-- 4. Unique indexes cho UPSERT
CREATE UNIQUE INDEX IF NOT EXISTS uq_frm_lenh_don_ma_ld_bu ON frm_lenh_don (ma_ld, COALESCE(business_unit, ''));
CREATE UNIQUE INDEX IF NOT EXISTS uq_frm_vehicles_bien_so_ma_the ON frm_vehicles (bien_so, COALESCE(ma_the, ''));
-- ERP: khóa theo ID bản ghi DigiScale (giữ riêng từng dòng và từng nguồn Oracle / WMS).
-- Quay lui: DROP INDEX uq_erp_order_lines_storage_key; tạo lại uq_erp_order_lines_shipment_bu (shipment_number, business_unit).
ALTER TABLE erp_order_lines ADD COLUMN IF NOT EXISTS api_id text;
ALTER TABLE erp_order_lines ADD COLUMN IF NOT EXISTS record_type text;
ALTER TABLE erp_order_lines ADD COLUMN IF NOT EXISTS shipment_line_number text;
ALTER TABLE erp_order_lines ADD COLUMN IF NOT EXISTS storage_key text;
DROP INDEX IF EXISTS uq_erp_order_lines_shipment_bu;
CREATE UNIQUE INDEX IF NOT EXISTS uq_erp_order_lines_storage_key ON erp_order_lines (storage_key) WHERE storage_key IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_erp_order_lines_shipment_number ON erp_order_lines (shipment_number);
CREATE INDEX IF NOT EXISTS idx_frm_lenh_don_business_unit ON frm_lenh_don (business_unit);
CREATE INDEX IF NOT EXISTS idx_erp_order_lines_business_unit ON erp_order_lines (business_unit);
