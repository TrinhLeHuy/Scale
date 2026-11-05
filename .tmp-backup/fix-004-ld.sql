\set ON_ERROR_STOP on
\copy (SELECT * FROM trips WHERE trip_no = '20261028-004') TO '/tmp/trip-004-before-ld-fix.csv' WITH CSV HEADER
BEGIN;
DO $$
DECLARE n int;
BEGIN
  -- Chuyến 004 cân thật theo LD2526-00014676 (ID lệnh đốn 24506c55..., 2 phiếu cân đã đẩy DigiScale với LD này).
  UPDATE trips
  SET reference_trip_no = 'LD2526-00014676', updated_at = now(), version = version + 1
  WHERE trip_no = '20261028-004'
    AND frm_lenh_don_id = '24506c55-b5ce-f111-b810-005056b45691'
    AND reference_trip_no = 'LD2526-00014683'
    AND deleted_at IS NULL;
  GET DIAGNOSTICS n = ROW_COUNT;
  IF n <> 1 THEN RAISE EXCEPTION 'Expected 1 row, got %', n; END IF;
END $$;
COMMIT;
SELECT trip_no, tractor_plate, lifecycle, reference_trip_no, version FROM trips WHERE trip_no = '20261028-004';
SELECT count(*) AS trips_using_14683 FROM trips
WHERE (reference_trip_no = 'LD2526-00014683' OR frm_lenh_don_id = '32506c55-b5ce-f111-b810-005056b45691')
  AND deleted_at IS NULL AND lifecycle <> 'Cancelled';
