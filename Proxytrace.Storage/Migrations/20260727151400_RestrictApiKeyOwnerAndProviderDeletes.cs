using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Proxytrace.Storage.Migrations
{
    /// <inheritdoc />
    public partial class RestrictApiKeyOwnerAndProviderDeletes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApiKeyEntity_ModelProviderEntity_Provider",
                table: "ApiKeyEntity");

            migrationBuilder.DropForeignKey(
                name: "FK_ApiKeyEntity_UserEntity_Owner",
                table: "ApiKeyEntity");

            migrationBuilder.AddForeignKey(
                name: "FK_ApiKeyEntity_ModelProviderEntity_Provider",
                table: "ApiKeyEntity",
                column: "Provider",
                principalTable: "ModelProviderEntity",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ApiKeyEntity_UserEntity_Owner",
                table: "ApiKeyEntity",
                column: "Owner",
                principalTable: "UserEntity",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApiKeyEntity_ModelProviderEntity_Provider",
                table: "ApiKeyEntity");

            migrationBuilder.DropForeignKey(
                name: "FK_ApiKeyEntity_UserEntity_Owner",
                table: "ApiKeyEntity");

            migrationBuilder.AddForeignKey(
                name: "FK_ApiKeyEntity_ModelProviderEntity_Provider",
                table: "ApiKeyEntity",
                column: "Provider",
                principalTable: "ModelProviderEntity",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ApiKeyEntity_UserEntity_Owner",
                table: "ApiKeyEntity",
                column: "Owner",
                principalTable: "UserEntity",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
