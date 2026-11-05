using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TTC.Weighing.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ID phiếu đăng tài DigiScale trên chuyến (trả về lúc đăng tài) — dùng làm phieudangtaiid khi hủy / xóa.
    /// Điền lại cho chuyến đã đăng tài trước đây từ nhật ký API (phản hồi new_phieudangtai saved = true, key = số đăng tài).
    /// Idempotent (IF NOT EXISTS); bỏ qua bước điền lại nếu chưa có bảng api_call_logs.
    /// </summary>
    [DbContext(typeof(WeighingDbContext))]
    [Migration("20261030120000_AddTripDigiRegistrationId")]
    public partial class AddTripDigiRegistrationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE trips ADD COLUMN IF NOT EXISTS digi_registration_id varchar(100);");

            migrationBuilder.Sql(@"
DO $$
BEGIN
  IF to_regclass('public.api_call_logs') IS NOT NULL THEN
    UPDATE trips t SET digi_registration_id = x.id
    FROM (
      SELECT DISTINCT ON (it->>'key') it->>'key' AS trip_no, it->>'id' AS id
      FROM api_call_logs l,
           jsonb_array_elements(CASE WHEN l.response_body ~ '^\s*\{' THEN (l.response_body::jsonb)->'items' ELSE '[]'::jsonb END) it
      WHERE l.request_body ILIKE '%new_phieudangtai%'
        AND l.request_body ILIKE '%""status"":""Create""%'
        AND (it->>'saved') = 'true' AND coalesce(it->>'id','') <> ''
      ORDER BY it->>'key', l.created_at DESC
    ) x
    WHERE t.trip_no = x.trip_no AND t.digi_registration_id IS NULL;
  END IF;
END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE trips DROP COLUMN IF EXISTS digi_registration_id;");
        }
    }
}
