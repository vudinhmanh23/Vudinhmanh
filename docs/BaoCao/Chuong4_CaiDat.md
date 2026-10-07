# CHƯƠNG 4. CÀI ĐẶT VÀ CÔNG NGHỆ

> **Quy ước đối chiếu.** Phiên bản thư viện lấy từ các file `.csproj`; tên lớp, tên file lấy từ nhánh `feat/stock-ledger-and-race-safety`. Đường dẫn tính từ thư mục gốc của solution. Chương này chỉ mô tả những gì có trong mã nguồn; những điểm chưa làm được nằm ở Chương 8.

## 4.1. Công nghệ và phiên bản

Toàn bộ solution nhắm tới .NET 8 (`net8.0`).

| Mục đích | Công nghệ | Phiên bản | Project dùng |
|---|---|---|---|
| Web API | ASP.NET Core Web API | 8.0 | `SalesInventory.Api` |
| Giao diện | Blazor Server (Razor Components) | 8.0 | `SalesInventory.Web` |
| Biểu đồ | Blazor-ApexCharts | 7.0.0 | `SalesInventory.Web` |
| ORM | Entity Framework Core + provider SQL Server | 8.0.10 | `SalesInventory.Infrastructure` |
| Người dùng, vai trò | ASP.NET Core Identity (EF Core store) | 8.0.10 | `SalesInventory.Infrastructure` |
| Xác thực | JWT Bearer (`Microsoft.AspNetCore.Authentication.JwtBearer`) | 8.0.10 | `SalesInventory.Api` |
| Tạo token | `System.IdentityModel.Tokens.Jwt` | 7.1.2 | `SalesInventory.Infrastructure` |
| Kiểm tra dữ liệu vào | FluentValidation, kèm DataAnnotations trên DTO | 11.11.0 | `SalesInventory.Application` |
| Ánh xạ DTO | AutoMapper | 14.0.0 | `SalesInventory.Application` |
| Xuất PDF | QuestPDF (giấy phép Community, đặt trong `Program.cs`) | 2026.9.1 | `SalesInventory.Application` |
| Xuất Excel | ClosedXML | 0.105.1 | `SalesInventory.Application` |
| Ghi log | Serilog.AspNetCore | 8.0.3 | `SalesInventory.Api` |
| Tài liệu API | Swashbuckle.AspNetCore (Swagger) | 6.6.2 | `SalesInventory.Api` |
| Kiểm thử | xUnit 2.9.2, Moq 4.20.72, Mvc.Testing 8.0.10, Testcontainers.MsSql 4.15.0 | | `tests/*` |
| Đóng gói | Docker (nhiều giai đoạn), Docker Compose | | `docker-compose.yml`, `Dockerfile` |

Hai dịch vụ AI không có thư viện riêng: `AnthropicChatService` và `HttpEmbeddingService` gọi REST trực tiếp bằng `HttpClient`.

> **Cần lưu ý.** Gói `AutoMapper` 14.0.0 bị `dotnet build` cảnh báo `NU1903` (lỗ hổng mức cao đã công bố, `GHSA-rvv3-g6hj-g44x`). Hệ thống chưa nâng cấp gói này; xem Chương 8.

## 4.2. Cấu trúc mã nguồn

```
SalesInventory.sln
├── src/
│   ├── SalesInventory.Domain          Entities/ (12 entity), Enums/ (3 enum)
│   ├── SalesInventory.Application     Services/, Interfaces/, Dtos/, Validators/, Mapping/, Assistant/ (công cụ của trợ lý)
│   ├── SalesInventory.Infrastructure  Persistence/ (AppDbContext, Migrations, UnitOfWork), Repositories/, Identity/, Ai/, Storage/, Security/
│   ├── SalesInventory.Api             Controllers/ (16), Middleware/, Hardening/, Logging/, Security/, RateLimiting/, Knowledge/
│   └── SalesInventory.Web             Components/Pages, Components/Shared, Services/ (ApiClient, AuthService, CatalogApi, AssistantApi)
├── tests/
│   ├── SalesInventory.Tests           Kiểm thử đơn vị (nghiệp vụ kho)
│   └── SalesInventory.Api.Tests       Kiểm thử tích hợp (API, SQL Server thật qua Testcontainers)
├── docs/                              Tài liệu thiết kế và báo cáo
├── docker-compose.yml
└── CLAUDE.md, README-deploy.md, PERFORMANCE.md, AI_ASSISTANT.md
```

Quy tắc phụ thuộc: `Domain` không tham chiếu project nào; `Application` chỉ tham chiếu `Domain`; `Infrastructure` tham chiếu `Application` và `Domain`; `Api` tham chiếu `Application` và `Infrastructure` (theo các `ProjectReference` trong `.csproj`). Lớp `Application` định nghĩa interface (`IProductRepository`, `IUnitOfWork`, `IChatService`...) và `Infrastructure` cài đặt chúng.

## 4.3. Các điểm kỹ thuật chính của nghiệp vụ kho

### 4.3.1. Sổ kho `StockMovement`

Các luồng nghiệp vụ đổi tồn kho (duyệt, hủy, xóa phiếu nhập; bán; điều chỉnh) ghi một dòng vào `StockMovements` cùng giao dịch với việc đổi `Products.StockQuantity`. Hai đường đổi tồn **không** ghi sổ: đặt tồn đầu kỳ khi tạo sản phẩm (`CreateProductAsync`) và ghi đè `StockQuantity` bằng giá trị client gửi khi sửa sản phẩm (`ProductService.UpdateProductAsync`, dòng `existing.StockQuantity = product.StockQuantity`); xem Chương 8. Mỗi dòng lưu số lượng có dấu (`Quantity`), tồn sau khi đổi (`StockAfter`), loại biến động (`StockMovementType`: `Import`, `Sale`, `Adjustment`) và chứng từ nguồn (`RefType`, `RefId`, `Reference`).

| Tình huống | Nơi cài đặt | Dòng sổ kho |
|---|---|---|
| Duyệt phiếu nhập | `PurchaseOrderService.ApprovePurchaseOrderAsync` | `Import`, số dương, `RefType = "PurchaseOrder"` |
| Hủy phiếu nhập đã duyệt | `PurchaseOrderService.CancelPurchaseOrderAsync` | `Adjustment`, số âm |
| Xóa phiếu nhập đã duyệt | `PurchaseOrderService.DeletePurchaseOrderAsync` | `Sale`, số âm (khác với hủy, xem Chương 8) |
| Tạo đơn bán | `SalesOrderService.PlaceOrderAsync` | `Sale`, số âm, `RefType = "SalesOrder"` |
| Điều chỉnh tay hoặc kiểm kê | `StockMovementService.ApplyAsync` | `Adjustment`, `RefType = "ManualAdjustment"`, `RefId = 0`, ghi chú là lý do bắt buộc |

### 4.3.2. Giao dịch và an toàn khi đồng thời

Các thao tác đổi kho chạy trong một giao dịch do `IUnitOfWork.BeginTransactionAsync()` mở (`Infrastructure/Persistence/UnitOfWork.cs`). Có ba lớp bảo vệ để tồn kho không âm:

1. **Kiểm tra trong service.** `SalesOrderService.PlaceOrderAsync` nạp mọi sản phẩm của đơn bằng một truy vấn (`GetByIdsAsync`), cộng số lượng các dòng cùng sản phẩm rồi so với tồn. Thiếu hàng thì ném `InsufficientStockException`, middleware trả HTTP 409 kèm tên sản phẩm, số cần và số còn.
2. **Khóa lạc quan.** `Product.RowVersion` có `[Timestamp]` (`Domain/Entities/Product.cs`). Hai giao dịch cùng sửa một sản phẩm thì giao dịch thua bị `DbUpdateConcurrencyException`. `SalesOrderRepository.SaveChangesAsync` đổi lỗi này, lỗi trùng khóa (SQL Server mã 2601, 2627) và lỗi nạn nhân deadlock (1205) thành một ngoại lệ chung `ConcurrencyConflictException`.
3. **Ràng buộc ở CSDL.** `CK_Products_StockQuantity_NonNegative` (`[StockQuantity] >= 0`) trong `AppDbContext`, chặn tồn âm dù mã ứng dụng có sót.

Khi gặp `ConcurrencyConflictException`, `CreateOrderAsync` hoàn tác toàn bộ, xóa trạng thái theo dõi của EF (`ResetTracking`), đặt lại các khóa sinh tự động rồi chạy lại, tối đa 5 lần (`MaxPlaceAttempts = 5`), chờ ngẫu nhiên ngắn giữa các lần:

```csharp
// SalesOrderService.cs (rút gọn)
for (var attempt = 1; ; attempt++)
{
    try { return await PlaceOrderAsync(order, items); }
    catch (ConcurrencyConflictException ex) when (attempt < MaxPlaceAttempts)
    {
        _unitOfWork.ResetTracking();
        // ... đặt lại Id và các navigation của đơn và các dòng ...
        await Task.Delay(Random.Shared.Next(10, 40) * attempt);
    }
}
```

Lần thử cuối vẫn thua thì ngoại lệ được ném ra ngoài và middleware ánh xạ thành HTTP 409 (`GlobalExceptionHandlingMiddleware`, nhánh `ConcurrencyConflictException`). Duyệt phiếu nhập kiểm tra thêm việc cộng tồn không vượt `int.MaxValue` (tính bằng `long`) trước khi đổi, và hủy hoặc xóa phiếu đã duyệt bị từ chối nếu một phần hàng đã bán (`ReverseStockAsync`).

### 4.3.3. Không tin dữ liệu tiền từ máy khách

Server tính lại `LineTotal` và `TotalAmount` cho cả đơn bán lẫn phiếu nhập, nên số tiền client gửi bị bỏ qua. Đơn bán làm tròn `LineTotal` đến 2 chữ số (`MidpointRounding.AwayFromZero`), từ chối giảm giá âm hoặc vượt tổng hàng, và từ chối đơn không có dòng hàng hoặc số lượng không dương.

### 4.3.4. Sinh mã chứng từ

Mã đơn bán có dạng `SO-yyyyMMdd-NNNN` (`SalesOrderService.GenerateOrderNumberAsync`), mã phiếu nhập `PO-yyyyMMdd-NNN` (`PurchaseOrderService.GenerateCodeAsync`). Số thứ tự là số lớn nhất đã dùng với tiền tố của ngày đó cộng một. Ngày tính theo giờ UTC. Chỉ mục duy nhất trên `OrderNumber` và `Code` là lớp bảo vệ cuối cùng khi hai request sinh cùng một số; với đơn bán, lỗi trùng khóa nằm trong nhóm lỗi được thử lại.

### 4.3.5. Truy vấn và chỉ mục

`PERFORMANCE.md` ghi các số đo bằng cách đếm sự kiện `CommandExecuted` của EF Core trên dữ liệu thật nhưng chỉ có vài dòng (tài liệu nêu rõ thời gian chạy trên bảng nhỏ không có ý nghĩa):

| Tình huống | Trước | Sau |
|---|---|---|
| Nạp sản phẩm khi tạo đơn bán, 8 dòng hàng khác nhau | 8 câu SQL | 1 câu (`WHERE Id IN (...)`) |
| Kiểm tra sản phẩm tồn tại khi tạo phiếu nhập, N sản phẩm | N câu | 1 câu |
| Kiểm tra trùng SKU, barcode, mã nhà cung cấp | 1 câu tải toàn bộ bảng | 1 câu `EXISTS` |
| Chi tiết đơn #4 | 32 cột | 18 cột (chỉ chiếu cột cần) |

Danh sách đơn bán và đơn nhập được chiếu thẳng sang DTO, không theo dõi (`AsNoTracking`). Các chỉ mục được thêm bằng migration `AddPerformanceIndexes`, `AddDashboardIndexes`, `AddCustomerPhoneIndex`, ví dụ chỉ mục `(ProductId, CreatedAt, Id)` cho thẻ kho và chỉ mục `(Status, OrderDate)` kèm `TotalAmount` cho dashboard. Số liệu này chỉ chứng minh số câu lệnh và lượng cột đọc, không chứng minh hiệu năng dưới tải.

### 4.3.6. Dashboard và báo cáo

`DashboardRepository` tính doanh thu, số đơn, giá trị đơn trung bình bằng một câu `SUM` và `COUNT` gộp (`GroupBy` trên hằng số), chỉ tính đơn `Completed`. Doanh thu theo kỳ nhóm theo ngày, tháng hoặc quý (`RevenueGroupBy`), có thể so với cùng kỳ năm trước. Giá trị tồn kho tính bằng tổng `StockQuantity × PurchasePrice` trong CSDL.

Báo cáo xuất file nằm ở lớp `Application/Services`:

- `InvoicePdfService`: hóa đơn bán hàng PDF bằng QuestPDF, gồm bảng STT, sản phẩm, số lượng, đơn giá, thành tiền.
- `RevenuePdfService`, `RevenueExcelService`: báo cáo doanh thu theo ngày và sản phẩm bán chạy, bằng QuestPDF và ClosedXML.
- Font mặc định của QuestPDF không có dấu tiếng Việt, nên `InvoiceFonts` nhúng sẵn Noto Sans (Regular, Bold) vào assembly và đăng ký một lần.
- Tiêu đề PDF lấy từ `ShopSettings` (tên cửa hàng, địa chỉ, điện thoại, logo; có thể thêm tên trường và mã sinh viên).

### 4.3.7. Ảnh sản phẩm

Có ba cách đặt ảnh: tải lên, nhập URL, hoặc lấy theo barcode từ Open Food Facts. Mọi ảnh phải qua `ProductImageRules`: đuôi `.jpg`, `.jpeg`, `.png`, `.webp`, loại nội dung tương ứng, tối đa 2 MB, và nội dung thật phải khớp với đuôi (nhận dạng bằng các byte đầu của file, vì đuôi và loại nội dung do client khai báo có thể giả).

Với URL do người dùng nhập, `SafeImageDownloader` chống SSRF theo các quy tắc ghi trong chú thích đầu lớp: chỉ https cổng 443, không có thông tin đăng nhập trong URL; kiểm tra địa chỉ IP **tại thời điểm kết nối** trên địa chỉ thực sự dùng (chống DNS rebinding) và từ chối mọi địa chỉ không phải địa chỉ công cộng; tự theo chuyển hướng tối đa 3 lần, mỗi lần kiểm tra lại; không dùng proxy; có thời gian chờ cứng và giới hạn số byte đọc. Ảnh lưu bằng `LocalFileStorage` dưới `wwwroot/uploads/products` và được phục vụ như file tĩnh.

### 4.3.8. Xử lý lỗi thống nhất

`GlobalExceptionHandlingMiddleware` đổi các ngoại lệ có nghĩa thành `ProblemDetails`:

| Ngoại lệ | Mã HTTP |
|---|---|
| `ValidationException` (FluentValidation), `ChatInputException` | 400 |
| `NotFoundException` | 404 |
| `InsufficientStockException`, `ConcurrencyConflictException`, `DbUpdateConcurrencyException`, `BusinessRuleException`, `ConflictException`, `DuplicateSkuException`, `DuplicateBarcodeException` | 409 |
| `UnprocessableEntityException` | 422 |
| `AssistantUnavailableException` | 503 |
| Ngoại lệ còn lại | 500; ngoài môi trường Development, nội dung chỉ là câu chung, không có chi tiết ngoại lệ |

## 4.4. Trợ lý AI bán hàng

### 4.4.1. Thành phần

| Thành phần | Lớp / file | Vai trò |
|---|---|---|
| Điểm vào | `AssistantController`, `ChatController` | Nhận câu hỏi, trả JSON hoặc luồng SSE; chat có lưu hội thoại |
| Điều phối | `Infrastructure/Ai/AnthropicChatService.cs` | Gọi Anthropic Messages API, chạy vòng lặp công cụ |
| Công cụ | `Application/Assistant/Tools/` (`GetStockTool`, `GetPriceTool`, `GetOrderStatusTool`) và `AssistantToolRegistry` | Đọc dữ liệu thật; lọc theo vai trò |
| RAG | `RagRetriever`, `DocumentIngestionService`, `TextChunker`, `VectorMath`, `HttpEmbeddingService` | Tìm đoạn tài liệu liên quan |
| An toàn | `PromptGuard`, `Security/SecretMasker`, `AiSafetyOptions`, `RateLimitingExtensions` | Che bí mật, giới hạn chi phí và tần suất |
| Lưu hội thoại | `ConversationStore`, bảng `Conversations`, `ChatMessages` | Lịch sử theo từng người dùng |

### 4.4.2. Luồng một câu hỏi

1. **Kiểm tra đầu vào** trước mọi lời gọi ra ngoài: câu hỏi không rỗng, không dài quá `AiSafety:MaxQuestionLength` (2000 ký tự), khóa API và system prompt đã cấu hình. System prompt không được chứa bí mật; nếu thiếu cấu hình, dịch vụ không chạy với luật yếu hơn mà từ chối (503).
2. **Chọn mô hình.** Câu hỏi ngắn (tối đa `ShortQuestionMaxLength` = 80 ký tự) dùng tầng rẻ (`haiku`); câu khác dùng `Anthropic:ModelTier` (`sonnet`). Mã mô hình cụ thể chỉ nằm trong cấu hình (`Anthropic:Models`). Cài đặt `effort` được thêm vào yêu cầu với các tầng hỗ trợ, và bỏ qua với tầng Haiku vì API từ chối.
3. **Lấy tài liệu (RAG)**, nếu có cấu hình embeddings: nhúng câu hỏi bằng Voyage, so với mọi đoạn trong `KnowledgeChunks`, giữ tối đa `TopK` = 3 đoạn có điểm cosine ≥ `MinScore` = 0,3. Lấy tài liệu là "cố gắng hết sức": lỗi nhà cung cấp thì trợ lý vẫn trả lời bằng công cụ.
4. **Dựng lời nhắc.** System prompt (có tên cửa hàng) nằm ở trường `system` riêng và không bao giờ bị ghép thêm. Câu hỏi luôn bọc trong thẻ `<question trust="untrusted">`, được thoát XML để không đóng thẻ giả; đoạn tài liệu nằm trong thẻ riêng và cũng được thoát và lọc bí mật. Lịch sử (tối đa `MaxHistoryMessages` = 20 tin) đi theo cùng quy tắc.
5. **Vòng lặp công cụ.** Khi mô hình yêu cầu công cụ (`tool_use`), dịch vụ chỉ chạy công cụ mà vai trò người gọi được phép, từng công cụ một (chúng dùng chung một `DbContext`), cắt kết quả quá 4000 ký tự, rồi gửi lại. Quá `MaxToolRounds` = 4 vòng thì dừng và trả một câu thông báo.
6. **Lọc đầu ra.** `PromptGuard` che mọi bí mật cấu hình và các chuỗi trông như bí mật (khóa, JWT, chuỗi kết nối); nếu đầu ra chép lại 120 ký tự liên tiếp của system prompt (`LeakWindow`), cắt và thay bằng câu từ chối. Khi truyền luồng, phần cuối có thể là đầu của một bí mật được giữ lại (`HeldBackLength`) cho tới khi chắc chắn không phải.
7. **Ghi chi phí.** Mỗi lượt ghi một dòng log chỉ gồm số đếm: người dùng (id mờ), tầng mô hình, số token và chi phí ước tính theo bảng giá trong `AiSafety:Pricing`. Không ghi câu hỏi, câu trả lời hay khóa.

### 4.4.3. Công cụ

| Công cụ | Trả về | Cố ý không trả |
|---|---|---|
| `get_stock` | Số tồn, đơn vị, tình trạng (hết hàng, sắp hết, còn hàng), cờ ngừng kinh doanh | |
| `get_price` | Giá bán hiện tại (có dạng chuỗi đã định dạng VND) | Giá nhập |
| `get_order_status` | Mã đơn, trạng thái, ngày, tổng tiền, số dòng | Thông tin khách, chi tiết dòng hàng |

Tham số của công cụ do mô hình sinh ra nên được coi là dữ liệu không tin cậy: `get_order_status` chỉ chấp nhận chữ, số và dấu `-`, tối đa 30 ký tự, trước khi truy vấn CSDL. Công cụ chỉ **đọc** dữ liệu. Trợ lý không tạo, sửa hay xóa dữ liệu nào.

### 4.4.4. Các giới hạn cấu hình (`appsettings.json`, mục `AiSafety`)

| Tham số | Giá trị | Ý nghĩa |
|---|---|---|
| `MaxTokens` | 1024 | Trần độ dài một câu trả lời |
| `MaxQuestionLength` | 2000 | Câu hỏi dài hơn bị từ chối trước khi gọi ra ngoài |
| `MaxHistoryMessages` | 20 | Số tin cũ đưa vào làm ngữ cảnh |
| `MaxToolRounds` | 4 | Số vòng công cụ tối đa |
| `RateLimit` | 10 lượt mỗi 60 giây mỗi người dùng, thuật toán `FixedWindow` | Dùng chung cho ba endpoint AI; vượt thì HTTP 429 kèm `Retry-After` |

Các giá trị được kiểm tra khi khởi động (`AiSafetyOptionsValidator`); giá trị sai làm ứng dụng không khởi động thay vì âm thầm nới lỏng giới hạn. Bộ đếm tần suất đặt theo mã người dùng, lấy từ claim của token.

## 4.5. Bảo mật và cấu hình

### 4.5.1. Xác thực và phân quyền

- **Mật khẩu** (`Infrastructure/DependencyInjection.cs`): tối thiểu 8 ký tự, bắt buộc có chữ hoa và chữ số; Identity băm mật khẩu.
- **JWT**: ký HMAC-SHA256, hết hạn theo `Jwt:ExpiryMinutes` (60). Khi kiểm tra bắt buộc đúng `Issuer`, `Audience`, thời hạn và chữ ký, `ClockSkew = 0`. Khóa phải từ 32 byte; nếu không API từ chối khởi động (`Program.cs`).
- **Phân quyền**: ba vai trò và ba policy (xem Chương 3, mục 3.1.3). Đăng ký công khai (`POST /api/auth/register`) chỉ cấp vai trò `BanHang`; vai trò khác chỉ do `Admin` cấp, thường qua `POST /api/admin/users`.
- **Khóa tài khoản**: do Admin thực hiện (`/api/admin/users/{id}/lock`, `unlock`); đăng nhập bị từ chối khi tài khoản đang bị khóa.

### 4.5.2. Quản lý bí mật

Quy tắc trong `CLAUDE.md`: không commit bí mật; môi trường phát triển dùng `dotnet user-secrets`, Production chỉ dùng biến môi trường. `SecretSettingsGuard` kiểm tra khi khởi động ở Production: API từ chối chạy nếu `Jwt:Key`, `ConnectionStrings:DefaultConnection`, `Anthropic:ApiKey`, `Embeddings:ApiKey` hoặc `SeedAdmin:Password` đến từ một file cấu hình, hoặc thiếu chuỗi kết nối. Một test quét cả các file cấu hình của repo.

`SecretMasker` (`Infrastructure/Security`) tìm mọi giá trị bí mật theo tên khóa cấu hình và theo hình dạng (JWT, chuỗi kết nối), và được dùng ở hai nơi: lọc nội dung gửi tới mô hình AI (`PromptGuard`) và lọc nội dung ghi vào log (các formatter của Serilog).

### 4.5.3. Cứng hóa môi trường Production

`ApiHardeningExtensions` chỉ siết Production, Development giữ nguyên: HSTS một năm, chuyển hướng HTTPS bằng mã 308 (giữ nguyên phương thức và nội dung của POST), trình xử lý lỗi dự phòng chỉ trả câu chung, `ForwardedHeaders` chỉ tin các proxy liệt kê trong `Hardening:TrustedProxies`, CORS theo đúng origin liệt kê (rỗng thì không origin nào được phép; không dùng `AllowCredentials` vì API xác thực bằng header chứ không bằng cookie). Swagger chỉ bật ở Development.

### 4.5.4. Ghi log và giám sát

Serilog ghi ra console và file theo ngày (`logs/salesinventory-.log`, giữ 14 file), với formatter che bí mật (`Logging/ScrubbingFormatters.cs`). Mỗi request có một dòng log (phương thức, đường dẫn, mã trạng thái, thời gian) theo `RequestLogging`, không ghi chuỗi truy vấn hay nội dung request. `AuthFailureLoggingMiddleware` và `AuthFailureTracker` đếm các phản hồi 401 và 403 theo địa chỉ khách trong cửa sổ trượt (mặc định 10 lần trong 5 phút) rồi ghi cảnh báo, và mức lỗi khi vượt ngưỡng; bộ nhớ theo dõi có trần số địa chỉ. **Cơ chế này chỉ ghi nhận và cảnh báo, không chặn** (xem Chương 8).

## 4.6. Giao diện Blazor Server

### 4.6.1. Luồng gọi API và đăng nhập

Trang Razor chạy trên máy chủ. `Program.cs` của project Web đăng ký một `HttpClient` có tên với `BaseAddress = ApiBaseUrl` và gắn `AuthMessageHandler`. Handler đọc JWT từ `TokenStore` (bọc `ProtectedSessionStorage`) để thêm header `Authorization: Bearer`, và xóa token khi API trả 401. `JwtAuthenticationStateProvider` và `JwtParser` đọc claims từ token để `AuthorizeView` và `NavMenu` ẩn hiện chức năng theo vai trò. Token hết hạn thì các trang chuyển về `/login`.

### 4.6.2. Các trang có thật

| Trang | Đường dẫn | Chức năng trên giao diện |
|---|---|---|
| Đăng nhập | `/login` | Đăng nhập, lấy JWT |
| Sản phẩm | `/products`, `/products/new`, `/products/{id}/edit` | Danh sách có lọc, sắp xếp, phân trang, hiện ảnh nhỏ, nút Sửa và Xóa (xóa bị chặn thì báo "đã phát sinh dữ liệu liên quan"); form tạo và sửa chỉ có 6 trường: tên, SKU, danh mục, giá bán, tồn kho, mô tả |
| Nhập hàng | `/purchases/create` | Lập phiếu nhập, tùy chọn duyệt ngay (mặc định đã chọn) |
| Bán hàng | `/pos` | Chọn sản phẩm, giỏ hàng, chọn hoặc thêm nhanh khách, chốt đơn; hiện mã đơn và cảnh báo sắp hết hàng |
| Dashboard | `/dashboard` | KPI và cảnh báo sắp hết hàng |
| Báo cáo doanh thu | `/reports/revenue` | Biểu đồ doanh thu theo ngày, tháng, quý, so sánh cùng kỳ (Blazor-ApexCharts) |
| Trợ lý AI | `/assistant`, `/assistant/{ConversationId}` | Hội thoại có lịch sử, nhận câu trả lời theo luồng (SSE) |

Bảy trang sau chỉ có tiêu đề (và nút điều hướng) mà **chưa có chức năng**: `/` (Tổng quan), `/categories`, `/suppliers`, `/customers`, `/purchases`, `/sales`, `/reports`; trong đó 6 mục (Tổng quan, Danh mục, Nhà cung cấp, Khách hàng, Nhập hàng, Bán hàng) vẫn nằm trong menu `NavMenu`. Khách hàng chỉ thêm được từ quầy bán hàng (`/pos`, nút "Thêm khách hàng mới"). Các chức năng sau chỉ dùng được qua API: quản lý danh mục, nhà cung cấp, khách hàng; danh sách, duyệt, hủy phiếu nhập và đơn bán; hóa đơn PDF; xuất Excel/PDF; sổ kho; quản lý người dùng; các trường sản phẩm ngoài 6 trường trên (giá nhập, barcode, nhà cung cấp, đơn vị, ngưỡng tồn, ảnh). Chi tiết ở Chương 8.

## 4.7. Đóng gói

- **Dockerfile của API** (`src/SalesInventory.Api/Dockerfile`): giai đoạn `build` dùng `dotnet/sdk:8.0`, sao chép file `.csproj` trước để tận dụng bộ đệm `restore`; giai đoạn cuối dùng `dotnet/aspnet:8.0-jammy-chiseled-extra` (ảnh tối giản), chạy bằng người dùng không phải root (UID 1654), `ASPNETCORE_ENVIRONMENT=Production`, nghe cổng 8080.
- **`docker-compose.yml`**: ba dịch vụ `sqlserver` (SQL Server 2022, kiểm tra sức khỏe, cổng 1433 chỉ gắn vào `127.0.0.1`), `api` và `blazor`, đều `restart: unless-stopped`; ba volume `sqlserver_data`, `api_logs`, `api_uploads`. Chuỗi kết nối do compose dựng từ `MSSQL_SA_PASSWORD`; `Database__MigrateOnStartup=true` để migration tự chạy khi khởi động.
- Compose mặc định `ASPNETCORE_ENVIRONMENT=Development` cho API (để có Swagger khi chạy cục bộ); triển khai thật phải đặt `Production` và đặt sau một reverse proxy kết thúc TLS, vì compose không có TLS (`README-deploy.md`).
- Biến môi trường và quy ước cấu hình đầy đủ nằm ở `CLAUDE.md`; hướng dẫn triển khai VPS ở `README-deploy.md` (Chương 6).
