using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankApiAbp.Migrations
{
    /// <inheritdoc />
    public partial class Added_Transfer_Notification_Log : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankingTransferNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransferId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankingTransferNotifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankingTransferNotifications_CreationTime",
                table: "BankingTransferNotifications",
                column: "CreationTime");

            migrationBuilder.CreateIndex(
                name: "IX_BankingTransferNotifications_EventId",
                table: "BankingTransferNotifications",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_BankingTransferNotifications_TransferId",
                table: "BankingTransferNotifications",
                column: "TransferId");

            migrationBuilder.CreateIndex(
                name: "IX_BankingTransferNotifications_UserId",
                table: "BankingTransferNotifications",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankingTransferNotifications");
        }
    }
}
