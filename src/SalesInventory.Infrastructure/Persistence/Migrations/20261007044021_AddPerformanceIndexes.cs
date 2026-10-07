using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalesInventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockMovements_ProductId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrderItems_ProductId",
                table: "SalesOrderItems");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ProductId_CreatedAt_Id",
                table: "StockMovements",
                columns: new[] { "ProductId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_OrderDate_Id",
                table: "SalesOrders",
                columns: new[] { "OrderDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderItems_ProductId",
                table: "SalesOrderItems",
                column: "ProductId")
                .Annotation("SqlServer:Include", new[] { "SalesOrderId", "Quantity", "LineTotal" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_OrderDate_Id",
                table: "PurchaseOrders",
                columns: new[] { "OrderDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Name",
                table: "Products",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Products_SalePrice",
                table: "Products",
                column: "SalePrice");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockMovements_ProductId_CreatedAt_Id",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_OrderDate_Id",
                table: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrderItems_ProductId",
                table: "SalesOrderItems");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_OrderDate_Id",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_Products_Name",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_SalePrice",
                table: "Products");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ProductId",
                table: "StockMovements",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderItems_ProductId",
                table: "SalesOrderItems",
                column: "ProductId");
        }
    }
}
