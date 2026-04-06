using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankApiAbp.Migrations
{
    /// <inheritdoc />
    public partial class AddInboxRetryAndReplay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeadLetterReason",
                table: "BankingInboxMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeadLetteredAt",
                table: "BankingInboxMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastErrorCode",
                table: "BankingInboxMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxRetryCount",
                table: "BankingInboxMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextRetryTime",
                table: "BankingInboxMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayloadJson",
                table: "BankingInboxMessages",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeadLetterReason",
                table: "BankingInboxMessages");

            migrationBuilder.DropColumn(
                name: "DeadLetteredAt",
                table: "BankingInboxMessages");

            migrationBuilder.DropColumn(
                name: "LastErrorCode",
                table: "BankingInboxMessages");

            migrationBuilder.DropColumn(
                name: "MaxRetryCount",
                table: "BankingInboxMessages");

            migrationBuilder.DropColumn(
                name: "NextRetryTime",
                table: "BankingInboxMessages");

            migrationBuilder.DropColumn(
                name: "PayloadJson",
                table: "BankingInboxMessages");
        }
    }
}
