using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TTC.Weighing.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Lưu số đọc Brix / Pol / Pol cải chính của mẫu CCS — để xem lại và đẩy lại DigiScale đúng số đã đo.
    /// Idempotent (IF NOT EXISTS) vì DB dev được cập nhật bằng script tay.
    /// </summary>
    [DbContext(typeof(WeighingDbContext))]
    [Migration("20261026150000_AddCcsReadings")]
    public partial class AddCcsReadings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE quality_samples ADD COLUMN IF NOT EXISTS brix_value numeric(9,4);");
            migrationBuilder.Sql("ALTER TABLE quality_samples ADD COLUMN IF NOT EXISTS pol_value numeric(9,4);");
            migrationBuilder.Sql("ALTER TABLE quality_samples ADD COLUMN IF NOT EXISTS pol_corrected numeric(9,4);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE quality_samples DROP COLUMN IF EXISTS pol_corrected;");
            migrationBuilder.Sql("ALTER TABLE quality_samples DROP COLUMN IF EXISTS pol_value;");
            migrationBuilder.Sql("ALTER TABLE quality_samples DROP COLUMN IF EXISTS brix_value;");
        }
    }
}
