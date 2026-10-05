using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalesInventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrderNumberStatusAndStockRefRename : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ReferenceType",
                table: "StockMovements",
                newName: "RefType");

            migrationBuilder.RenameColumn(
                name: "ReferenceId",
                table: "StockMovements",
                newName: "RefId");

            migrationBuilder.RenameIndex(
                name: "IX_StockMovements_ReferenceType_ReferenceId",
                table: "StockMovements",
                newName: "IX_StockMovements_RefType_RefId");

            migrationBuilder.AddColumn<string>(
                name: "OrderNumber",
                table: "SalesOrders",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "SalesOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Backfill existing orders so the unique index can be created
            migrationBuilder.Sql(
                "UPDATE SalesOrders SET OrderNumber = 'SO-' + FORMAT(OrderDate, 'yyyyMMdd') + '-L' + CAST(Id AS nvarchar(10))");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_OrderNumber",
                table: "SalesOrders",
                column: "OrderNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_OrderNumber",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "OrderNumber",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "SalesOrders");

            migrationBuilder.RenameColumn(
                name: "RefType",
                table: "StockMovements",
                newName: "ReferenceType");

            migrationBuilder.RenameColumn(
                name: "RefId",
                table: "StockMovements",
                newName: "ReferenceId");

            migrationBuilder.RenameIndex(
                name: "IX_StockMovements_RefType_RefId",
                table: "StockMovements",
                newName: "IX_StockMovements_ReferenceType_ReferenceId");
        }
    }
}
