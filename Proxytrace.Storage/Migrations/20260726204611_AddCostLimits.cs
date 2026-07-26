using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Proxytrace.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddCostLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CostLimitEntity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Project = table.Column<Guid>(type: "uuid", nullable: false),
                    Agent = table.Column<Guid>(type: "uuid", nullable: true),
                    SoftLimitEur = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    HardLimitEur = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostLimitEntity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CostLimitEntity_AgentEntity_Agent",
                        column: x => x.Agent,
                        principalTable: "AgentEntity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CostLimitEntity_ProjectEntity_Project",
                        column: x => x.Project,
                        principalTable: "ProjectEntity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CostLimitBreachEntity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CostLimit = table.Column<Guid>(type: "uuid", nullable: false),
                    MonthStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Threshold = table.Column<int>(type: "integer", nullable: false),
                    SpendEur = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostLimitBreachEntity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CostLimitBreachEntity_CostLimitEntity_CostLimit",
                        column: x => x.CostLimit,
                        principalTable: "CostLimitEntity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CostLimitBreachEntity_CostLimit_MonthStart_Threshold",
                table: "CostLimitBreachEntity",
                columns: new[] { "CostLimit", "MonthStart", "Threshold" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CostLimitBreachEntity_MonthStart",
                table: "CostLimitBreachEntity",
                column: "MonthStart");

            migrationBuilder.CreateIndex(
                name: "IX_CostLimitEntity_Agent",
                table: "CostLimitEntity",
                column: "Agent");

            migrationBuilder.CreateIndex(
                name: "IX_CostLimitEntity_Enabled",
                table: "CostLimitEntity",
                column: "Enabled");

            migrationBuilder.CreateIndex(
                name: "IX_CostLimitEntity_Project_Agent_AgentScope",
                table: "CostLimitEntity",
                columns: new[] { "Project", "Agent" },
                unique: true,
                filter: "\"Agent\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CostLimitEntity_Project_ProjectScope",
                table: "CostLimitEntity",
                column: "Project",
                unique: true,
                filter: "\"Agent\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CostLimitBreachEntity");

            migrationBuilder.DropTable(
                name: "CostLimitEntity");
        }
    }
}
