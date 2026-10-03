# Phân quyền (Authorization)

API dùng JWT Bearer và ASP.NET Core Identity với 3 vai trò: `Admin`, `Kho` (quản lý kho), `BanHang` (nhân viên bán hàng).
Mã trạng thái chung: **401** khi không có token hoặc token không hợp lệ/hết hạn, **403** khi đã đăng nhập nhưng sai vai trò.

## Ma trận quyền

| Nhóm endpoint | Không token | BanHang | Kho | Admin |
|---|---|---|---|---|
| `GET /api/products`, `GET /api/categories` | 401 | 200 | 200 | 200 |
| `POST/PUT/DELETE /api/products` (nhập/điều chỉnh tồn kho) | 401 | 403 | OK | OK |
| `POST/PUT/DELETE /api/categories` | 401 | 403 | OK | OK |
| `/api/suppliers` (CRUD) | 401 | 403 | OK | OK |
| `/api/purchase-orders` (đơn nhập kho, gồm `POST /{id}/approve`) | 401 | 403 | OK | OK |
| `GET/POST /api/salesorders` (đơn bán) | 401 | OK | OK | OK |
| `DELETE /api/salesorders/{id}` | 401 | 403 | 403 | OK |
| `/api/users`, `/api/admin/employees`, `POST /api/auth/assign-role` | 401 | 403 | 403 | OK |

Quy tắc "Admin hoặc Kho" cho nghiệp vụ tồn kho được định nghĩa một lần trong policy `CanManageInventory` (`Program.cs`) và áp bằng `[Authorize(Policy = "CanManageInventory")]`.
Các nhóm còn lại dùng `[Authorize(Roles = "...")]`.

## Kiểm tra mẫu: `POST /api/suppliers`

Mỗi vai trò đăng ký, đăng nhập lấy JWT thật rồi gọi cùng một request:

| Vai trò | Status code | Kết luận |
|---|---|---|
| Admin | 201 Created | được phép |
| Kho (Warehouse) | 201 Created | được phép |
| BanHang (Sales) | 403 Forbidden | bị chặn |
| Không token | 401 Unauthorized | bị chặn |

Kịch bản này được kiểm tra tự động trong `tests/SalesInventory.Api.Tests/SuppliersRoleTableTests.cs`.
Toàn bộ ma trận được kiểm tra trong `RoleAccessMatrixTests.cs`, `WriteAuthorizationTests.cs` và các `*AuthorizationTests.cs`.

## Thử trên Swagger UI

1. Chạy app ở môi trường Development rồi mở `/swagger`.
2. Gọi `POST /api/auth/login`, copy giá trị `token`.
3. Bấm **Authorize**, dán chỉ chuỗi token (không gõ chữ `Bearer`).
4. Chỉ các endpoint có `[Authorize]` mới hiện ổ khóa (xem `Swagger/AuthorizeOperationFilter.cs`).
5. Muốn đổi vai trò: **Authorize** → **Logout** → dán token mới.

Cấu hình `Jwt:Key` và tài khoản Admin mặc định (`SeedAdmin:*`) đặt trong user-secrets, không đặt trong `appsettings.json`.
