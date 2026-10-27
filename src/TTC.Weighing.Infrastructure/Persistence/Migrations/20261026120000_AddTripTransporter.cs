using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TTC.Weighing.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Nhà vận chuyển (ERP transporter) trên chuyến — lưu lúc đăng tài vật tư để đẩy oracle-tickets khi cân xong.
    /// Idempotent (IF NOT EXISTS) vì một số cột trips trước đây được thêm tay ngoài migration.
    /// </summary>
    [DbContext(typeof(WeighingDbContext))]
    [Migration("20261026120000_AddTripTransporter")]
    public partial class AddTripTransporter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE trips ADD COLUMN IF NOT EXISTS transporter varchar(300);");
            migrationBuilder.Sql("ALTER TABLE trips ADD COLUMN IF NOT EXISTS warehouse_name varchar(300);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE trips DROP COLUMN IF EXISTS warehouse_name;");
            migrationBuilder.Sql("ALTER TABLE trips DROP COLUMN IF EXISTS transporter;");
        }
    }
}
