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

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: __EFMigrationsHistory; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL
);


--
-- Name: allocations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.allocations (
    id uuid NOT NULL,
    trip_id uuid,
    measured_total_ref uuid,
    document_line_id uuid,
    owner_ref text,
    allocated_kg numeric,
    method text,
    ratio numeric,
    rounding_adjustment_kg numeric,
    approval_ref text,
    is_approved boolean DEFAULT false,
    created_at timestamp with time zone,
    created_by text,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer DEFAULT 1
);


--
-- Name: api_call_logs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.api_call_logs (
    id uuid NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    direction character varying(10) NOT NULL,
    system character varying(20) NOT NULL,
    operation character varying(120) NOT NULL,
    business_key character varying(120),
    is_policy boolean DEFAULT false NOT NULL,
    method character varying(10) NOT NULL,
    url text NOT NULL,
    http_status integer,
    success boolean NOT NULL,
    duration_ms integer NOT NULL,
    request_body text,
    response_body text,
    error text,
    actor text,
    client_name character varying(80),
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    organization_unit character varying(30)
);


--
-- Name: approvals; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.approvals (
    id uuid NOT NULL,
    trip_id uuid,
    action_type text,
    requested_by text,
    approved_by text,
    approved_at timestamp with time zone,
    reason text,
    document_ref text,
    created_at timestamp with time zone,
    created_by text,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer DEFAULT 1
);


--
-- Name: audit_log_entries; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.audit_log_entries (
    id uuid NOT NULL,
    action_type character varying(60) NOT NULL,
    trip_id uuid,
    actor character varying(100) NOT NULL,
    before_json jsonb,
    after_json jsonb,
    reason character varying(1000),
    correlation_id character varying(100) NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by text NOT NULL,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer NOT NULL
);


--
-- Name: bom_snapshots; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.bom_snapshots (
    id uuid NOT NULL,
    source_system text,
    item_code text,
    source_version text,
    effective_at timestamp with time zone,
    uom text,
    nominal_kg_per_unit numeric,
    min_kg_per_unit numeric,
    max_kg_per_unit numeric,
    packaging_tare_kg numeric,
    pallet_tare_kg numeric,
    mass_basis text,
    snapshot_hash text,
    created_at timestamp with time zone,
    created_by text,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer DEFAULT 1
);


--
-- Name: checkpoint_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.checkpoint_events (
    id uuid NOT NULL,
    trip_id uuid NOT NULL,
    type character varying(30) NOT NULL,
    status character varying(20) NOT NULL,
    warehouse_id uuid,
    lane_id uuid,
    started_at timestamp with time zone,
    completed_at timestamp with time zone,
    confirmed_by text,
    waiver_approval_ref character varying(100),
    waiver_reason character varying(500),
    created_at timestamp with time zone NOT NULL,
    created_by text NOT NULL,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer NOT NULL
);


--
-- Name: device_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.device_events (
    id uuid NOT NULL,
    device_id uuid,
    sequence integer,
    event_type text,
    measurement_session_id uuid,
    payload_json text,
    processing_status text,
    occurred_at timestamp with time zone,
    created_at timestamp with time zone,
    created_by text,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer DEFAULT 1,
    event_id text DEFAULT ''::text,
    boot_session_id text DEFAULT ''::text,
    site_id uuid,
    lane_id uuid,
    received_at timestamp with time zone DEFAULT now(),
    correlation_id text DEFAULT ''::text,
    trip_id uuid,
    payload_hash text DEFAULT ''::text,
    processing_note text
);


--
-- Name: devices; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.devices (
    id uuid NOT NULL,
    site_id uuid NOT NULL,
    lane_id uuid,
    code character varying(50) NOT NULL,
    device_class character varying(30) NOT NULL,
    mapping_version character varying(30),
    is_enabled boolean NOT NULL,
    is_test_device boolean NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by text NOT NULL,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer NOT NULL
);


--
-- Name: erp_order_lines; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.erp_order_lines (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    shipment_number text,
    trip_number text,
    plate_number text,
    transaction_type_name text,
    transaction_type_code text,
    item_number text,
    item_description text,
    organization_code text,
    vendor_name text,
    customer_name text,
    transporter text,
    driver_name text,
    total_quantity numeric(18,3),
    remaining_quantity numeric(18,3),
    gross_weight numeric(15,3),
    transaction_date text,
    business_unit text,
    synced_at timestamp with time zone DEFAULT now() NOT NULL,
    reference_number text,
    action_code text,
    source_system text,
    created_by text,
    truck_owner text,
    vendor_number text,
    vendor_site_code text,
    organization_name text,
    sub_inventory_code text,
    api_id text,
    record_type text,
    shipment_line_number text,
    storage_key text
);


--
-- Name: external_documents; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.external_documents (
    id uuid NOT NULL,
    source_system text,
    organization_code text,
    business_unit text,
    document_type text,
    reference_no text,
    shipment_no text,
    source_version text,
    source_hash text,
    status text,
    raw_snapshot_ref text,
    created_at timestamp with time zone,
    created_by text,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer DEFAULT 1
);


--
-- Name: frm_lenh_don; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.frm_lenh_don (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    ma_ld text NOT NULL,
    bien_so_xe text,
    ngay_cap text,
    ngay_het_han text,
    status_code_name text,
    kh_name text,
    loai_mia_name text,
    giong_mia_name text,
    tram_name text,
    vu_dau_tu_name text,
    loai_xe_name text,
    tai_trong numeric(15,3),
    doi_tac_thu_hoach_name text,
    thua_dat_canh_tac_name text,
    the_xe_name text,
    hop_dong_dau_tu_mia_name text,
    source_created_date text,
    synced_at timestamp with time zone DEFAULT now() NOT NULL,
    source_key text,
    business_unit text,
    loai_xe_code text,
    uu_tien text,
    khdn_name text,
    mia_chay boolean,
    loai_mia_chay_name text,
    thu_hoach_may boolean,
    the_xe_tam boolean,
    so_xe_tam text,
    loai_hop_dong_name text,
    doi_tac_boc_xep_name text,
    doi_tac_thu_hoach_dn_name text,
    ten_nha_may text
);


--
-- Name: frm_vehicles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.frm_vehicles (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    bien_so text NOT NULL,
    ma_xe text,
    loai_xe text,
    loai_xe_name text,
    tai_trong numeric(15,3),
    trong_tai_toi_da numeric(15,3),
    ma_the text,
    tram_name text,
    hop_dong_van_chuyen_name text,
    loai_the_xe_name text,
    status_name text,
    thu_hoach_may boolean,
    ngay_ap_dung_tu text,
    ngay_ap_dung_den text,
    doi_tac_van_chuyen_name text,
    vu_dau_tu_name text,
    ma_hop_dong text,
    source_created_date text,
    synced_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: gate_commands; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.gate_commands (
    id uuid NOT NULL,
    gate_decision_id uuid,
    trip_id uuid,
    command_id text,
    issued_at timestamp with time zone,
    expires_at timestamp with time zone,
    state text,
    controller_feedback text,
    passage_event_ref text,
    passage_confirmed_at timestamp with time zone,
    passage_confirmed_by text,
    created_at timestamp with time zone,
    created_by text,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer DEFAULT 1
);


--
-- Name: gate_decisions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.gate_decisions (
    id uuid NOT NULL,
    trip_id uuid,
    lane_id uuid,
    gate_id uuid,
    direction text,
    identity_evidence text,
    identity_verified_by text,
    conditions_json text DEFAULT '{}'::text,
    allowed boolean,
    deny_reason text,
    decided_at timestamp with time zone,
    created_at timestamp with time zone,
    created_by text,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer DEFAULT 1
);


--
-- Name: gates; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.gates (
    id uuid NOT NULL,
    site_id uuid,
    code text,
    name text,
    created_at timestamp with time zone,
    created_by text,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer DEFAULT 1
);


--
-- Name: incidents; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.incidents (
    id uuid NOT NULL,
    trip_id uuid,
    site_id uuid,
    issue_code text,
    scope text,
    description text,
    opened_at timestamp with time zone,
    opened_by text,
    resolved_at timestamp with time zone,
    resolved_by text,
    unknown_transactions_resolved boolean DEFAULT false,
    created_at timestamp with time zone,
    created_by text,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer DEFAULT 1
);


--
-- Name: integration_messages; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.integration_messages (
    id uuid NOT NULL,
    trip_id uuid,
    trip_revision integer,
    destination text,
    operation text,
    business_key text,
    shipment_no text,
    payload_ref text,
    payload_hash text,
    interface_reference text,
    status text,
    attempts integer DEFAULT 0,
    last_attempt_at timestamp with time zone,
    masked_response text,
    business_error_code text,
    business_error_message text,
    external_document_ref text,
    superseded_by_id uuid,
    auto_retry_allowed boolean DEFAULT false,
    created_at timestamp with time zone,
    created_by text,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer DEFAULT 1
);


--
-- Name: lanes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.lanes (
    id uuid NOT NULL,
    site_id uuid NOT NULL,
    gate_id uuid,
    weighbridge_id uuid,
    code character varying(30) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by text NOT NULL,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer NOT NULL
);


--
-- Name: measurement_sessions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.measurement_sessions (
    id uuid NOT NULL,
    trip_id uuid,
    warehouse_id uuid,
    conveyor_lane_id uuid,
    item_code text,
    document_line_id uuid,
    device_ids text,
    mass_basis text,
    reading_mode text,
    counter_epoch text,
    counter_start numeric,
    status text,
    opened_at timestamp with time zone,
    closed_at timestamp with time zone,
    iot_mass_kg numeric,
    net_bag_count integer,
    forward_bag_count integer,
    reverse_bag_count integer,
    sequence_watermark integer,
    data_complete boolean,
    hold_reason text,
    iot_mass_origin text,
    created_at timestamp with time zone,
    created_by text,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer DEFAULT 1,
    counter_end numeric(18,4)
);


--
-- Name: qr_tokens; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.qr_tokens (
    id uuid NOT NULL,
    token_hash character varying(128) NOT NULL,
    type character varying(20) NOT NULL,
    status character varying(20) NOT NULL,
    trip_id uuid,
    vehicle_id uuid,
    site_id uuid NOT NULL,
    issued_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone,
    revoked_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    created_by text NOT NULL,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer NOT NULL,
    encoded_content text
);


--
-- Name: quality_samples; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.quality_samples (
    id uuid NOT NULL,
    trip_id uuid NOT NULL,
    sample_qr_ref character varying(100),
    type character varying(20) NOT NULL,
    status character varying(20) NOT NULL,
    revision integer NOT NULL,
    supersedes_id uuid,
    is_effective boolean NOT NULL,
    sample_before_kg numeric(15,3),
    sample_after_kg numeric(15,3),
    impurity_percent numeric(9,4),
    ccs_value numeric(9,4),
    source character varying(20) NOT NULL,
    received_at timestamp with time zone,
    received_by text,
    below_minimum_sample_warning boolean NOT NULL,
    approval_ref character varying(100),
    reason character varying(500),
    created_at timestamp with time zone NOT NULL,
    created_by text NOT NULL,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer NOT NULL,
    brix_value numeric(9,4),
    pol_value numeric(9,4),
    pol_corrected numeric(9,4)
);


--
-- Name: queue_sequence_tracker; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.queue_sequence_tracker (
    site_id uuid NOT NULL,
    vehicle_type character varying(20) NOT NULL,
    operating_date date NOT NULL,
    last_value integer DEFAULT 0 NOT NULL
);


--
-- Name: reconciliation_checks; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.reconciliation_checks (
    id uuid NOT NULL,
    reconciliation_id uuid,
    check_type text,
    observed_value numeric,
    expected_value numeric,
    lower_bound numeric,
    upper_bound numeric,
    difference numeric,
    unit text,
    tolerance_policy_id uuid,
    tolerance_policy_version integer,
    verdict text,
    detail text,
    created_at timestamp with time zone,
    created_by text,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer DEFAULT 1
);


--
-- Name: reconciliations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.reconciliations (
    id uuid NOT NULL,
    trip_id uuid,
    revision integer,
    is_effective boolean,
    input_versions_json text,
    evaluated_at timestamp with time zone,
    created_at timestamp with time zone,
    created_by text,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer DEFAULT 1,
    resolved_by character varying(100)
);


--
-- Name: sites; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.sites (
    id uuid NOT NULL,
    code character varying(20) NOT NULL,
    name character varying(200) NOT NULL,
    time_zone_id character varying(60) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by text NOT NULL,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer NOT NULL
);


--
-- Name: trip_document_lines; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.trip_document_lines (
    id uuid NOT NULL,
    trip_id uuid,
    document_id uuid,
    source_line_id text,
    item_code text,
    warehouse_id uuid,
    planned_quantity numeric,
    planned_uom text,
    confirmed_quantity numeric,
    confirmed_uom text,
    remaining_quantity numeric,
    movement_segment_id integer,
    created_at timestamp with time zone,
    created_by text,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer DEFAULT 1
);


--
-- Name: trip_no_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.trip_no_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: trip_policies; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.trip_policies (
    so_phieu_can character varying(60) NOT NULL,
    ma_lenh_don character varying(60),
    chinh_sach_thu_mua text,
    tap_chat_cs numeric(9,4),
    ccs_chinh_sach numeric(9,4),
    weight_ton numeric(15,3),
    is_preview boolean DEFAULT false NOT NULL,
    fetched_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone
);


--
-- Name: trips; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.trips (
    id uuid NOT NULL,
    trip_no character varying(50) NOT NULL,
    site_id uuid NOT NULL,
    workflow_profile_id uuid NOT NULL,
    workflow_profile_version integer NOT NULL,
    source_system character varying(20) NOT NULL,
    reference_trip_no character varying(100),
    vehicle_id uuid,
    tractor_plate character varying(30),
    trailer_plate character varying(30),
    driver_ref text,
    lifecycle character varying(30) NOT NULL,
    execution_status character varying(20) NOT NULL,
    pause_issue_code text,
    pause_scope text,
    paused_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    created_by text NOT NULL,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer NOT NULL,
    vehicle_type character varying(20),
    queue_sequence_number integer,
    assigned_weighbridge_id uuid,
    assigned_warehouse_id uuid,
    gate_id uuid,
    sugarcane_type character varying(100),
    sugarcane_variety character varying(100),
    sugarcane_owner character varying(200),
    harvest_method character varying(100),
    station character varying(100),
    crop_season character varying(50),
    ticket_number character varying(50),
    organization_unit_code text,
    frm_lenh_don_id text,
    frm_ma_lenh_don text,
    frm_lenh_don_bu text,
    frm_loai_xe_code text,
    transporter character varying(300),
    warehouse_name character varying(300)
);


--
-- Name: weigh_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.weigh_events (
    id uuid NOT NULL,
    weigh_session_id uuid NOT NULL,
    trip_id uuid NOT NULL,
    revision integer NOT NULL,
    supersedes_id uuid,
    is_effective boolean NOT NULL,
    role character varying(10) NOT NULL,
    raw_weight numeric(15,3) NOT NULL,
    raw_uom character varying(5) NOT NULL,
    normalized_kg numeric(15,3) NOT NULL,
    measured_at timestamp with time zone NOT NULL,
    source character varying(10) NOT NULL,
    device_id uuid,
    stable_window interval NOT NULL,
    policy_version integer NOT NULL,
    evidence_refs jsonb NOT NULL,
    approval_ref character varying(100),
    reason character varying(500),
    created_at timestamp with time zone NOT NULL,
    created_by text NOT NULL,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer NOT NULL
);


--
-- Name: weigh_sessions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.weigh_sessions (
    id uuid NOT NULL,
    trip_id uuid NOT NULL,
    weighbridge_id uuid NOT NULL,
    checkpoint character varying(30) NOT NULL,
    role character varying(10) NOT NULL,
    segment_index integer NOT NULL,
    status character varying(20) NOT NULL,
    ownership_token character varying(64) NOT NULL,
    opened_at timestamp with time zone NOT NULL,
    closed_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    created_by text NOT NULL,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer NOT NULL
);


--
-- Name: weigh_tickets; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.weigh_tickets (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    trip_id uuid NOT NULL,
    ticket_number character varying(50) NOT NULL,
    template_code character varying(50) NOT NULL,
    printed_at timestamp with time zone NOT NULL,
    printed_by character varying(100) NOT NULL,
    is_reprint boolean DEFAULT false NOT NULL,
    print_count integer DEFAULT 1 NOT NULL,
    snapshot_json jsonb DEFAULT '{}'::jsonb NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by character varying(100) NOT NULL,
    updated_at timestamp with time zone,
    updated_by character varying(100),
    deleted_at timestamp with time zone,
    version integer DEFAULT 1 NOT NULL
);


--
-- Name: weighbridges; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.weighbridges (
    id uuid NOT NULL,
    site_id uuid NOT NULL,
    code character varying(30) NOT NULL,
    min_capacity_kg numeric(15,3) NOT NULL,
    max_capacity_kg numeric(15,3) NOT NULL,
    resolution_kg numeric(15,3) NOT NULL,
    is_out_of_service boolean NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by text NOT NULL,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer NOT NULL,
    first_weight_min_kg numeric(15,3),
    first_weight_max_kg numeric(15,3),
    second_weight_min_kg numeric(15,3),
    second_weight_max_kg numeric(15,3)
);


--
-- Name: workflow_profiles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.workflow_profiles (
    id uuid NOT NULL,
    code character varying(50) NOT NULL,
    name character varying(200) NOT NULL,
    profile_version integer NOT NULL,
    source_system character varying(20) NOT NULL,
    direction character varying(20) NOT NULL,
    checkpoints jsonb NOT NULL,
    weight_role_sequence jsonb NOT NULL,
    gate_checkpoints_required boolean NOT NULL,
    impurity_required_before_second_weight boolean NOT NULL,
    warehouse_confirmation_required boolean NOT NULL,
    bom_snapshot_required boolean NOT NULL,
    effective_from timestamp with time zone,
    effective_to timestamp with time zone,
    approved_by text,
    approved_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    created_by text NOT NULL,
    updated_at timestamp with time zone,
    updated_by text,
    deleted_at timestamp with time zone,
    version integer NOT NULL,
    thresholds jsonb NOT NULL
);


--
-- Name: allocations allocations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.allocations
    ADD CONSTRAINT allocations_pkey PRIMARY KEY (id);


--
-- Name: api_call_logs api_call_logs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.api_call_logs
    ADD CONSTRAINT api_call_logs_pkey PRIMARY KEY (id);


--
-- Name: approvals approvals_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.approvals
    ADD CONSTRAINT approvals_pkey PRIMARY KEY (id);


--
-- Name: bom_snapshots bom_snapshots_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bom_snapshots
    ADD CONSTRAINT bom_snapshots_pkey PRIMARY KEY (id);


--
-- Name: device_events device_events_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.device_events
    ADD CONSTRAINT device_events_pkey PRIMARY KEY (id);


--
-- Name: erp_order_lines erp_order_lines_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.erp_order_lines
    ADD CONSTRAINT erp_order_lines_pkey PRIMARY KEY (id);


--
-- Name: external_documents external_documents_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.external_documents
    ADD CONSTRAINT external_documents_pkey PRIMARY KEY (id);


--
-- Name: frm_lenh_don frm_lenh_don_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.frm_lenh_don
    ADD CONSTRAINT frm_lenh_don_pkey PRIMARY KEY (id);


--
-- Name: frm_vehicles frm_vehicles_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.frm_vehicles
    ADD CONSTRAINT frm_vehicles_pkey PRIMARY KEY (id);


--
-- Name: gate_commands gate_commands_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gate_commands
    ADD CONSTRAINT gate_commands_pkey PRIMARY KEY (id);


--
-- Name: gate_decisions gate_decisions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gate_decisions
    ADD CONSTRAINT gate_decisions_pkey PRIMARY KEY (id);


--
-- Name: gates gates_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gates
    ADD CONSTRAINT gates_pkey PRIMARY KEY (id);


--
-- Name: incidents incidents_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.incidents
    ADD CONSTRAINT incidents_pkey PRIMARY KEY (id);


--
-- Name: integration_messages integration_messages_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.integration_messages
    ADD CONSTRAINT integration_messages_pkey PRIMARY KEY (id);


--
-- Name: measurement_sessions measurement_sessions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.measurement_sessions
    ADD CONSTRAINT measurement_sessions_pkey PRIMARY KEY (id);


--
-- Name: __EFMigrationsHistory pk___ef_migrations_history; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."__EFMigrationsHistory"
    ADD CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id);


--
-- Name: audit_log_entries pk_audit_log_entries; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.audit_log_entries
    ADD CONSTRAINT pk_audit_log_entries PRIMARY KEY (id);


--
-- Name: checkpoint_events pk_checkpoint_events; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checkpoint_events
    ADD CONSTRAINT pk_checkpoint_events PRIMARY KEY (id);


--
-- Name: devices pk_devices; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.devices
    ADD CONSTRAINT pk_devices PRIMARY KEY (id);


--
-- Name: lanes pk_lanes; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.lanes
    ADD CONSTRAINT pk_lanes PRIMARY KEY (id);


--
-- Name: qr_tokens pk_qr_tokens; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.qr_tokens
    ADD CONSTRAINT pk_qr_tokens PRIMARY KEY (id);


--
-- Name: quality_samples pk_quality_samples; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.quality_samples
    ADD CONSTRAINT pk_quality_samples PRIMARY KEY (id);


--
-- Name: sites pk_sites; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sites
    ADD CONSTRAINT pk_sites PRIMARY KEY (id);


--
-- Name: trips pk_trips; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.trips
    ADD CONSTRAINT pk_trips PRIMARY KEY (id);


--
-- Name: weigh_events pk_weigh_events; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.weigh_events
    ADD CONSTRAINT pk_weigh_events PRIMARY KEY (id);


--
-- Name: weigh_sessions pk_weigh_sessions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.weigh_sessions
    ADD CONSTRAINT pk_weigh_sessions PRIMARY KEY (id);


--
-- Name: weighbridges pk_weighbridges; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.weighbridges
    ADD CONSTRAINT pk_weighbridges PRIMARY KEY (id);


--
-- Name: workflow_profiles pk_workflow_profiles; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.workflow_profiles
    ADD CONSTRAINT pk_workflow_profiles PRIMARY KEY (id);


--
-- Name: queue_sequence_tracker queue_sequence_tracker_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.queue_sequence_tracker
    ADD CONSTRAINT queue_sequence_tracker_pkey PRIMARY KEY (site_id, vehicle_type, operating_date);


--
-- Name: reconciliation_checks reconciliation_checks_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reconciliation_checks
    ADD CONSTRAINT reconciliation_checks_pkey PRIMARY KEY (id);


--
-- Name: reconciliations reconciliations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reconciliations
    ADD CONSTRAINT reconciliations_pkey PRIMARY KEY (id);


--
-- Name: trip_document_lines trip_document_lines_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.trip_document_lines
    ADD CONSTRAINT trip_document_lines_pkey PRIMARY KEY (id);


--
-- Name: trip_policies trip_policies_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.trip_policies
    ADD CONSTRAINT trip_policies_pkey PRIMARY KEY (so_phieu_can);


--
-- Name: weigh_tickets weigh_tickets_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.weigh_tickets
    ADD CONSTRAINT weigh_tickets_pkey PRIMARY KEY (id);


--
-- Name: idx_api_call_logs_business_key; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_api_call_logs_business_key ON public.api_call_logs USING btree (business_key);


--
-- Name: idx_api_call_logs_occurred_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_api_call_logs_occurred_at ON public.api_call_logs USING btree (occurred_at DESC);


--
-- Name: idx_erp_order_lines_business_unit; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_erp_order_lines_business_unit ON public.erp_order_lines USING btree (business_unit);


--
-- Name: idx_erp_order_lines_plate; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_erp_order_lines_plate ON public.erp_order_lines USING btree (plate_number);


--
-- Name: idx_erp_order_lines_shipment; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_erp_order_lines_shipment ON public.erp_order_lines USING btree (shipment_number);


--
-- Name: idx_erp_order_lines_shipment_number; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_erp_order_lines_shipment_number ON public.erp_order_lines USING btree (shipment_number);


--
-- Name: idx_frm_lenh_don_bien_so; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_frm_lenh_don_bien_so ON public.frm_lenh_don USING btree (bien_so_xe);


--
-- Name: idx_frm_lenh_don_business_unit; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_frm_lenh_don_business_unit ON public.frm_lenh_don USING btree (business_unit);


--
-- Name: idx_frm_lenh_don_ma_ld; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_frm_lenh_don_ma_ld ON public.frm_lenh_don USING btree (ma_ld);


--
-- Name: idx_frm_vehicles_bien_so; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_frm_vehicles_bien_so ON public.frm_vehicles USING btree (bien_so);


--
-- Name: idx_weigh_tickets_trip; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_weigh_tickets_trip ON public.weigh_tickets USING btree (trip_id);


--
-- Name: ix_audit_log_entries_action_type; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_audit_log_entries_action_type ON public.audit_log_entries USING btree (action_type);


--
-- Name: ix_audit_log_entries_trip_id_occurred_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_audit_log_entries_trip_id_occurred_at ON public.audit_log_entries USING btree (trip_id, occurred_at);


--
-- Name: ix_checkpoint_events_trip_id_type; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_checkpoint_events_trip_id_type ON public.checkpoint_events USING btree (trip_id, type);


--
-- Name: ix_devices_lane_id_device_class; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_devices_lane_id_device_class ON public.devices USING btree (lane_id, device_class);


--
-- Name: ix_devices_site_id_code; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_devices_site_id_code ON public.devices USING btree (site_id, code);


--
-- Name: ix_lanes_site_id_code; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_lanes_site_id_code ON public.lanes USING btree (site_id, code);


--
-- Name: ix_qr_tokens_token_hash; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_qr_tokens_token_hash ON public.qr_tokens USING btree (token_hash);


--
-- Name: ix_qr_tokens_trip_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_qr_tokens_trip_id ON public.qr_tokens USING btree (trip_id);


--
-- Name: ix_quality_samples_supersedes_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_quality_samples_supersedes_id ON public.quality_samples USING btree (supersedes_id);


--
-- Name: ix_sites_code; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_sites_code ON public.sites USING btree (code);


--
-- Name: ix_trips_site_id_lifecycle; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_trips_site_id_lifecycle ON public.trips USING btree (site_id, lifecycle);


--
-- Name: ix_trips_tractor_plate; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_trips_tractor_plate ON public.trips USING btree (tractor_plate);


--
-- Name: ix_trips_trip_no; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_trips_trip_no ON public.trips USING btree (trip_no);


--
-- Name: ix_weigh_events_measured_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_weigh_events_measured_at ON public.weigh_events USING btree (measured_at);


--
-- Name: ix_weigh_events_supersedes_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_weigh_events_supersedes_id ON public.weigh_events USING btree (supersedes_id);


--
-- Name: ix_weigh_events_weigh_session_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_weigh_events_weigh_session_id ON public.weigh_events USING btree (weigh_session_id);


--
-- Name: ix_weighbridges_site_id_code; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_weighbridges_site_id_code ON public.weighbridges USING btree (site_id, code);


--
-- Name: ix_workflow_profiles_code_profile_version; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_workflow_profiles_code_profile_version ON public.workflow_profiles USING btree (code, profile_version);


--
-- Name: uq_erp_order_lines_storage_key; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_erp_order_lines_storage_key ON public.erp_order_lines USING btree (storage_key) WHERE (storage_key IS NOT NULL);


--
-- Name: uq_frm_lenh_don_ma_ld_bu; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_frm_lenh_don_ma_ld_bu ON public.frm_lenh_don USING btree (ma_ld, COALESCE(business_unit, ''::text));


--
-- Name: uq_frm_vehicles_bien_so_ma_the; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_frm_vehicles_bien_so_ma_the ON public.frm_vehicles USING btree (bien_so, COALESCE(ma_the, ''::text));


--
-- Name: uq_quality_samples_trip_type_effective; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_quality_samples_trip_type_effective ON public.quality_samples USING btree (trip_id, type, is_effective) WHERE (is_effective = true);


--
-- Name: uq_weigh_events_trip_role_effective; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_weigh_events_trip_role_effective ON public.weigh_events USING btree (trip_id, role, is_effective) WHERE (is_effective = true);


--
-- Name: uq_weigh_sessions_bridge_open; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_weigh_sessions_bridge_open ON public.weigh_sessions USING btree (weighbridge_id, status) WHERE ((status)::text = 'Open'::text);


--
-- Name: uq_weigh_sessions_trip_checkpoint_open; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_weigh_sessions_trip_checkpoint_open ON public.weigh_sessions USING btree (trip_id, checkpoint, segment_index, status) WHERE ((status)::text = 'Open'::text);


--
-- Name: uq_weigh_tickets_number; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_weigh_tickets_number ON public.weigh_tickets USING btree (ticket_number) WHERE (is_reprint = false);


--
-- Name: checkpoint_events fk_checkpoint_events_trips_trip_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checkpoint_events
    ADD CONSTRAINT fk_checkpoint_events_trips_trip_id FOREIGN KEY (trip_id) REFERENCES public.trips(id) ON DELETE CASCADE;


--
-- Name: qr_tokens fk_qr_tokens_trips_trip_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.qr_tokens
    ADD CONSTRAINT fk_qr_tokens_trips_trip_id FOREIGN KEY (trip_id) REFERENCES public.trips(id);


--
-- Name: quality_samples fk_quality_samples_quality_samples_supersedes_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.quality_samples
    ADD CONSTRAINT fk_quality_samples_quality_samples_supersedes_id FOREIGN KEY (supersedes_id) REFERENCES public.quality_samples(id);


--
-- Name: weigh_events fk_weigh_events_weigh_events_supersedes_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.weigh_events
    ADD CONSTRAINT fk_weigh_events_weigh_events_supersedes_id FOREIGN KEY (supersedes_id) REFERENCES public.weigh_events(id);


--
-- Name: weigh_events fk_weigh_events_weigh_sessions_weigh_session_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.weigh_events
    ADD CONSTRAINT fk_weigh_events_weigh_sessions_weigh_session_id FOREIGN KEY (weigh_session_id) REFERENCES public.weigh_sessions(id) ON DELETE CASCADE;


--
-- Name: weigh_sessions fk_weigh_sessions_trips_trip_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.weigh_sessions
    ADD CONSTRAINT fk_weigh_sessions_trips_trip_id FOREIGN KEY (trip_id) REFERENCES public.trips(id) ON DELETE CASCADE;


--
-- Name: weigh_tickets weigh_tickets_trip_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.weigh_tickets
    ADD CONSTRAINT weigh_tickets_trip_id_fkey FOREIGN KEY (trip_id) REFERENCES public.trips(id);


--
-- PostgreSQL database dump complete
--

