using Microsoft.EntityFrameworkCore.Migrations;

namespace Proxytrace.Storage.Migrations;

public sealed partial class RefreshMultiTurnTracePreviews : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The startup preview backfill recalculates only null previews. Requeue existing multi-turn
        // requests once so their stored first-user previews become latest-user previews. The CASE
        // guards are load-bearing: they are evaluated in order, so a row whose Request is not a
        // JSON object with a messages array is skipped instead of aborting the whole migration.
        migrationBuilder.Sql("""
            UPDATE "AgentCallEntity" AS ac
            SET "RequestPreview" = NULL
            WHERE "RequestPreview" IS NOT NULL
              AND CASE
                    WHEN NOT pg_input_is_valid(ac."Request", 'jsonb') THEN false
                    WHEN jsonb_typeof((ac."Request")::jsonb -> 'messages') IS DISTINCT FROM 'array' THEN false
                    ELSE (
                      SELECT count(*)
                      FROM jsonb_array_elements((ac."Request")::jsonb -> 'messages') AS msg
                      WHERE lower(coalesce(msg->>'Role', msg->>'role')) = 'user'
                    ) > 1
                  END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
