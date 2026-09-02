using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlashFlights.Ordering.Persistence.Migrations
{
    /// <summary>
    /// Enforces the append-only ledger in the database as well as in
    /// <see cref="OrderingDbContext"/>.
    ///
    /// The change-tracker guard only sees work that goes through SaveChanges:
    /// <c>ExecuteUpdate</c>, <c>ExecuteDelete</c>, and raw SQL walk straight
    /// past it. ADR-0001 rests on this ledger being an auditable record of what
    /// actually happened, so the rule belongs where nothing can route around
    /// it. Statement-level, so it fires even when the statement matches no rows.
    /// </summary>
    public partial class SeatMovementLedgerAppendOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION reject_seat_movement_rewrite()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION
                        'The SeatMovement ledger is append-only; % on "SeatMovements" is not permitted', TG_OP;
                END;
                $$;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER seat_movements_are_append_only
                BEFORE UPDATE OR DELETE ON "SeatMovements"
                FOR EACH STATEMENT
                EXECUTE FUNCTION reject_seat_movement_rewrite();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """DROP TRIGGER IF EXISTS seat_movements_are_append_only ON "SeatMovements";""");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS reject_seat_movement_rewrite();");
        }
    }
}
