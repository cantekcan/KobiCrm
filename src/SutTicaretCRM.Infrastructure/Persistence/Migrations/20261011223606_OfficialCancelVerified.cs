using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SutTicaretCRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OfficialCancelVerified : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OfficialCancelVerified",
                table: "Invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OfficialCancelVerified",
                table: "Invoices");
        }
    }
}
