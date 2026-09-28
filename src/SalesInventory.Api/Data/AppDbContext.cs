using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SalesInventory.Api.Models;

namespace SalesInventory.Api.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Category>(entity =>
        {
            entity.Property(c => c.Name).IsRequired().HasMaxLength(200);
            entity.Property(c => c.Description).HasMaxLength(1000);

            // Seed sample categories for local testing
            entity.HasData(
                new Category { Id = 1, Name = "Đồ điện tử", Description = "Các sản phẩm điện tử, thiết bị công nghệ" },
                new Category { Id = 2, Name = "Văn phòng phẩm", Description = "Dụng cụ và vật tư văn phòng" },
                new Category { Id = 3, Name = "Gia dụng", Description = "Đồ dùng gia đình" },
                new Category { Id = 4, Name = "Thời trang", Description = "Quần áo, giày dép, phụ kiện thời trang" },
                new Category { Id = 5, Name = "Thực phẩm", Description = "Thực phẩm, đồ uống" }
            );
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(p => p.Name).IsRequired().HasMaxLength(200);
            entity.Property(p => p.Sku).IsRequired().HasMaxLength(50);
            entity.Property(p => p.Price).HasColumnType("decimal(18,2)");

            entity.HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(p => p.Supplier)
                .WithMany(s => s.Products)
                .HasForeignKey(p => p.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);

            // Seed sample products, each linked to a seeded category and the seeded default supplier
            var seedCreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            entity.HasData(
                new { Id = 1, Name = "Bàn phím cơ", Sku = "SKU-DT-001", Price = 550000m, StockQuantity = 50, CategoryId = 1, SupplierId = 1, CreatedAt = seedCreatedAt },
                new { Id = 2, Name = "Chuột không dây", Sku = "SKU-DT-002", Price = 250000m, StockQuantity = 100, CategoryId = 1, SupplierId = 1, CreatedAt = seedCreatedAt },
                new { Id = 3, Name = "Bút bi Thiên Long", Sku = "SKU-VPP-001", Price = 5000m, StockQuantity = 500, CategoryId = 2, SupplierId = 1, CreatedAt = seedCreatedAt },
                new { Id = 4, Name = "Giấy in A4", Sku = "SKU-VPP-002", Price = 65000m, StockQuantity = 200, CategoryId = 2, SupplierId = 1, CreatedAt = seedCreatedAt },
                new { Id = 5, Name = "Nồi cơm điện", Sku = "SKU-GD-001", Price = 850000m, StockQuantity = 30, CategoryId = 3, SupplierId = 1, CreatedAt = seedCreatedAt },
                new { Id = 6, Name = "Áo thun nam", Sku = "SKU-TT-001", Price = 150000m, StockQuantity = 80, CategoryId = 4, SupplierId = 1, CreatedAt = seedCreatedAt },
                new { Id = 7, Name = "Giày thể thao", Sku = "SKU-TT-002", Price = 750000m, StockQuantity = 40, CategoryId = 4, SupplierId = 1, CreatedAt = seedCreatedAt },
                new { Id = 8, Name = "Mì gói Hảo Hảo (thùng)", Sku = "SKU-TP-001", Price = 120000m, StockQuantity = 150, CategoryId = 5, SupplierId = 1, CreatedAt = seedCreatedAt }
            );
        });

        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.Property(s => s.Name).IsRequired().HasMaxLength(200);
            entity.Property(s => s.Phone).HasMaxLength(20);
            entity.Property(s => s.Email).HasMaxLength(200);
            entity.Property(s => s.Address).HasMaxLength(300);

            // Seed a default supplier so seeded products have a valid SupplierId (required FK)
            entity.HasData(
                new Supplier { Id = 1, Name = "Nhà cung cấp mặc định", Phone = "0900000000" }
            );
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.Property(c => c.FullName).IsRequired().HasMaxLength(200);
            entity.Property(c => c.Phone).HasMaxLength(20);
            entity.Property(c => c.Email).HasMaxLength(200);
            entity.Property(c => c.Address).HasMaxLength(300);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasOne(o => o.Customer)
                .WithMany(c => c.Orders)
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.Property(oi => oi.UnitPrice).HasColumnType("decimal(18,2)");

            entity.HasOne(oi => oi.Order)
                .WithMany(o => o.OrderItems)
                .HasForeignKey(oi => oi.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(oi => oi.Product)
                .WithMany()
                .HasForeignKey(oi => oi.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseOrder>(entity =>
        {
            entity.HasOne(po => po.Supplier)
                .WithMany()
                .HasForeignKey(po => po.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseOrderItem>(entity =>
        {
            entity.Property(poi => poi.UnitPrice).HasColumnType("decimal(18,2)");

            entity.HasOne(poi => poi.PurchaseOrder)
                .WithMany(po => po.PurchaseOrderItems)
                .HasForeignKey(poi => poi.PurchaseOrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(poi => poi.Product)
                .WithMany()
                .HasForeignKey(poi => poi.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
