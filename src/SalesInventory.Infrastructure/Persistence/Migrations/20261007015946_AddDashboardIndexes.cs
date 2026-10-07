using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalesInventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_Status_OrderDate",
                table: "SalesOrders",
                columns: new[] { "Status", "OrderDate" })
                .Annotation("SqlServer:Include", new[] { "TotalAmount" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_StockQuantity",
                table: "Products",
                column: "StockQuantity")
                .Annotation("SqlServer:Include", new[] { "IsActive", "LowStockThreshold", "PurchasePrice", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_Status_OrderDate",
                table: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_Products_StockQuantity",
                table: "Products");
        }
    }
}
