using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalesInventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseOrderCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "PurchaseOrders",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            // Backfill existing orders (they got "" above) with PO-yyyyMMdd-NNN, numbered per OrderDate by Id,
            // so the unique index below can be created
            migrationBuilder.Sql(@"
                UPDATE po
                SET Code = 'PO-' + CONVERT(char(8), po.OrderDate, 112) + '-' + RIGHT('000' + CAST(n.Seq AS varchar(10)), 3)
                FROM PurchaseOrders po
                JOIN (
                    SELECT Id, ROW_NUMBER() OVER (PARTITION BY CAST(OrderDate AS date) ORDER BY Id) AS Seq
                    FROM PurchaseOrders
                ) n ON n.Id = po.Id;");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_Code",
                table: "PurchaseOrders",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_Code",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "PurchaseOrders");
        }
    }
}
