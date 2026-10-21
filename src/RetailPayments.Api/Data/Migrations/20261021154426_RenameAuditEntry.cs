using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPayments.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameAuditEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditEntries_Transactions_TransactionRefId",
                table: "AuditEntries");

            migrationBuilder.RenameColumn(
                name: "TransactionRefId",
                table: "AuditEntries",
                newName: "TransactionKey");

            migrationBuilder.RenameIndex(
                name: "IX_AuditEntries_TransactionRefId",
                table: "AuditEntries",
                newName: "IX_AuditEntries_TransactionKey");

            migrationBuilder.AddForeignKey(
                name: "FK_AuditEntries_Transactions_TransactionKey",
                table: "AuditEntries",
                column: "TransactionKey",
                principalTable: "Transactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditEntries_Transactions_TransactionKey",
                table: "AuditEntries");

            migrationBuilder.RenameColumn(
                name: "TransactionKey",
                table: "AuditEntries",
                newName: "TransactionRefId");

            migrationBuilder.RenameIndex(
                name: "IX_AuditEntries_TransactionKey",
                table: "AuditEntries",
                newName: "IX_AuditEntries_TransactionRefId");

            migrationBuilder.AddForeignKey(
                name: "FK_AuditEntries_Transactions_TransactionRefId",
                table: "AuditEntries",
                column: "TransactionRefId",
                principalTable: "Transactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
