using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TTC.Weighing.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Giới hạn số cân theo lượt cho từng bàn cân (Vào min/Max, Ra min/Max của phần mềm cân cũ), đơn vị kg.
    /// Cột NULL = không đặt giới hạn. Idempotent (IF NOT EXISTS), cùng nội dung với
    /// db/upgrades/20261105_01_weighbridge_weigh_limits.sql.
    /// </summary>
    [DbContext(typeof(WeighingDbContext))]
    [Migration("20261105090000_AddWeighbridgeWeighLimits")]
    public partial class AddWeighbridgeWeighLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE weighbridges ADD COLUMN IF NOT EXISTS first_weight_min_kg numeric(15,3) NULL;
ALTER TABLE weighbridges ADD COLUMN IF NOT EXISTS first_weight_max_kg numeric(15,3) NULL;
ALTER TABLE weighbridges ADD COLUMN IF NOT EXISTS second_weight_min_kg numeric(15,3) NULL;
ALTER TABLE weighbridges ADD COLUMN IF NOT EXISTS second_weight_max_kg numeric(15,3) NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE weighbridges DROP COLUMN IF EXISTS first_weight_min_kg;
ALTER TABLE weighbridges DROP COLUMN IF EXISTS first_weight_max_kg;
ALTER TABLE weighbridges DROP COLUMN IF EXISTS second_weight_min_kg;
ALTER TABLE weighbridges DROP COLUMN IF EXISTS second_weight_max_kg;");
        }
    }
}
