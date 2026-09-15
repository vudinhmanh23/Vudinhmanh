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

## Cấu hình bí mật (secrets)

Không commit các file chứa API key, chuỗi kết nối,... Sử dụng:

- `appsettings.*.Local.json` (đã bị ignore) cho cấu hình local có secret
- `.env` / biến môi trường cho secret khi triển khai
