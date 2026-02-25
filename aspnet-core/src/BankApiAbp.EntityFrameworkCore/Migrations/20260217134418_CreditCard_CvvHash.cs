using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankApiAbp.Migrations
{
    /// <inheritdoc />
    public partial class CreditCard_CvvHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Cvv",
                table: "CreditCards");

            migrationBuilder.AddColumn<string>(
                name: "CvvHash",
                table: "CreditCards",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CvvHash",
                table: "CreditCards");

            migrationBuilder.AddColumn<string>(
                name: "Cvv",
                table: "CreditCards",
                type: "character varying(4)",
                maxLength: 4,
                nullable: false,
                defaultValue: "");
        }
    }
}
