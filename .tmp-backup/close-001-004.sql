\set ON_ERROR_STOP on
-- Sao lưu trạng thái trước khi sửa
\copy (SELECT * FROM trips WHERE trip_no IN ('20261028-001','20261028-004')) TO '/tmp/trips-001-004-before.csv' WITH CSV HEADER
BEGIN;
DO $$
DECLARE n int;
BEGIN
  UPDATE trips
  SET lifecycle = 'Closed', updated_at = now(), version = version + 1
  WHERE trip_no IN ('20261028-001','20261028-004')
    AND deleted_at IS NULL
    AND lifecycle IN ('Registered','InProgress');
  GET DIAGNOSTICS n = ROW_COUNT;
  IF n <> 2 THEN
    RAISE EXCEPTION 'Expected 2 rows, got %', n;
  END IF;
END $$;
COMMIT;
SELECT trip_no, tractor_plate, lifecycle, reference_trip_no, version FROM trips
WHERE trip_no IN ('20261028-001','20261028-004') ORDER BY trip_no;
