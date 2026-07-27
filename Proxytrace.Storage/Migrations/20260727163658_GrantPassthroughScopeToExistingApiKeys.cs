using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Proxytrace.Storage.Migrations
{
    /// <summary>
    /// Grants the new <c>ApiKeyScopes.Passthrough</c> (32) to every existing key that can already use
    /// the proxy, so upgrading does not break the documented "reach other upstream endpoints" setup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pass-through — relaying any method and any path to the provider's host with the organisation's
    /// real upstream credential — used to be admitted by the <c>Ingestion</c> scope alone. It now has
    /// its own scope, because its reach differs in kind from capturing inference traffic and is
    /// neither detected, traced, nor audited.
    /// </para>
    /// <para>
    /// <b>No schema change: this migration is data only.</b> Existing keys keep exactly the behaviour
    /// they have today (grandfathered), while newly minted keys must request the scope explicitly —
    /// so least privilege applies going forward without an upgrade breaking anyone mid-flight.
    /// </para>
    /// <para>
    /// Scoped to keys holding <c>Ingestion</c> (1): a key that cannot reach the proxy at all has no
    /// use for pass-through, and granting it would widen that key for no reason.
    /// </para>
    /// </remarks>
    public partial class GrantPassthroughScopeToExistingApiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "ApiKeyEntity"
                SET "Scopes" = "Scopes" | 32
                WHERE ("Scopes" & 1) = 1 AND ("Scopes" & 32) = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Clearing the bit is the exact inverse. A key granted the scope deliberately after this
            // migration ran also loses it, which is the correct behaviour for reverting to a schema
            // where the scope does not exist.
            migrationBuilder.Sql(
                """
                UPDATE "ApiKeyEntity"
                SET "Scopes" = "Scopes" & ~32
                WHERE ("Scopes" & 32) = 32;
                """);
        }
    }
}
