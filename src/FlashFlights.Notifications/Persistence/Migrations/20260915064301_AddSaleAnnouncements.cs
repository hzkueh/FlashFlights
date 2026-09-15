using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlashFlights.Notifications.Persistence.Migrations
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
                    FlightId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AnnouncedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
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
