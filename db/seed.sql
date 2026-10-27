--
-- PostgreSQL database dump
--

-- Dumped from database version 16.4
-- Dumped by pg_dump version 16.4

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Data for Name: __EFMigrationsHistory; Type: TABLE DATA; Schema: public; Owner: -
--

SET SESSION AUTHORIZATION DEFAULT;

ALTER TABLE public."__EFMigrationsHistory" DISABLE TRIGGER ALL;

-- Khớp 5 migration hiện có trong code; schema.sql đã ở phiên bản mới nhất.
INSERT INTO public."__EFMigrationsHistory" (migration_id, product_version) VALUES ('20260911105802_InitialWeighingSchema', '8.0.11');
INSERT INTO public."__EFMigrationsHistory" (migration_id, product_version) VALUES ('20260918013337_AddOrganizationUnitCode', '8.0.11');
INSERT INTO public."__EFMigrationsHistory" (migration_id, product_version) VALUES ('20260928082604_RemoveUnusedTables', '8.0.11');
INSERT INTO public."__EFMigrationsHistory" (migration_id, product_version) VALUES ('20261026120000_AddTripTransporter', '8.0.11');
INSERT INTO public."__EFMigrationsHistory" (migration_id, product_version) VALUES ('20261026150000_AddCcsReadings', '8.0.11');


ALTER TABLE public."__EFMigrationsHistory" ENABLE TRIGGER ALL;

--
-- Data for Name: devices; Type: TABLE DATA; Schema: public; Owner: -
--

ALTER TABLE public.devices DISABLE TRIGGER ALL;

INSERT INTO public.devices (id, site_id, lane_id, code, device_class, mapping_version, is_enabled, is_test_device, created_at, created_by, updated_at, updated_by, deleted_at, version) VALUES ('9ea51c20-f7f4-4a17-91ae-84997429dd37', 'bddcbe10-6f69-4545-9f0b-02b3f4a06369', '61a6191e-01fd-44ac-9c8e-2be7d83ad6f9', 'SCALE-01', 'SCALE', 'scenario-0.1', true, false, '2026-09-17 09:50:58.256588+00', 'seeder', NULL, NULL, NULL, 0);
INSERT INTO public.devices (id, site_id, lane_id, code, device_class, mapping_version, is_enabled, is_test_device, created_at, created_by, updated_at, updated_by, deleted_at, version) VALUES ('8d05316e-d977-4616-8fc0-2bc1d87af31c', 'bddcbe10-6f69-4545-9f0b-02b3f4a06369', '61a6191e-01fd-44ac-9c8e-2be7d83ad6f9', 'CAM-POS-01', 'POSITION_AI', 'scenario-0.1', true, false, '2026-09-17 09:50:58.256588+00', 'seeder', NULL, NULL, NULL, 0);
INSERT INTO public.devices (id, site_id, lane_id, code, device_class, mapping_version, is_enabled, is_test_device, created_at, created_by, updated_at, updated_by, deleted_at, version) VALUES ('61f05ec6-ee12-46d5-8d8b-41b9800cefdd', 'bddcbe10-6f69-4545-9f0b-02b3f4a06369', '61a6191e-01fd-44ac-9c8e-2be7d83ad6f9', 'QR-READER-01', 'QR', 'scenario-0.1', true, false, '2026-09-17 09:50:58.256588+00', 'seeder', NULL, NULL, NULL, 0);
INSERT INTO public.devices (id, site_id, lane_id, code, device_class, mapping_version, is_enabled, is_test_device, created_at, created_by, updated_at, updated_by, deleted_at, version) VALUES ('e1eeb623-a5df-4d59-b5c7-43f26f374596', 'bddcbe10-6f69-4545-9f0b-02b3f4a06369', '61a6191e-01fd-44ac-9c8e-2be7d83ad6f9', 'BARRIER-01', 'BARRIER', 'scenario-0.1', true, false, '2026-09-17 09:50:58.256588+00', 'seeder', NULL, NULL, NULL, 0);
INSERT INTO public.devices (id, site_id, lane_id, code, device_class, mapping_version, is_enabled, is_test_device, created_at, created_by, updated_at, updated_by, deleted_at, version) VALUES ('e57cf5a3-7ce7-4b38-808e-39dd0db0a2f5', 'bddcbe10-6f69-4545-9f0b-02b3f4a06369', '61a6191e-01fd-44ac-9c8e-2be7d83ad6f9', 'IOT-MASS-01', 'IOT_MASS', 'scenario-0.1', true, false, '2026-09-17 09:50:58.256588+00', 'seeder', NULL, NULL, NULL, 0);
INSERT INTO public.devices (id, site_id, lane_id, code, device_class, mapping_version, is_enabled, is_test_device, created_at, created_by, updated_at, updated_by, deleted_at, version) VALUES ('d19d01c3-01e0-4dbb-8eeb-30ac07585814', 'bddcbe10-6f69-4545-9f0b-02b3f4a06369', '61a6191e-01fd-44ac-9c8e-2be7d83ad6f9', 'CAM-BAG-01', 'BAG_AI', 'scenario-0.1', true, false, '2026-09-17 09:50:58.256588+00', 'seeder', NULL, NULL, NULL, 0);


ALTER TABLE public.devices ENABLE TRIGGER ALL;

--
-- Data for Name: gates; Type: TABLE DATA; Schema: public; Owner: -
--

ALTER TABLE public.gates DISABLE TRIGGER ALL;



ALTER TABLE public.gates ENABLE TRIGGER ALL;

--
-- Data for Name: lanes; Type: TABLE DATA; Schema: public; Owner: -
--

ALTER TABLE public.lanes DISABLE TRIGGER ALL;

INSERT INTO public.lanes (id, site_id, gate_id, weighbridge_id, code, created_at, created_by, updated_at, updated_by, deleted_at, version) VALUES ('61a6191e-01fd-44ac-9c8e-2be7d83ad6f9', 'bddcbe10-6f69-4545-9f0b-02b3f4a06369', '4d894728-9cc9-4384-8a39-18475f33ff9f', 'c106ae55-b798-46b2-a3e1-5f6f00d8aecf', 'LAN-01', '2026-09-17 09:50:58.256588+00', 'seeder', NULL, NULL, NULL, 0);


ALTER TABLE public.lanes ENABLE TRIGGER ALL;

--
-- Data for Name: sites; Type: TABLE DATA; Schema: public; Owner: -
--

ALTER TABLE public.sites DISABLE TRIGGER ALL;

INSERT INTO public.sites (id, code, name, time_zone_id, created_at, created_by, updated_at, updated_by, deleted_at, version) VALUES ('bddcbe10-6f69-4545-9f0b-02b3f4a06369', 'TTC-01', 'Nhà máy TTC-01', 'SE Asia Standard Time', '2026-09-17 09:07:36.669601+00', 'auto-bootstrap', NULL, NULL, NULL, 0);


ALTER TABLE public.sites ENABLE TRIGGER ALL;

--
-- Data for Name: weighbridges; Type: TABLE DATA; Schema: public; Owner: -
--

ALTER TABLE public.weighbridges DISABLE TRIGGER ALL;

INSERT INTO public.weighbridges (id, site_id, code, min_capacity_kg, max_capacity_kg, resolution_kg, is_out_of_service, created_at, created_by, updated_at, updated_by, deleted_at, version) VALUES ('c106ae55-b798-46b2-a3e1-5f6f00d8aecf', 'bddcbe10-6f69-4545-9f0b-02b3f4a06369', 'BAN-CAN-01', 500.000, 80000.000, 10.000, false, '2026-09-17 09:50:58.256588+00', 'seeder', NULL, NULL, NULL, 0);
INSERT INTO public.weighbridges (id, site_id, code, min_capacity_kg, max_capacity_kg, resolution_kg, is_out_of_service, created_at, created_by, updated_at, updated_by, deleted_at, version) VALUES ('608e11ef-a9e7-4a4a-ba15-1669250565ae', 'bddcbe10-6f69-4545-9f0b-02b3f4a06369', 'BAN-CAN-02', 500.000, 80000.000, 10.000, false, '2026-09-17 09:50:58.256588+00', 'seeder', NULL, NULL, NULL, 0);


ALTER TABLE public.weighbridges ENABLE TRIGGER ALL;

--
-- Data for Name: workflow_profiles; Type: TABLE DATA; Schema: public; Owner: -
--

ALTER TABLE public.workflow_profiles DISABLE TRIGGER ALL;

INSERT INTO public.workflow_profiles (id, code, name, profile_version, source_system, direction, checkpoints, weight_role_sequence, gate_checkpoints_required, impurity_required_before_second_weight, warehouse_confirmation_required, bom_snapshot_required, effective_from, effective_to, approved_by, approved_at, created_at, created_by, updated_at, updated_by, deleted_at, version, thresholds) VALUES ('9053bd8c-0ff3-474e-bc2f-4563a414e9e2', 'CANE_IN', 'Nhập mía', 1, 'FRM', 'Inbound', '[0, 1, 2, 4, 5, 3, 8, 9, 10]', '[0, 1]', true, true, false, false, '2025-09-17 09:50:58.256588+00', NULL, 'SCENARIO_ONLY', '2025-09-17 09:50:58.256588+00', '2026-09-17 09:50:58.256588+00', 'seeder', NULL, NULL, NULL, 0, '{"StabilityWindow": "00:00:05", "AiEvidenceMaxAge": "00:00:03", "StabilityDeltaKg": 1, "ScaleSignalMaxAge": "00:00:02", "PositionConfidenceThreshold": 0.90}');
INSERT INTO public.workflow_profiles (id, code, name, profile_version, source_system, direction, checkpoints, weight_role_sequence, gate_checkpoints_required, impurity_required_before_second_weight, warehouse_confirmation_required, bom_snapshot_required, effective_from, effective_to, approved_by, approved_at, created_at, created_by, updated_at, updated_by, deleted_at, version, thresholds) VALUES ('7bbf114a-45bc-432f-8724-3c4625adc539', 'MATERIAL_OUT_BAGGED', 'Xuất vật tư hàng bao', 1, 'ERP', 'Outbound', '[0, 1, 2, 7, 3, 8, 9, 10]', '[1, 0]', true, false, true, true, '2025-09-17 09:50:58.256588+00', NULL, 'SCENARIO_ONLY', '2025-09-17 09:50:58.256588+00', '2026-09-17 09:50:58.256588+00', 'seeder', NULL, NULL, NULL, 0, '{"StabilityWindow": "00:00:05", "AiEvidenceMaxAge": "00:00:03", "StabilityDeltaKg": 1, "ScaleSignalMaxAge": "00:00:02", "PositionConfidenceThreshold": 0.90}');


ALTER TABLE public.workflow_profiles ENABLE TRIGGER ALL;

--
-- PostgreSQL database dump complete
--

