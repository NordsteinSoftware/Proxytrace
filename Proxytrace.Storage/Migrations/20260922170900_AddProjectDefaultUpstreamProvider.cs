using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Proxytrace.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectDefaultUpstreamProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultUpstreamProviderId",
                table: "ProjectEntity",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEntity_DefaultUpstreamProviderId",
                table: "ProjectEntity",
                column: "DefaultUpstreamProviderId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectEntity_ModelProviderEntity_DefaultUpstreamProviderId",
                table: "ProjectEntity",
                column: "DefaultUpstreamProviderId",
                principalTable: "ModelProviderEntity",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectEntity_ModelProviderEntity_DefaultUpstreamProviderId",
                table: "ProjectEntity");

            migrationBuilder.DropIndex(
                name: "IX_ProjectEntity_DefaultUpstreamProviderId",
                table: "ProjectEntity");

            migrationBuilder.DropColumn(
                name: "DefaultUpstreamProviderId",
                table: "ProjectEntity");
        }
    }
}
