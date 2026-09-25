using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using TTC.Weighing.Domain.Enums;

#nullable disable

namespace TTC.Weighing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialWeighingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // R-005 — số đăng tài tăng tại server, an toàn khi nhiều trạm đăng tài đồng thời.
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS trip_no_seq START 1 INCREMENT 1;");

            migrationBuilder.CreateTable(
                name: "allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    measured_total_ref = table.Column<Guid>(type: "uuid", nullable: false),
                    document_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    allocated_kg = table.Column<decimal>(type: "numeric(15,3)", precision: 15, scale: 3, nullable: false),
                    method = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ratio = table.Column<decimal>(type: "numeric(12,8)", precision: 12, scale: 8, nullable: true),
                    rounding_adjustment_kg = table.Column<decimal>(type: "numeric(15,3)", precision: 15, scale: 3, nullable: false),
                    approval_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_allocations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "approvals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    requested_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    approved_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    document_ref = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approvals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_log_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    before_json = table.Column<string>(type: "jsonb", nullable: true),
                    after_json = table.Column<string>(type: "jsonb", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bom_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_system = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    item_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    effective_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    uom = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nominal_kg_per_unit = table.Column<decimal>(type: "numeric(15,3)", precision: 15, scale: 3, nullable: true),
                    min_kg_per_unit = table.Column<decimal>(type: "numeric(15,3)", precision: 15, scale: 3, nullable: true),
                    max_kg_per_unit = table.Column<decimal>(type: "numeric(15,3)", precision: 15, scale: 3, nullable: true),
                    packaging_tare_kg = table.Column<decimal>(type: "numeric(15,3)", precision: 15, scale: 3, nullable: true),
                    pallet_tare_kg = table.Column<decimal>(type: "numeric(15,3)", precision: 15, scale: 3, nullable: true),
                    mass_basis = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    snapshot_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bom_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "device_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boot_session_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lane_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    measurement_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    processing_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    processing_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lane_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    device_class = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    mapping_version = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    is_test_device = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_devices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "external_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_system = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    organization_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    business_unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    document_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reference_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    shipment_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    source_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    source_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    raw_snapshot_ref = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gate_commands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gate_decision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    command_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    controller_feedback = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    passage_event_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    passage_confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    passage_confirmed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gate_commands", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gate_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lane_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    identity_evidence = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    identity_verified_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    conditions_json = table.Column<string>(type: "jsonb", nullable: false),
                    allowed = table.Column<bool>(type: "boolean", nullable: false),
                    deny_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gate_decisions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "incidents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issue_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    scope = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    opened_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    unknown_transactions_resolved = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incidents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "integration_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_revision = table.Column<int>(type: "integer", nullable: false),
                    destination = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    business_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    shipment_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    payload_ref = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    interface_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    masked_response = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    business_error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    business_error_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    external_document_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    superseded_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    auto_retry_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_integration_messages_integration_messages_superseded_by_id",
                        column: x => x.superseded_by_id,
                        principalTable: "integration_messages",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "lanes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    weighbridge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lanes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "measurement_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conveyor_lane_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    document_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    device_ids = table.Column<List<Guid>>(type: "jsonb", nullable: false),
                    mass_basis = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reading_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    counter_epoch = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    counter_start = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    counter_end = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    sequence_watermark = table.Column<long>(type: "bigint", nullable: true),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    hold_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    iot_mass_kg = table.Column<decimal>(type: "numeric(15,3)", precision: 15, scale: 3, nullable: true),
                    iot_mass_origin = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    forward_bag_count = table.Column<int>(type: "integer", nullable: true),
                    reverse_bag_count = table.Column<int>(type: "integer", nullable: true),
                    net_bag_count = table.Column<int>(type: "integer", nullable: true),
                    data_complete = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_measurement_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quality_samples",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_qr_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    supersedes_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_effective = table.Column<bool>(type: "boolean", nullable: false),
                    sample_before_kg = table.Column<decimal>(type: "numeric(15,3)", precision: 15, scale: 3, nullable: true),
                    sample_after_kg = table.Column<decimal>(type: "numeric(15,3)", precision: 15, scale: 3, nullable: true),
                    impurity_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    ccs_value = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_by = table.Column<string>(type: "text", nullable: true),
                    below_minimum_sample_warning = table.Column<bool>(type: "boolean", nullable: false),
                    approval_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quality_samples", x => x.id);
                    table.ForeignKey(
                        name: "fk_quality_samples_quality_samples_supersedes_id",
                        column: x => x.supersedes_id,
                        principalTable: "quality_samples",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "reconciliations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    is_effective = table.Column<bool>(type: "boolean", nullable: false),
                    input_versions_json = table.Column<string>(type: "jsonb", nullable: false),
                    evaluated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reconciliations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sites", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trips",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_profile_version = table.Column<int>(type: "integer", nullable: false),
                    source_system = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reference_trip_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tractor_plate = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    trailer_plate = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    driver_ref = table.Column<string>(type: "text", nullable: true),
                    lifecycle = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    execution_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pause_issue_code = table.Column<string>(type: "text", nullable: true),
                    pause_scope = table.Column<string>(type: "text", nullable: true),
                    paused_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trips", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "weighbridges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    min_capacity_kg = table.Column<decimal>(type: "numeric(15,3)", precision: 15, scale: 3, nullable: false),
                    max_capacity_kg = table.Column<decimal>(type: "numeric(15,3)", precision: 15, scale: 3, nullable: false),
                    resolution_kg = table.Column<decimal>(type: "numeric(15,3)", precision: 15, scale: 3, nullable: false),
                    is_out_of_service = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_weighbridges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    profile_version = table.Column<int>(type: "integer", nullable: false),
                    source_system = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    checkpoints = table.Column<List<CheckpointType>>(type: "jsonb", nullable: false),
                    weight_role_sequence = table.Column<List<WeightRole>>(type: "jsonb", nullable: false),
                    gate_checkpoints_required = table.Column<bool>(type: "boolean", nullable: false),
                    impurity_required_before_second_weight = table.Column<bool>(type: "boolean", nullable: false),
                    warehouse_confirmation_required = table.Column<bool>(type: "boolean", nullable: false),
                    bom_snapshot_required = table.Column<bool>(type: "boolean", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<string>(type: "text", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    thresholds = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trip_document_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_line_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    item_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                    planned_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    planned_uom = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    confirmed_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    confirmed_uom = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    remaining_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    movement_segment_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_document_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_trip_document_lines_external_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "external_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reconciliation_checks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reconciliation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    check_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    observed_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    expected_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    lower_bound = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    upper_bound = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    difference = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tolerance_policy_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tolerance_policy_version = table.Column<int>(type: "integer", nullable: true),
                    verdict = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reconciliation_checks", x => x.id);
                    table.ForeignKey(
                        name: "fk_reconciliation_checks_reconciliations_reconciliation_id",
                        column: x => x.reconciliation_id,
                        principalTable: "reconciliations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "checkpoint_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lane_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by = table.Column<string>(type: "text", nullable: true),
                    waiver_approval_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    waiver_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checkpoint_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_checkpoint_events_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "qr_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: true),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qr_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_qr_tokens_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "weigh_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    weighbridge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    checkpoint = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    role = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    segment_index = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ownership_token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_weigh_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_weigh_sessions_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "weigh_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    weigh_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    supersedes_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_effective = table.Column<bool>(type: "boolean", nullable: false),
                    role = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    raw_weight = table.Column<decimal>(type: "numeric(15,3)", precision: 15, scale: 3, nullable: false),
                    raw_uom = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    normalized_kg = table.Column<decimal>(type: "numeric(15,3)", precision: 15, scale: 3, nullable: false),
                    measured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stable_window = table.Column<TimeSpan>(type: "interval", nullable: false),
                    policy_version = table.Column<int>(type: "integer", nullable: false),
                    evidence_refs = table.Column<List<string>>(type: "jsonb", nullable: false),
                    approval_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_weigh_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_weigh_events_weigh_events_supersedes_id",
                        column: x => x.supersedes_id,
                        principalTable: "weigh_events",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_weigh_events_weigh_sessions_weigh_session_id",
                        column: x => x.weigh_session_id,
                        principalTable: "weigh_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_allocations_trip_id",
                table: "allocations",
                column: "trip_id");

            migrationBuilder.CreateIndex(
                name: "ix_approvals_trip_id_action_type",
                table: "approvals",
                columns: new[] { "trip_id", "action_type" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_entries_action_type",
                table: "audit_log_entries",
                column: "action_type");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_entries_trip_id_occurred_at",
                table: "audit_log_entries",
                columns: new[] { "trip_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bom_snapshots_source_system_item_code_effective_at",
                table: "bom_snapshots",
                columns: new[] { "source_system", "item_code", "effective_at" });

            migrationBuilder.CreateIndex(
                name: "ix_checkpoint_events_trip_id_type",
                table: "checkpoint_events",
                columns: new[] { "trip_id", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_device_events_measurement_session_id_sequence",
                table: "device_events",
                columns: new[] { "measurement_session_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_device_events_processing_status",
                table: "device_events",
                column: "processing_status");

            migrationBuilder.CreateIndex(
                name: "uq_device_events_dedup",
                table: "device_events",
                columns: new[] { "device_id", "boot_session_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_devices_lane_id_device_class",
                table: "devices",
                columns: new[] { "lane_id", "device_class" });

            migrationBuilder.CreateIndex(
                name: "ix_devices_site_id_code",
                table: "devices",
                columns: new[] { "site_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_documents_source_system_document_type_reference_no",
                table: "external_documents",
                columns: new[] { "source_system", "document_type", "reference_no" });

            migrationBuilder.CreateIndex(
                name: "ix_gate_commands_command_id",
                table: "gate_commands",
                column: "command_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gate_commands_trip_id_state",
                table: "gate_commands",
                columns: new[] { "trip_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_gate_decisions_trip_id_decided_at",
                table: "gate_decisions",
                columns: new[] { "trip_id", "decided_at" });

            migrationBuilder.CreateIndex(
                name: "ix_incidents_site_id_resolved_at",
                table: "incidents",
                columns: new[] { "site_id", "resolved_at" });

            migrationBuilder.CreateIndex(
                name: "ix_incidents_trip_id",
                table: "incidents",
                column: "trip_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_messages_status",
                table: "integration_messages",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_integration_messages_superseded_by_id",
                table: "integration_messages",
                column: "superseded_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_messages_trip_id_status",
                table: "integration_messages",
                columns: new[] { "trip_id", "status" });

            migrationBuilder.CreateIndex(
                name: "uq_integration_messages_business_key",
                table: "integration_messages",
                column: "business_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lanes_site_id_code",
                table: "lanes",
                columns: new[] { "site_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_measurement_sessions_trip_id_status",
                table: "measurement_sessions",
                columns: new[] { "trip_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_measurement_sessions_warehouse_id_status",
                table: "measurement_sessions",
                columns: new[] { "warehouse_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_qr_tokens_token_hash",
                table: "qr_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_qr_tokens_trip_id",
                table: "qr_tokens",
                column: "trip_id");

            migrationBuilder.CreateIndex(
                name: "ix_quality_samples_supersedes_id",
                table: "quality_samples",
                column: "supersedes_id");

            migrationBuilder.CreateIndex(
                name: "uq_quality_samples_trip_type_effective",
                table: "quality_samples",
                columns: new[] { "trip_id", "type", "is_effective" },
                unique: true,
                filter: "is_effective = true");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_checks_reconciliation_id_check_type",
                table: "reconciliation_checks",
                columns: new[] { "reconciliation_id", "check_type" });

            migrationBuilder.CreateIndex(
                name: "uq_reconciliations_trip_effective",
                table: "reconciliations",
                columns: new[] { "trip_id", "is_effective" },
                unique: true,
                filter: "is_effective = true");

            migrationBuilder.CreateIndex(
                name: "ix_sites_code",
                table: "sites",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trip_document_lines_document_id",
                table: "trip_document_lines",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_document_lines_trip_id_movement_segment_id",
                table: "trip_document_lines",
                columns: new[] { "trip_id", "movement_segment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_trips_site_id_lifecycle",
                table: "trips",
                columns: new[] { "site_id", "lifecycle" });

            migrationBuilder.CreateIndex(
                name: "ix_trips_tractor_plate",
                table: "trips",
                column: "tractor_plate");

            migrationBuilder.CreateIndex(
                name: "ix_trips_trip_no",
                table: "trips",
                column: "trip_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_weigh_events_measured_at",
                table: "weigh_events",
                column: "measured_at");

            migrationBuilder.CreateIndex(
                name: "ix_weigh_events_supersedes_id",
                table: "weigh_events",
                column: "supersedes_id");

            migrationBuilder.CreateIndex(
                name: "ix_weigh_events_weigh_session_id",
                table: "weigh_events",
                column: "weigh_session_id");

            migrationBuilder.CreateIndex(
                name: "uq_weigh_events_trip_role_effective",
                table: "weigh_events",
                columns: new[] { "trip_id", "role", "is_effective" },
                unique: true,
                filter: "is_effective = true");

            migrationBuilder.CreateIndex(
                name: "uq_weigh_sessions_bridge_open",
                table: "weigh_sessions",
                columns: new[] { "weighbridge_id", "status" },
                unique: true,
                filter: "status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "uq_weigh_sessions_trip_checkpoint_open",
                table: "weigh_sessions",
                columns: new[] { "trip_id", "checkpoint", "segment_index", "status" },
                unique: true,
                filter: "status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_weighbridges_site_id_code",
                table: "weighbridges",
                columns: new[] { "site_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_profiles_code_profile_version",
                table: "workflow_profiles",
                columns: new[] { "code", "profile_version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS trip_no_seq;");

            migrationBuilder.DropTable(
                name: "allocations");

            migrationBuilder.DropTable(
                name: "approvals");

            migrationBuilder.DropTable(
                name: "audit_log_entries");

            migrationBuilder.DropTable(
                name: "bom_snapshots");

            migrationBuilder.DropTable(
                name: "checkpoint_events");

            migrationBuilder.DropTable(
                name: "device_events");

            migrationBuilder.DropTable(
                name: "devices");

            migrationBuilder.DropTable(
                name: "gate_commands");

            migrationBuilder.DropTable(
                name: "gate_decisions");

            migrationBuilder.DropTable(
                name: "gates");

            migrationBuilder.DropTable(
                name: "incidents");

            migrationBuilder.DropTable(
                name: "integration_messages");

            migrationBuilder.DropTable(
                name: "lanes");

            migrationBuilder.DropTable(
                name: "measurement_sessions");

            migrationBuilder.DropTable(
                name: "qr_tokens");

            migrationBuilder.DropTable(
                name: "quality_samples");

            migrationBuilder.DropTable(
                name: "reconciliation_checks");

            migrationBuilder.DropTable(
                name: "sites");

            migrationBuilder.DropTable(
                name: "trip_document_lines");

            migrationBuilder.DropTable(
                name: "weigh_events");

            migrationBuilder.DropTable(
                name: "weighbridges");

            migrationBuilder.DropTable(
                name: "workflow_profiles");

            migrationBuilder.DropTable(
                name: "reconciliations");

            migrationBuilder.DropTable(
                name: "external_documents");

            migrationBuilder.DropTable(
                name: "weigh_sessions");

            migrationBuilder.DropTable(
                name: "trips");
        }
    }
}
