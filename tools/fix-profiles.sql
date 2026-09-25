UPDATE workflow_profiles SET 
  checkpoints = '["Registration","GateIn","FirstWeight","ImpuritySample","SecondWeight","WarehouseIn","GateOut"]'::jsonb,
  weight_role_sequence = '["Gross","Tare"]'::jsonb
WHERE code = 'CANE_IN';

UPDATE workflow_profiles SET 
  checkpoints = '["Registration","GateIn","FirstWeight","SecondWeight","GateOut"]'::jsonb,
  weight_role_sequence = '["Gross","Tare"]'::jsonb
WHERE code = 'MATERIAL_OUT_BAGGED';

SELECT code, checkpoints, weight_role_sequence, direction FROM workflow_profiles;
