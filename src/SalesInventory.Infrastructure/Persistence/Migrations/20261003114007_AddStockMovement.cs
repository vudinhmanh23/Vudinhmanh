using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalesInventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStockMovement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RefId",
                table: "StockMovements",
                newName: "ReferenceId");

            migrationBuilder.RenameColumn(
                name: "Reason",
                table: "StockMovements",
                newName: "ReferenceType");

            migrationBuilder.RenameColumn(
                name: "ChangeQuantity",
                table: "StockMovements",
                newName: "Quantity");

            migrationBuilder.RenameIndex(
                name: "IX_StockMovements_Reason_RefId",
                table: "StockMovements",
                newName: "IX_StockMovements_ReferenceType_ReferenceId");

            migrationBuilder.AddColumn<int>(
                name: "MovementType",
                table: "StockMovements",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "StockMovements",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "PurchaseOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // EF treated Reason as renamed to ReferenceType, so ReferenceType currently holds the OLD Reason text
            // ('Purchase' / 'PurchaseCancel'). Convert those rows to the new shape (all rows reference purchase orders).
            // The right-hand sides below are evaluated against the pre-update row, so the order of assignments is safe.
            migrationBuilder.Sql(@"
                UPDATE StockMovements
                SET MovementType = CASE ReferenceType WHEN 'PurchaseCancel' THEN 1 ELSE 0 END,
                    Note = CASE ReferenceType WHEN 'PurchaseCancel' THEN 'Deleted purchase order' ELSE 'Purchase order created' END,
                    ReferenceType = 'PurchaseOrder';");

            // Orders created before this migration already increased stock when they were created,
            // so they are Approved; only orders created from now on start as Draft.
            migrationBuilder.Sql("UPDATE PurchaseOrders SET Status = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the old Reason text (held by ReferenceType after the rename back) before MovementType is dropped
            migrationBuilder.Sql(@"
                UPDATE StockMovements
                SET ReferenceType = CASE MovementType WHEN 1 THEN 'PurchaseCancel' ELSE 'Purchase' END;");

            migrationBuilder.DropColumn(
                name: "MovementType",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "PurchaseOrders");

            migrationBuilder.RenameColumn(
                name: "ReferenceType",
                table: "StockMovements",
                newName: "Reason");

            migrationBuilder.RenameColumn(
                name: "ReferenceId",
                table: "StockMovements",
                newName: "RefId");

            migrationBuilder.RenameColumn(
                name: "Quantity",
                table: "StockMovements",
                newName: "ChangeQuantity");

            migrationBuilder.RenameIndex(
                name: "IX_StockMovements_ReferenceType_ReferenceId",
                table: "StockMovements",
                newName: "IX_StockMovements_Reason_RefId");
        }
    }
}
