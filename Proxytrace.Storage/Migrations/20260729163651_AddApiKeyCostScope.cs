using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Proxytrace.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeyCostScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CostLimitEntity_Project_ProjectScope",
                table: "CostLimitEntity");

            migrationBuilder.AddColumn<Guid>(
                name: "ApiKey",
                table: "CostLimitEntity",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApiKeyId",
                table: "AgentCallEntity",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CostLimitEntity_ApiKey",
                table: "CostLimitEntity",
                column: "ApiKey");

            migrationBuilder.CreateIndex(
                name: "IX_CostLimitEntity_Project_ApiKey_ApiKeyScope",
                table: "CostLimitEntity",
                columns: new[] { "Project", "ApiKey" },
                unique: true,
                filter: "\"ApiKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CostLimitEntity_Project_ProjectScope",
                table: "CostLimitEntity",
                column: "Project",
                unique: true,
                filter: "\"Agent\" IS NULL AND \"ApiKey\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_CostLimitEntity_ApiKeyEntity_ApiKey",
                table: "CostLimitEntity",
                column: "ApiKey",
                principalTable: "ApiKeyEntity",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CostLimitEntity_ApiKeyEntity_ApiKey",
                table: "CostLimitEntity");

            migrationBuilder.DropIndex(
                name: "IX_CostLimitEntity_ApiKey",
                table: "CostLimitEntity");

            migrationBuilder.DropIndex(
                name: "IX_CostLimitEntity_Project_ApiKey_ApiKeyScope",
                table: "CostLimitEntity");

            migrationBuilder.DropIndex(
                name: "IX_CostLimitEntity_Project_ProjectScope",
                table: "CostLimitEntity");

            migrationBuilder.DropColumn(
                name: "ApiKey",
                table: "CostLimitEntity");

            migrationBuilder.DropColumn(
                name: "ApiKeyId",
                table: "AgentCallEntity");

            migrationBuilder.CreateIndex(
                name: "IX_CostLimitEntity_Project_ProjectScope",
                table: "CostLimitEntity",
                column: "Project",
                unique: true,
                filter: "\"Agent\" IS NULL");
        }
    }
}
