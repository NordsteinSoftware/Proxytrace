using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Proxytrace.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SessionId",
                table: "AgentCallEntity",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SessionEntity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TraceCount = table.Column<int>(type: "integer", nullable: false),
                    TotalTokens = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionEntity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionEntity_ProjectEntity_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "ProjectEntity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentCallEntity_SessionId_CreatedAt",
                table: "AgentCallEntity",
                columns: new[] { "SessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionEntity_ProjectId_ExternalKey",
                table: "SessionEntity",
                columns: new[] { "ProjectId", "ExternalKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionEntity_ProjectId_LastActivityAt",
                table: "SessionEntity",
                columns: new[] { "ProjectId", "LastActivityAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionEntity");

            migrationBuilder.DropIndex(
                name: "IX_AgentCallEntity_SessionId_CreatedAt",
                table: "AgentCallEntity");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "AgentCallEntity");
        }
    }
}
