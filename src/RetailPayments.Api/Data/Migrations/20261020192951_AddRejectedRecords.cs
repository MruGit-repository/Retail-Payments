using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPayments.Api.Data.Migrations
{
    public partial class AddRejectedRecords : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RejectedRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ImportBatchId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceSystem = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TransactionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    RejectedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RejectedRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RejectedRecords_ImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RejectedRecords_ImportBatchId",
                table: "RejectedRecords",
                column: "ImportBatchId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RejectedRecords");
        }
    }
}
