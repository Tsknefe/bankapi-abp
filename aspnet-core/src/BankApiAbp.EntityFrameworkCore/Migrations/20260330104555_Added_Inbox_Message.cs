using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankApiAbp.Migrations
{
    /// <inheritdoc />
    public partial class Added_Inbox_Message : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankingInboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ConsumerName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankingInboxMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankingInboxMessages_ConsumerName_EventId",
                table: "BankingInboxMessages",
                columns: new[] { "ConsumerName", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankingInboxMessages_LastAttemptTime",
                table: "BankingInboxMessages",
                column: "LastAttemptTime");

            migrationBuilder.CreateIndex(
                name: "IX_BankingInboxMessages_ProcessedAt",
                table: "BankingInboxMessages",
                column: "ProcessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_BankingInboxMessages_Status",
                table: "BankingInboxMessages",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankingInboxMessages");
        }
    }
}
