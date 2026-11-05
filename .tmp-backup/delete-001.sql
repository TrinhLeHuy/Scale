\set ON_ERROR_STOP on
\copy (SELECT * FROM trips WHERE trip_no = '20261028-001') TO '/tmp/trip-001-before-delete.csv' WITH CSV HEADER
BEGIN;
DO $$
DECLARE n int;
BEGIN
  -- Xóa mềm (deleted_at) theo quy tắc dự án: không DELETE cứng chứng từ; lượt cân vẫn giữ để tra cứu.
  UPDATE trips SET deleted_at = now(), updated_at = now(), version = version + 1
  WHERE trip_no = '20261028-001' AND deleted_at IS NULL;
  GET DIAGNOSTICS n = ROW_COUNT;
  IF n <> 1 THEN RAISE EXCEPTION 'Expected 1 row, got %', n; END IF;
  -- QR của chuyến không còn dùng được.
  UPDATE qr_tokens SET status = 'Revoked' WHERE trip_id = (SELECT id FROM trips WHERE trip_no = '20261028-001') AND status <> 'Revoked';
END $$;
COMMIT;
SELECT trip_no, lifecycle, deleted_at FROM trips WHERE trip_no = '20261028-001';
