using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TTC.Weighing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWeighingWorkflowIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE weigh_tickets (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    trip_id uuid NOT NULL REFERENCES trips(id),
    ticket_number varchar(50) NOT NULL,
    template_code varchar(50) NOT NULL,
    printed_at timestamptz NOT NULL,
    printed_by varchar(100) NOT NULL,
    is_reprint boolean NOT NULL DEFAULT false,
    print_count int NOT NULL DEFAULT 1,
    snapshot_json jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by varchar(100) NOT NULL,
    updated_at timestamptz,
    updated_by varchar(100),
    deleted_at timestamptz,
    version int NOT NULL DEFAULT 1
);

CREATE INDEX idx_weigh_tickets_trip ON weigh_tickets (trip_id);
CREATE UNIQUE INDEX uq_weigh_tickets_number ON weigh_tickets (ticket_number)
    WHERE is_reprint = false;

ALTER TABLE trips ADD COLUMN ticket_number varchar(50);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TABLE IF EXISTS weigh_tickets;
ALTER TABLE trips DROP COLUMN IF EXISTS ticket_number;
");
        }
    }
}
