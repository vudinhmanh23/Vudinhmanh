# SalesInventory

Web API (ASP.NET Core, .NET 8) cho hệ thống quản lý bán hàng và kho.

## Cấu trúc dự án

```
SalesInventory.sln
src/
  SalesInventory.Api/       # Web API project
    Controllers/            # API controllers
    Program.cs               # Application entry point
    appsettings.json          # Cấu hình chung
    appsettings.Development.json
```

## Công nghệ

- ASP.NET Core 8 Web API
- Swagger / OpenAPI (Swashbuckle.AspNetCore) cho tài liệu API

## Chạy dự án

```bash
dotnet restore
dotnet run --project src/SalesInventory.Api
```

Khi chạy ở môi trường Development, Swagger UI sẽ khả dụng để khám phá và thử API.

## Tài liệu dự án

- [`docs/SRS.md`](docs/SRS.md) — Đặc tả yêu cầu phần mềm
- [`docs/UseCases.md`](docs/UseCases.md) — Sơ đồ use case và user stories
- [`docs/database-design.md`](docs/database-design.md) — Thiết kế cơ sở dữ liệu (ERD)
- [`docs/architecture.md`](docs/architecture.md) — Kiến trúc phân lớp (Domain/Application/Infrastructure/Api) và chiều phụ thuộc
- [`docs/adr/0001-kien-truc-du-an.md`](docs/adr/0001-kien-truc-du-an.md) — ADR: lý do chọn kiến trúc, các phương án đã cân nhắc, hệ quả

## Cấu hình bí mật (secrets)

Không commit các file chứa API key, chuỗi kết nối,... Sử dụng:

- `appsettings.*.Local.json` (đã bị ignore) cho cấu hình local có secret
- `.env` / biến môi trường cho secret khi triển khai
