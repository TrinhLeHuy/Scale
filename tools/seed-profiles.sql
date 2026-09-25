-- Seed workflow profiles + infrastructure cho DB trống
INSERT INTO workflow_profiles (id, code, name, profile_version, source_system, direction,
  checkpoints, weight_role_sequence, gate_checkpoints_required, impurity_required_before_second_weight,
  warehouse_confirmation_required, bom_snapshot_required, thresholds,
  effective_from, approved_by, approved_at, created_at, created_by, version)
VALUES (
  gen_random_uuid(), 'CANE_IN', 'Sugarcane Inbound', 1, 'FRM', 'In',
  '[0,1,2,4,3,5,10]'::jsonb, '[0,1]'::jsonb, true, true, false, false,
  '{"StabilityWindow":"00:00:05","StabilityDeltaKg":0.5,"ScaleSignalMaxAge":"00:00:02","AiEvidenceMaxAge":"00:00:05","PositionConfidenceThreshold":0.7}'::jsonb,
  now() - interval '1 year', 'system', now() - interval '1 year',
  now(), 'db-seed', 1
);

INSERT INTO workflow_profiles (id, code, name, profile_version, source_system, direction,
  checkpoints, weight_role_sequence, gate_checkpoints_required, impurity_required_before_second_weight,
  warehouse_confirmation_required, bom_snapshot_required, thresholds,
  effective_from, approved_by, approved_at, created_at, created_by, version)
VALUES (
  gen_random_uuid(), 'MATERIAL_OUT_BAGGED', 'Material Outbound (Bagged)', 1, 'ERP', 'Out',
  '[0,1,2,3,10]'::jsonb, '[0,1]'::jsonb, true, false, true, true,
  '{"StabilityWindow":"00:00:05","StabilityDeltaKg":0.5,"ScaleSignalMaxAge":"00:00:02","AiEvidenceMaxAge":"00:00:05","PositionConfidenceThreshold":0.7}'::jsonb,
  now() - interval '1 year', 'system', now() - interval '1 year',
  now(), 'db-seed', 1
);

SELECT code, name, profile_version, direction FROM workflow_profiles;
SELECT code, name FROM gates;
SELECT code FROM weighbridges;
SELECT code FROM lanes;
SELECT count(*) AS device_count FROM devices;
