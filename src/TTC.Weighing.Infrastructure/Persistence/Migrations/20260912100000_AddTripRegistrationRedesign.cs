using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TTC.Weighing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTripRegistrationRedesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Thêm 11 cột mới cho bảng trips — tất cả nullable để tương thích ngược
            migrationBuilder.AddColumn<string>(
                name: "vehicle_type",
                table: "trips",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "queue_sequence_number",
                table: "trips",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "assigned_weighbridge_id",
                table: "trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "assigned_warehouse_id",
                table: "trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "gate_id",
                table: "trips",
                type: "uuid",
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
                name: "sugarcane_owner",
                table: "trips",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "harvest_method",
                table: "trips",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "station",
                table: "trips",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "crop_season",
                table: "trips",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // 2. Thêm cột encoded_content cho bảng qr_tokens
            migrationBuilder.AddColumn<string>(
                name: "encoded_content",
                table: "qr_tokens",
                type: "text",
                nullable: true);

            // 3. Tạo bảng queue_sequence_tracker — theo dõi sequence hàng chờ, reset hằng ngày
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

            // 4. Tạo indexes hỗ trợ lưới đăng tài và truy vấn
            migrationBuilder.CreateIndex(
                name: "idx_trips_vehicle_type",
                table: "trips",
                columns: new[] { "site_id", "vehicle_type" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "idx_trips_gate_id",
                table: "trips",
                column: "gate_id",
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "idx_trips_queue_seq",
                table: "trips",
                columns: new[] { "site_id", "vehicle_type", "queue_sequence_number" },
                filter: "deleted_at IS NULL AND queue_sequence_number IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 4. Xóa indexes
            migrationBuilder.DropIndex(
                name: "idx_trips_queue_seq",
                table: "trips");

            migrationBuilder.DropIndex(
                name: "idx_trips_gate_id",
                table: "trips");

            migrationBuilder.DropIndex(
                name: "idx_trips_vehicle_type",
                table: "trips");

            // 3. Xóa bảng queue_sequence_tracker
            migrationBuilder.DropTable(
                name: "queue_sequence_tracker");

            // 2. Xóa cột encoded_content từ qr_tokens
            migrationBuilder.DropColumn(
                name: "encoded_content",
                table: "qr_tokens");

            // 1. Xóa 11 cột mới từ trips (thứ tự ngược lại)
            migrationBuilder.DropColumn(
                name: "crop_season",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "station",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "harvest_method",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "sugarcane_owner",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "sugarcane_variety",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "sugarcane_type",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "gate_id",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "assigned_warehouse_id",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "assigned_weighbridge_id",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "queue_sequence_number",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "vehicle_type",
                table: "trips");
        }
    }
}
