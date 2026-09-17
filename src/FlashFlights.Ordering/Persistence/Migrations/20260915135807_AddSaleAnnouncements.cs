using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlashFlights.Ordering.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleAnnouncements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SaleAnnouncements",
                columns: table => new
                {
                    FlightId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleEndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AnnouncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleAnnouncements", x => x.FlightId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SaleAnnouncements");
        }
    }
}
