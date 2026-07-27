using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Proxytrace.Storage.Migrations
{
    /// <summary>
    /// Widens <c>ModelProviderEntity.ApiKeyLookupHash</c> from 64 to 128 characters so it can hold
    /// the scheme-prefixed keyed blind index (<c>"hmac1:"</c> + 64 hex chars) alongside the bare
    /// 64-char hash that predates it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Up is a widening — no existing value can be truncated, and no data is rewritten here.</b>
    /// Existing rows keep their legacy unkeyed index and stay fully functional: the provider lookup
    /// matches both schemes, and <c>SecretsBackfillService.ReindexProviderKeysAsync</c> upgrades them
    /// on the next start.
    /// </para>
    /// <para>
    /// The tooling's "may result in the loss of data" warning refers to <c>Down</c>, which narrows
    /// the column back to 64 and would therefore truncate any already-upgraded keyed index. That is
    /// inherent to reverting this change; a rollback needs the rows re-indexed to the legacy scheme
    /// first, which is the same operation as re-keying and is not automated.
    /// </para>
    /// </remarks>
    public partial class WidenProviderApiKeyLookupHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ApiKeyLookupHash",
                table: "ModelProviderEntity",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ApiKeyLookupHash",
                table: "ModelProviderEntity",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);
        }
    }
}
