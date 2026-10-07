# CHƯƠNG 5. KIỂM THỬ

> **Nguồn số liệu.** Các con số trong chương lấy từ một lần chạy `dotnet test` toàn solution ngày 07/10/2026, trên nhánh `feat/stock-ledger-and-race-safety`, Docker đang chạy. Cần chạy lại và cập nhật số liệu nếu code hoặc test thay đổi trước khi nộp. Tên lớp test lấy từ thư mục `tests/`.

## 5.1. Chiến lược kiểm thử

Hệ thống có hai project test (xUnit):

| Project | Loại | Phạm vi |
|---|---|---|
| `tests/SalesInventory.Tests` | Kiểm thử đơn vị | Nghiệp vụ kho: nhập, bán, biên giá trị (`Inventory/*Tests.cs`), dùng `InventoryHarness` và `RecordingUnitOfWork` trong `Support/` |
| `tests/SalesInventory.Api.Tests` | Kiểm thử tích hợp | Gọi API qua `CustomWebApplicationFactory` (host thật trong bộ nhớ) và kiểm thử service trên cơ sở dữ liệu |

Cơ sở dữ liệu của các test tích hợp được chọn qua `TestDatabase.cs`. Mặc định dùng SQL Server thật trong container (Testcontainers, cần Docker). Đặt `SALESINVENTORY_TESTS_DB=InMemory` để chạy không cần Docker (theo `CLAUDE.md`). Lý do cần SQL Server thật: các test về tranh chấp đồng thời cần `rowversion`, khóa dòng và giao dịch thật, mà provider InMemory hay SQLite không mô phỏng được (chú thích đầu `Services/SalesOrderConcurrencyTests.cs`). Các test loại này dùng thuộc tính `DockerSqlFact` và tự bị bỏ qua (`Skip`) nếu máy không có Docker.

## 5.2. Kết quả chạy kiểm thử

Lệnh: `dotnet test` tại thư mục gốc của solution.

| Project | Tổng | Đạt | Lỗi | Bỏ qua | Thời gian |
|---|--:|--:|--:|--:|---|
| `SalesInventory.Tests` | 57 | 57 | 0 | 0 | 122 ms |
| `SalesInventory.Api.Tests` | 592 | 591 | 0 | 1 | 1 phút 49 giây |
| **Cộng** | **649** | **648** | **0** | **1** | |

*Bảng 5.1. Kết quả một lần chạy `dotnet test`. Số "tổng" tính cả các ca sinh ra từ `[Theory]`, vì thế lớn hơn số phương thức test.*

Về test bị bỏ qua: đó là `LiveInjectionTests.The_real_model_does_not_hand_over_the_rules_or_any_secret`. Test này gọi mô hình thật của Anthropic nên chỉ chạy khi đặt biến môi trường `SALES_LIVE_ANTHROPIC_KEY` (chú thích trong `InjectionCorpusTests.cs`). Nó không được chạy trong lần này nên **chưa có kết luận nào về hành vi của mô hình thật**.

Vì chỉ có một test bị bỏ qua và các test cần Docker tự bỏ qua khi thiếu Docker, có thể suy ra các test SQL Server thật đã chạy. Đây là suy luận từ số liệu, chưa kiểm tra từng test; nếu cần chắc chắn, chạy `dotnet test --logger "console;verbosity=normal"` và đối chiếu tên các test thuộc `SalesOrderConcurrencyTests` và `RealSqlServerTests`.

Trình biên dịch và NuGet ghi các cảnh báo (không làm test lỗi): gói `AutoMapper` 14.0.0 có lỗ hổng mức cao đã công bố (NU1903), cảnh báo null ở `ChatStreamTests.cs`, và `MsSqlBuilder()` đã lỗi thời trong `TestDatabase.cs`. Cảnh báo về `AutoMapper` nên được nêu ở Chương 8.

## 5.3. Các nhóm kiểm thử

Danh sách dưới đây nêu tên lớp test có trong thư mục `tests/`. Chưa thống kê số ca theo từng nhóm; nếu cần, lấy từ báo cáo `dotnet test` chi tiết.

### 5.3.1. Nghiệp vụ kho và đơn hàng

| Lớp test | Nội dung chính (suy từ tên và chú thích; cần đọc test để mô tả chi tiết) |
|---|---|
| `Inventory/PurchaseOrderStockTests`, `PurchaseOrderBoundaryTests` | Duyệt, hủy phiếu nhập và giá trị biên |
| `Inventory/SalesOrderStockTests`, `StockMovementServiceTests` | Bán hàng trừ kho, điều chỉnh kho |
| `SalesOrdersStockTests`, `SalesOrderHttpTests`, `PurchaseOrdersTests` | Qua HTTP |
| `Services/SalesOrderServiceTests`, `PurchaseOrderServiceTests`, `StockAdjustmentServiceTests`, `LowStockReportTests` | Tầng service |
| `InventoryEndpointsTests`, `DashboardTests` | Điểm cuối kho và dashboard |

### 5.3.2. Đồng thời và toàn vẹn dữ liệu

`Services/SalesOrderConcurrencyTests` và `RealSqlServerTests` kiểm tra trên SQL Server thật. Đây là nhóm chứng minh cơ chế `RowVersion`, giao dịch và vòng thử lại (tối đa 5 lần) trong `SalesOrderService`. Nên đọc từng test để ghi đúng kịch bản (ví dụ số request đồng thời, kết quả mong đợi) trước khi đưa vào báo cáo.

### 5.3.3. Phân quyền và xác thực

`RoleAccessMatrixTests`, `WriteAuthorizationTests`, `ProductsAuthorizationTests`, `PurchaseOrdersAuthorizationTests`, `SuppliersRoleTableTests`, `AuthMeTests`, `UserAdminTests`, `AdminEmployeesTests`, `AdminSeederTests`, `SwaggerSecurityTests`, `EndpointInventoryTests`.

> **Lưu ý.** Một test có thể xác nhận hành vi hiện tại chứ không chứng minh hành vi đó là đúng. Bộ test từng không phát hiện lỗi `POST /api/auth/register` cho người lạ tự chọn vai trò `Admin`: chính `AuthTestHelper` dùng lỗ hổng này để tạo tài khoản Admin và Kho cho hàng chục test. Lỗi được phát hiện khi đọc code. Sau khi sửa, `AuthTestHelper` tạo tài khoản Admin/Kho qua `POST /api/admin/users` bằng một Admin có sẵn (`CustomWebApplicationFactory` đặt `SeedAdmin:*`), và `RegistrationSecurityTests` kiểm tra quy tắc mới.

Ba lớp test thêm sau khi sửa ba lỗi tìm thấy lúc rà soát báo cáo (18 ca, gồm cả các ca sinh từ `[Theory]`):

| Lớp test | Kiểm tra | Lỗi cũ (đã xác nhận khi gỡ bản sửa: 10 ca đỏ) |
|---|---|---|
| `RegistrationSecurityTests` | Người lạ đăng ký `Admin` hoặc `Kho` bị từ chối 403 và không tạo tài khoản; đăng ký không chọn vai trò, hoặc `BanHang`, thành công với đúng một vai trò `BanHang`; vai trò không tồn tại trả 400; `BanHang` đã đăng nhập không tự nâng quyền; `Admin` đăng nhập tạo được tài khoản `Kho` | Người lạ đăng ký được tài khoản `Admin` |
| `ProductDeleteTests` | Xóa sản phẩm chưa có chứng từ trả 204; sản phẩm không tồn tại trả 404; sản phẩm đã nằm trong phiếu nhập, đơn bán, hoặc chỉ có dòng sổ kho trả 409 (`application/problem+json`) và sản phẩm vẫn còn | Xóa sản phẩm có chứng từ làm khóa ngoại thất bại và API không trả 409 (ba ca xóa bị từ chối đều đỏ khi gỡ bản sửa) |
| `StockLedgerReconciliationTests` | Tạo sản phẩm có tồn đầu kỳ ghi đúng một dòng `Adjustment` (`RefType = "InitialStock"`); tồn bằng 0 thì không ghi dòng nào; sửa sản phẩm bỏ qua trường `quantity` và không ghi dòng; một form mở trước một đơn bán không ghi đè được đơn bán đó; sau chuỗi tạo, nhập, bán, điều chỉnh, sửa thì tổng `Quantity` của sổ kho bằng tồn và dòng cuối có `StockAfter` bằng tồn | Tồn kho đổi mà không có dòng sổ giải thích; form cũ ghi đè đơn bán (4 trên 5 ca đỏ) |

Cách kiểm chứng: gỡ tạm các thay đổi trong `src/` bằng `git stash`, chạy các lớp test mới (10 ca đỏ), rồi khôi phục (toàn bộ xanh).

### 5.3.4. Sản phẩm, nhà cung cấp, khách hàng

`ProductsQueryTests`, `ProductsSearchTests`, `ProductsLookupAndUniquenessTests`, `ProductsUpdateKeepsCostPriceTests`, `ProductCreateDtoValidatorTests`, `ProductImageUploadTests`, `ProductImageFromInternetTests`, `SuppliersCodeTests`, `CustomersTests`, `Services/CategoryServiceTests`.

### 5.3.5. Bảo mật và vận hành

`SqlInjectionTests`, `HardeningTests`, `ProductionEnvironmentVariableTests`, `SecurityLoggingTests`, `LoggingTests`, `GlobalExceptionHandlingMiddlewareTests`, `RateLimitTests`.

### 5.3.6. Trợ lý AI

`AssistantTests`, `AssistantToolsTests`, `RagTests`, `RagStreamingTests`, `ChatStreamTests`, `ChatCancelTests`, `StreamingIncrementalTests`, `BlazorChatClientTests`, `CostControlTests`, `AiSafetyConfigTests`, `PromptInjectionTests`, `InjectionCorpusTests`.

**Giới hạn cần nói rõ khi bảo vệ.** Theo chú thích trong `InjectionCorpusTests.cs`, bộ kiểm thử chống prompt injection dùng một **mô hình giả luôn tuân lệnh** (đưa ra system prompt và mọi bí mật khi được hỏi). Vì vậy các test này chứng minh **máy chủ vẫn giữ kín luật và bí mật dù mô hình không từ chối**, chứ không chứng minh Claude từ chối các yêu cầu đó. Phần kiểm tra bằng mô hình thật nằm ở `LiveInjectionTests` và chưa được chạy (mục 5.2).

## 5.4. Các điểm chưa được kiểm thử hoặc chưa xác nhận

Chương này chỉ nên nêu những gì đã kiểm chứng. Các điểm sau chưa được kiểm thử hoặc chưa xác nhận, nên liệt kê trung thực:

- Hành vi của mô hình thật trước prompt injection (test `LiveInjectionTests` bị bỏ qua).
- Kiểm thử giao diện Blazor: trong thư mục `tests/` không có project kiểm thử giao diện. Chỉ có `BlazorChatClientTests` cho client trò chuyện. `BlazorChatClientTests` chạy `AssistantApi` (lớp mà `ChatBox` dùng) với đường xử lý thật của `/api/chat/stream`, nên kiểm tra phần máy khách gọi API chứ không kiểm tra giao diện hiển thị.
- Kiểm thử tải và hiệu năng dưới tải: số liệu trong `PERFORMANCE.md` là số câu SQL trên dữ liệu ít dòng, không phải đo tải.
