using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Proxytrace.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ScopeId",
                table: "AgentCallEntity",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ScopeEntity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScopeEntity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScopeEntity_ProjectEntity_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "ProjectEntity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScopeAgentVersionEntity",
                columns: table => new
                {
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TraceCount = table.Column<int>(type: "integer", nullable: false),
                    TotalTokens = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScopeAgentVersionEntity", x => new { x.ScopeId, x.AgentVersionId });
                    table.ForeignKey(
                        name: "FK_ScopeAgentVersionEntity_AgentVersionEntity_AgentVersionId",
                        column: x => x.AgentVersionId,
                        principalTable: "AgentVersionEntity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScopeAgentVersionEntity_ScopeEntity_ScopeId",
                        column: x => x.ScopeId,
                        principalTable: "ScopeEntity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentCallEntity_ScopeId_CreatedAt",
                table: "AgentCallEntity",
                columns: new[] { "ScopeId", "CreatedAt" },
                filter: "\"ScopeId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ScopeAgentVersionEntity_AgentVersionId",
                table: "ScopeAgentVersionEntity",
                column: "AgentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScopeEntity_ProjectId_ExternalKey",
                table: "ScopeEntity",
                columns: new[] { "ProjectId", "ExternalKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScopeAgentVersionEntity");

            migrationBuilder.DropTable(
                name: "ScopeEntity");

            migrationBuilder.DropIndex(
                name: "IX_AgentCallEntity_ScopeId_CreatedAt",
                table: "AgentCallEntity");

            migrationBuilder.DropColumn(
                name: "ScopeId",
                table: "AgentCallEntity");
        }
    }
}
