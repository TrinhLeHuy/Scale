using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TTC.Weighing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationUnitCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "assigned_warehouse_id",
                table: "trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "assigned_weighbridge_id",
                table: "trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "crop_season",
                table: "trips",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "gate_id",
                table: "trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "harvest_method",
                table: "trips",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "organization_unit_code",
                table: "trips",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "queue_sequence_number",
                table: "trips",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "station",
                table: "trips",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sugarcane_owner",
                table: "trips",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sugarcane_type",
                table: "trips",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sugarcane_variety",
                table: "trips",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ticket_number",
                table: "trips",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "vehicle_type",
                table: "trips",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "encoded_content",
                table: "qr_tokens",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "queue_sequence_tracker",
                columns: table => new
                {
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    operating_date = table.Column<DateOnly>(type: "date", nullable: false),
                    last_value = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_queue_sequence_tracker", x => new { x.site_id, x.vehicle_type, x.operating_date });
                });

            migrationBuilder.CreateTable(
                name: "weigh_tickets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    template_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    printed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    printed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_reprint = table.Column<bool>(type: "boolean", nullable: false),
                    print_count = table.Column<int>(type: "integer", nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_weigh_tickets", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_weigh_tickets_trip",
                table: "weigh_tickets",
                column: "trip_id");

            migrationBuilder.CreateIndex(
                name: "uq_weigh_tickets_number",
                table: "weigh_tickets",
                column: "ticket_number",
                unique: true,
                filter: "is_reprint = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "queue_sequence_tracker");

            migrationBuilder.DropTable(
                name: "weigh_tickets");

            migrationBuilder.DropColumn(
                name: "assigned_warehouse_id",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "assigned_weighbridge_id",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "crop_season",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "gate_id",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "harvest_method",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "organization_unit_code",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "queue_sequence_number",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "station",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "sugarcane_owner",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "sugarcane_type",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "sugarcane_variety",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "ticket_number",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "vehicle_type",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "encoded_content",
                table: "qr_tokens");
        }
    }
}
