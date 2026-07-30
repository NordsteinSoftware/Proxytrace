using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Proxytrace.Storage.Migrations
{
    /// <summary>
    /// Widens <c>MfaBackupCodeEntity.CodeHash</c> from 64 to 256 characters so it can hold the
    /// salted PBKDF2 hash that replaced the single-round unsalted SHA-256.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Up is a widening — no existing value is truncated or rewritten.</b> Codes issued before
    /// this change keep their 64-character hash and keep working: <c>MfaService</c> recognises the
    /// legacy form and verifies against it. They cannot be upgraded in place, because the raw code
    /// is unrecoverable — a stored hash could only be re-hashed at the moment the user redeems it,
    /// and redeeming consumes it. Existing codes therefore age out as they are used or when the user
    /// re-enrolls; every newly issued batch gets the stronger hash.
    /// </para>
    /// <para>
    /// The tooling's data-loss warning refers to <c>Down</c>, which narrows the column back to 64 and
    /// would truncate any PBKDF2 hash already written.
    /// </para>
    /// </remarks>
    public partial class WidenMfaBackupCodeHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "CodeHash",
                table: "MfaBackupCodeEntity",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "CodeHash",
                table: "MfaBackupCodeEntity",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);
        }
    }
}
