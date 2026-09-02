using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlashFlights.Catalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalogSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Flights",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FlightNumber = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Origin = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Destination = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    DepartureAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FlashPrice = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    SaleStartsAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SaleEndsAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flights", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FlightSeatCounts",
                columns: table => new
                {
                    FlightId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TotalSeats = table.Column<int>(type: "INTEGER", nullable: false),
                    AvailableSeats = table.Column<int>(type: "INTEGER", nullable: false),
                    HeldSeats = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfirmedSeats = table.Column<int>(type: "INTEGER", nullable: false),
                    LastMovementAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlightSeatCounts", x => x.FlightId);
                    table.ForeignKey(
                        name: "FK_FlightSeatCounts_Flights_FlightId",
                        column: x => x.FlightId,
                        principalTable: "Flights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Flights_SaleStartsAt",
                table: "Flights",
                column: "SaleStartsAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FlightSeatCounts");

            migrationBuilder.DropTable(
                name: "Flights");
        }
    }
}
