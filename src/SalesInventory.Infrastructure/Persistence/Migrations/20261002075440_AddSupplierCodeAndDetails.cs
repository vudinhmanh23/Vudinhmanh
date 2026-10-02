using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalesInventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierCodeAndDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                table: "Suppliers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Suppliers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            // Backfill unique codes for suppliers that already exist (e.g. SUP-007) before the unique index is created
            migrationBuilder.Sql("UPDATE Suppliers SET Code = 'SUP-' + RIGHT('000' + CAST(Id AS varchar(10)), 3)");

            migrationBuilder.AddColumn<string>(
                name: "ContactPerson",
                table: "Suppliers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Suppliers",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Suppliers",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.UpdateData(
                table: "Suppliers",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Code", "ContactPerson", "CreatedAt", "IsActive" },
                values: new object[] { "SUP-001", null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true });

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Code",
                table: "Suppliers",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Suppliers_Code",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "ContactPerson",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Suppliers");

            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                table: "Suppliers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);
        }
    }
}
