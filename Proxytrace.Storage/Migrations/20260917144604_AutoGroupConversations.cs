using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Proxytrace.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AutoGroupConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContinuationHash",
                table: "AgentCallEntity",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParentContinuationHash",
                table: "AgentCallEntity",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentCallEntity_ContinuationHash",
                table: "AgentCallEntity",
                column: "ContinuationHash");

            migrationBuilder.CreateIndex(
                name: "IX_AgentCallEntity_ParentContinuationHash",
                table: "AgentCallEntity",
                column: "ParentContinuationHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AgentCallEntity_ContinuationHash",
                table: "AgentCallEntity");

            migrationBuilder.DropIndex(
                name: "IX_AgentCallEntity_ParentContinuationHash",
                table: "AgentCallEntity");

            migrationBuilder.DropColumn(
                name: "ContinuationHash",
                table: "AgentCallEntity");

            migrationBuilder.DropColumn(
                name: "ParentContinuationHash",
                table: "AgentCallEntity");
        }
    }
}
