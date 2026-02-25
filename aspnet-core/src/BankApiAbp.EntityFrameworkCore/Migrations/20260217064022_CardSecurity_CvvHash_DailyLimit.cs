using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankApiAbp.Migrations
{
    /// <inheritdoc />
    public partial class CardSecurity_CvvHash_DailyLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Cvv",
                table: "DebitCards");

            migrationBuilder.AddColumn<string>(
                name: "CvvHash",
                table: "DebitCards",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "DailyLimit",
                table: "DebitCards",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CvvHash",
                table: "DebitCards");

            migrationBuilder.DropColumn(
                name: "DailyLimit",
                table: "DebitCards");

            migrationBuilder.AddColumn<string>(
                name: "Cvv",
                table: "DebitCards",
                type: "character varying(4)",
                maxLength: 4,
                nullable: false,
                defaultValue: "");
        }
    }
}
