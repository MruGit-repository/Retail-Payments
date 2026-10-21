using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPayments.Api.Data.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    TriggeredBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Created = table.Column<int>(type: "INTEGER", nullable: false),
                    Updated = table.Column<int>(type: "INTEGER", nullable: false),
                    Revoked = table.Column<int>(type: "INTEGER", nullable: false),
                    Restored = table.Column<int>(type: "INTEGER", nullable: false),
                    Finalized = table.Column<int>(type: "INTEGER", nullable: false),
                    Unchanged = table.Column<int>(type: "INTEGER", nullable: false),
                    Rejected = table.Column<int>(type: "INTEGER", nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceSystem = table.Column<string>(type: "TEXT", maxLength: 19, nullable: false),
                    TransactionId = table.Column<int>(type: "INTEGER", nullable: false),
                    CardNumber = table.Column<string>(type: "TEXT", maxLength: 19, nullable: false),
                    LocationCode = table.Column<string>(type: "TEXT", maxLength: 19, nullable: false),
                    ProductName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    TransactionTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TransactionRefId = table.Column<int>(type: "INTEGER", nullable: false),
                    ImportBatchId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangeType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FieldName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    OldValue = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    NewValue = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditEntries_ImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuditEntries_Transactions_TransactionRefId",
                        column: x => x.TransactionRefId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_ImportBatchId",
                table: "AuditEntries",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_TransactionRefId",
                table: "AuditEntries",
                column: "TransactionRefId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_SourceSystem_TransactionId",
                table: "Transactions",
                columns: new[] { "SourceSystem", "TransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Status",
                table: "Transactions",
                column: "Status");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropTable(
                name: "Transactions");
        }
    }
}
