using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlashFlights.Catalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReferenceFare : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ReferenceFare",
                table: "Flights",
                type: "TEXT",
                precision: 10,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReferenceFare",
                table: "Flights");
        }
    }
}
