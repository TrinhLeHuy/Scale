\set ON_ERROR_STOP on
BEGIN;
DO $$
DECLARE n int;
BEGIN
  UPDATE trips
  SET lifecycle = 'Registered',
      reference_trip_no = 'LD2526-00014683',
      updated_at = now(),
      version = version + 1
  WHERE trip_no = '20261028-004'
    AND frm_lenh_don_id = '24506c55-b5ce-f111-b810-005056b45691'
    AND deleted_at IS NULL;
  GET DIAGNOSTICS n = ROW_COUNT;
  IF n <> 1 THEN
    RAISE EXCEPTION 'Expected 1 row, got %', n;
  END IF;
END $$;
COMMIT;
SELECT trip_no, tractor_plate, lifecycle, reference_trip_no, frm_lenh_don_id, version FROM trips WHERE trip_no = '20261028-004';
