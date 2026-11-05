-- Giới hạn số cân theo lượt cho từng bàn cân (Vào min/Max, Ra min/Max), đơn vị kg. NULL = không giới hạn.
-- Chỉ thêm cột NULL, không đổi dữ liệu có sẵn.
-- Quay lui:
--   ALTER TABLE public.weighbridges DROP COLUMN IF EXISTS first_weight_min_kg;
--   ALTER TABLE public.weighbridges DROP COLUMN IF EXISTS first_weight_max_kg;
--   ALTER TABLE public.weighbridges DROP COLUMN IF EXISTS second_weight_min_kg;
--   ALTER TABLE public.weighbridges DROP COLUMN IF EXISTS second_weight_max_kg;

ALTER TABLE public.weighbridges ADD COLUMN IF NOT EXISTS first_weight_min_kg numeric(15,3) NULL;
ALTER TABLE public.weighbridges ADD COLUMN IF NOT EXISTS first_weight_max_kg numeric(15,3) NULL;
ALTER TABLE public.weighbridges ADD COLUMN IF NOT EXISTS second_weight_min_kg numeric(15,3) NULL;
ALTER TABLE public.weighbridges ADD COLUMN IF NOT EXISTS second_weight_max_kg numeric(15,3) NULL;
