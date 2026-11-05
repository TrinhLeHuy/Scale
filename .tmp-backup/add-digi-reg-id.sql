BEGIN;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS digi_registration_id varchar(100);
UPDATE trips t SET digi_registration_id = x.id
FROM (
  SELECT DISTINCT ON (it->>'key') it->>'key' AS trip_no, it->>'id' AS id
  FROM api_call_logs l,
       jsonb_array_elements(CASE WHEN l.response_body ~ '^\s*\{' THEN (l.response_body::jsonb)->'items' ELSE '[]'::jsonb END) it
  WHERE l.request_body ILIKE '%new_phieudangtai%'
    AND l.request_body ILIKE '%"status":"Create"%'
    AND (it->>'saved') = 'true' AND coalesce(it->>'id','') <> ''
  ORDER BY it->>'key', l.created_at DESC
) x
WHERE t.trip_no = x.trip_no AND t.digi_registration_id IS NULL;
SELECT count(*) AS filled FROM trips WHERE digi_registration_id IS NOT NULL;
COMMIT;
