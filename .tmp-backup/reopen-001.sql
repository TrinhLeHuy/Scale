\set ON_ERROR_STOP on
BEGIN;
DO $$
DECLARE n int;
BEGIN
  -- Chuyến 001 chỉ có cân vào (40.080 kg), chưa có cân ra → trả về InProgress để cân ra được.
  UPDATE trips SET lifecycle = 'InProgress', updated_at = now(), version = version + 1
  WHERE trip_no = '20261028-001' AND lifecycle = 'Closed' AND deleted_at IS NULL
    AND NOT EXISTS (SELECT 1 FROM weigh_sessions s JOIN weigh_events e ON e.weigh_session_id = s.id
                    WHERE s.trip_id = trips.id AND s.segment_index >= 1 AND e.is_effective);
  GET DIAGNOSTICS n = ROW_COUNT;
  IF n <> 1 THEN RAISE EXCEPTION 'Expected 1 row, got %', n; END IF;
END $$;
COMMIT;
SELECT trip_no, lifecycle, version FROM trips WHERE trip_no = '20261028-001';
