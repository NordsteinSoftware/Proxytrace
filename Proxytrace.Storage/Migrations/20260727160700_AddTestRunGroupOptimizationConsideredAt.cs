using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Proxytrace.Storage.Migrations
{
    /// <summary>
    /// Adds the durable "the optimizer has looked at this group" marker that its otherwise in-memory
    /// queue recovers from after a restart.
    /// </summary>
    /// <remarks>
    /// <b>Existing rows are backfilled as already considered</b>, and that is the load-bearing part
    /// of this migration. The column is nullable and a fresh column is null everywhere, so without
    /// the backfill the first start after upgrading would see every completed group an installation
    /// has ever run as an unprocessed backlog — and re-queuing those means real A/B validation runs
    /// against a real provider, i.e. real money, for history nobody asked to re-analyse. Only groups
    /// that complete *after* the upgrade are eligible for recovery.
    /// </remarks>
    public partial class AddTestRunGroupOptimizationConsideredAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OptimizationConsideredAt",
                table: "TestRunGroupEntity",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill before the index exists: history is not a backlog. See the remarks above.
            migrationBuilder.Sql(
                """UPDATE "TestRunGroupEntity" SET "OptimizationConsideredAt" = NOW() WHERE "OptimizationConsideredAt" IS NULL;""");

            migrationBuilder.CreateIndex(
                name: "IX_TestRunGroupEntity_OptimizationConsideredAt_IsSystemRun_Sta~",
                table: "TestRunGroupEntity",
                columns: new[] { "OptimizationConsideredAt", "IsSystemRun", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TestRunGroupEntity_OptimizationConsideredAt_IsSystemRun_Sta~",
                table: "TestRunGroupEntity");

            migrationBuilder.DropColumn(
                name: "OptimizationConsideredAt",
                table: "TestRunGroupEntity");
        }
    }
}
