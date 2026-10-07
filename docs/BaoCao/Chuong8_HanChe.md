# CHƯƠNG 8. HẠN CHẾ VÀ HƯỚNG PHÁT TRIỂN

> **Cách đọc chương này.** Mỗi hạn chế dưới đây được kiểm chứng bằng cách đọc mã nguồn (đường dẫn ghi ở cột "Bằng chứng"); chưa có hạn chế nào được dựng thành bài kiểm thử tái hiện, trừ khi ghi rõ. Mục "Hướng khắc phục" là **đề xuất, chưa cài đặt**. Các hạn chế được xếp theo mức ảnh hưởng đến độ tin cậy của dữ liệu, rồi đến giao diện, bảo mật, trợ lý AI, vận hành và quy trình.

## 8.1. Hạn chế về toàn vẹn dữ liệu và nghiệp vụ

| # | Hạn chế | Bằng chứng | Hướng khắc phục (đề xuất) |
|---|---|---|---|
| 1 | **Dữ liệu cũ thiếu dòng sổ kho.** Từ khi sửa lỗi sổ kho (mục 8.7) mọi đường đổi tồn đều ghi sổ, nhưng sản phẩm đã có trước đó thì không có dòng giải thích tồn của chúng: các sản phẩm mẫu chèn bởi migration (`HasData`) và sản phẩm tạo bằng bản cũ. Với chúng, tổng `Quantity` của sổ kho không bằng `StockQuantity`; chưa có migration bù | `AppDbContext.OnModelCreating` (`HasData` đặt `StockQuantity` trực tiếp); migration `SeedInitialData`, `MoreSeedData`; `StockLedgerReconciliationTests` chỉ phủ sản phẩm tạo qua API | Migration bù một dòng `Adjustment` (`RefType = "InitialStock"`) cho mỗi sản phẩm có tồn mà chưa có dòng sổ; hoặc tách dữ liệu mẫu khỏi migration (hạn chế 19) |
| 2 | **Chưa có chức năng hủy đơn bán.** `SalesOrderStatus.Cancelled` có trong enum và được truy vấn báo cáo lọc ra, nhưng không có đoạn mã nào đặt trạng thái này. Xóa đơn (`DELETE /api/sales-orders/{id}`, chỉ `Admin`) xóa cứng đơn và dòng hàng, **không hoàn tồn và không ghi sổ kho** | `SalesOrderService.DeleteOrderAsync`; chú thích "stock is not restored" trong `SalesOrdersController` | Thêm thao tác hủy đơn: đổi trạng thái, hoàn tồn và ghi dòng sổ kho trong một giao dịch; bỏ hoặc hạn chế xóa cứng |
| 3 | **Sản phẩm xóa cứng.** Sản phẩm chưa có chứng từ bị xóa hẳn (đã có chứng từ thì bị chặn, HTTP 409, xem mục 8.7). `Product.IsActive` là cờ "ngừng kinh doanh" riêng, không phải xóa mềm | `ProductService.DeleteProductAsync` | Dùng `IsActive = false` làm cách xóa mặc định |
| 4 | **Loại biến động không nhất quán khi đảo phiếu nhập.** Hủy phiếu đã duyệt ghi dòng `Adjustment`, nhưng xóa phiếu đã duyệt ghi dòng `Sale` | `PurchaseOrderService.CancelPurchaseOrderAsync` và `DeletePurchaseOrderAsync` (gọi `ReverseStockAsync` với hai loại khác nhau) | Thêm loại `Reversal` hoặc thống nhất một loại |
| 5 | **Mô hình dữ liệu hẹp.** Một kho duy nhất; tồn kho kiểu `int`; đơn vị tính là chuỗi tự do; đơn bán bắt buộc có khách hàng (chưa có khách lẻ); chưa có thanh toán, công nợ, thuế. Cột `Price` là cột cũ phản chiếu `SalePrice`. Hai danh mục cùng tên đều hợp lệ | `Domain/Entities/*.cs`; `ProductProfile.cs` ("legacy Price mirrors SalePrice"); `CategoryService` chỉ kiểm tra tên không rỗng | Bỏ cột `Price`; thêm khách lẻ mặc định; mở rộng sang nhiều kho khi cần |

## 8.2. Hạn chế về giao diện

| # | Hạn chế | Bằng chứng | Hướng khắc phục (đề xuất) |
|---|---|---|---|
| 6 | **Bảy trang chỉ có tiêu đề, chưa có chức năng:** `/` (Tổng quan), `/categories`, `/suppliers`, `/customers`, `/purchases`, `/sales`, `/reports`. Sáu trong số đó vẫn nằm trên menu (trừ `/reports`) | `Components/Pages/Categories.razor`, `Suppliers.razor`, `Customers.razor`, `Purchases.razor`, `Sales.razor`, `Reports.razor`, `Home.razor` (5 đến 10 dòng mỗi file); `Layout/NavMenu.razor` | Cài các trang quản lý; bỏ mục menu chưa dùng |
| 7 | **Nhiều chức năng của API không có trên giao diện:** quản lý danh mục, nhà cung cấp, khách hàng (khách chỉ thêm nhanh được ở `/pos`); danh sách, chi tiết, duyệt, hủy phiếu nhập và đơn bán; tải hóa đơn PDF; xuất Excel hoặc PDF; xem sổ kho; quản lý người dùng; tải ảnh sản phẩm | `SalesInventory.Web` không có lời gọi tới các route tương ứng; xem Chương 4, mục 4.6.2 | Bổ sung theo mức ưu tiên: danh sách đơn và duyệt phiếu, hóa đơn PDF, sổ kho |
| 8 | **Thông báo gây hiểu lầm.** Khi lưu phiếu nhập thành công nhưng duyệt thất bại, trang báo "có thể duyệt lại từ danh sách phiếu nhập", trong khi giao diện không có danh sách phiếu nhập nào | `Purchases/CreatePurchase.razor` (`_draftMessage`); `Purchases.razor` | Thêm danh sách phiếu và nút duyệt, hoặc sửa thông báo |
| 9 | **Form sản phẩm chỉ có 6 trường** (tên, SKU, danh mục, giá bán, tồn kho, mô tả). Giá nhập, barcode, nhà cung cấp, đơn vị, ngưỡng tồn và mức đặt hàng lại chỉ đặt được qua API | `Products/ProductForm.razor` | Bổ sung các trường còn thiếu |
| 10 | **Ngôn ngữ thông báo không đồng nhất.** Nhiều thông báo của service bằng tiếng Việt, nhiều thông báo khác bằng tiếng Anh | Chuỗi trong `ProductService`, `SalesOrderService`, `CustomerService`, `GlobalExceptionHandlingMiddleware` | Thống nhất một ngôn ngữ, hoặc dùng mã lỗi và bảng dịch ở giao diện |

## 8.3. Hạn chế về bảo mật

| # | Hạn chế | Bằng chứng | Hướng khắc phục (đề xuất) |
|---|---|---|---|
| 11 | **Không có refresh token và không thu hồi token.** Token hết hạn sau 60 phút, người dùng phải đăng nhập lại. Khóa tài khoản chỉ chặn việc cấp token mới; token đã cấp vẫn dùng được đến khi hết hạn (không tìm thấy bước kiểm tra tình trạng khóa khi xác thực token) | `Jwt:ExpiryMinutes`; `AuthController.Login` (kiểm tra khóa); `Program.cs` (cấu hình JwtBearer không có sự kiện kiểm tra thêm) | Refresh token có thể thu hồi; hoặc kiểm tra `SecurityStamp` khi xác thực token |
| 12 | **Đăng nhập không bị giới hạn tần suất và không khóa theo số lần sai.** Giới hạn tần suất chỉ gắn vào ba endpoint của trợ lý AI. `AuthFailureTracker` đếm 401 và 403 rồi **ghi log cảnh báo**, không chặn. Identity không đếm lần sai vì `Login` dùng `CheckPasswordAsync` | `[EnableRateLimiting]` chỉ có trong `AssistantController` và `ChatController`; `AuthFailureLoggingMiddleware`; `AuthController.Login` | Thêm chính sách giới hạn cho `/api/auth/login`; dùng `CheckPasswordSignInAsync` với `lockoutOnFailure` |
| 13 | **Đăng ký công khai vẫn mở.** Người lạ tạo được tài khoản `BanHang` mà không cần phê duyệt (đã chặn việc tự chọn vai trò khác, xem mục 8.7) | `AuthController.Register` | Tắt đăng ký công khai, hoặc yêu cầu Admin duyệt |
| 14 | **Gói thư viện có lỗ hổng đã công bố.** `AutoMapper` 14.0.0 bị cảnh báo `NU1903` (mức cao, `GHSA-rvv3-g6hj-g44x`); chưa nâng cấp | `SalesInventory.Application.csproj`; cảnh báo khi `dotnet build` | Nâng cấp lên bản đã vá, chạy lại bộ test |
| 15 | **Không có nhật ký kiểm toán theo nghĩa bảng ghi vết thao tác.** Chỉ có log có cấu trúc (Serilog) và sổ kho cho biến động tồn; không có xác thực hai lớp | Không có entity tương ứng trong `Domain/Entities`; `appsettings.json` (Serilog) | Bảng audit cho thao tác nhạy cảm (đăng nhập, đổi quyền, hủy xóa đơn) |

## 8.4. Hạn chế của trợ lý AI

| # | Hạn chế | Bằng chứng | Hướng khắc phục (đề xuất) |
|---|---|---|---|
| 16 | **Phạm vi hẹp, chỉ đọc.** Ba công cụ (`get_stock`, `get_price`, `get_order_status`), không tạo hay sửa dữ liệu. Tài liệu tri thức hiện có hai file; việc nạp tài liệu là thủ công (`POST /api/assistant/knowledge/ingest`, chỉ Admin), không tự chạy khi khởi động. Hội thoại không có thao tác xóa hay đổi tên | `Application/DependencyInjection.cs`; `Api/Knowledge/*.md`; `AssistantController`; `ChatController` (chỉ có `POST stream`, `GET`, `GET {id}`) | Thêm công cụ tìm sản phẩm theo danh mục; nạp tài liệu tự động; xóa hội thoại |
| 17 | **RAG không mở rộng được.** `RagRetriever` đọc mọi đoạn của `KnowledgeChunks` vào bộ nhớ rồi tính cosine cho từng đoạn ở mỗi câu hỏi. Vector lưu dạng `varbinary`. Hợp với vài chục đoạn, không hợp với kho tài liệu lớn | `Infrastructure/Ai/RagRetriever.cs` (`ToListAsync` rồi `CosineSimilarity`); `KnowledgeChunk.Embedding` | Dùng cơ sở dữ liệu vector hoặc chỉ mục xấp xỉ |
| 18 | **Chưa kiểm chứng với mô hình thật.** Các test chống prompt injection dùng mô hình giả luôn tuân lệnh, nên chỉ chứng minh máy chủ giữ kín luật và bí mật, không chứng minh mô hình từ chối. Test dùng mô hình thật (`LiveInjectionTests`) cần khóa API và bị bỏ qua khi chạy mặc định. Trợ lý còn phụ thuộc Anthropic, Voyage, mạng và có chi phí theo lượt | `tests/SalesInventory.Api.Tests/InjectionCorpusTests.cs` (chú thích đầu file); `AiSafetyOptions` | Chạy `LiveInjectionTests` định kỳ và đọc kết quả bằng mắt; theo dõi chi phí trong log |

## 8.5. Hạn chế về vận hành và hiệu năng

| # | Hạn chế | Bằng chứng | Hướng khắc phục (đề xuất) |
|---|---|---|---|
| 19 | **Dữ liệu mẫu nằm trong migration.** Các migration và `HasData` chèn danh mục, nhà cung cấp `SUP-001` và sản phẩm mẫu vào mọi cơ sở dữ liệu được tạo, kể cả khi triển khai thật bằng `Database__MigrateOnStartup=true` | `AppDbContext.OnModelCreating` (`HasData`); migration `SeedInitialData`, `MoreSeedData`; bộ test phải xóa các bảng này khi khởi động (`CustomWebApplicationFactory`) | Tách dữ liệu mẫu ra khỏi migration, nạp bằng lệnh riêng khi cần demo |
| 20 | **Giả định chỉ chạy một bản API.** Bộ đếm giới hạn tần suất và bộ theo dõi lỗi xác thực nằm trong bộ nhớ; ảnh sản phẩm lưu trên đĩa của container (volume `api_uploads`) | `RateLimitingExtensions`; `AuthFailureTracker`; `LocalFileStorage`; `docker-compose.yml` | Dùng bộ nhớ đệm phân tán và kho lưu trữ đối tượng nếu cần chạy nhiều bản |
| 21 | **Compose không có TLS.** Mặc định API chạy với `ASPNETCORE_ENVIRONMENT=Development`; triển khai thật phải đặt `Production` và đặt sau một reverse proxy kết thúc TLS. Tài liệu triển khai nêu mật khẩu và token đi dạng chữ rõ nếu truy cập qua `http://IP` | `docker-compose.yml`; `README-deploy.md` | Thêm dịch vụ proxy có TLS vào compose |
| 22 | **Danh sách đơn không phân trang mặc định.** `GET /api/sales-orders` và `/api/purchase-orders` trả toàn bộ nếu không truyền `page` và `pageSize` | `SalesOrdersController.GetOrders`, `PurchaseOrdersController.GetPurchaseOrders` | Phân trang bắt buộc với kích thước mặc định |
| 23 | **Hiệu năng chưa được đo dưới tải.** Số đo trong `PERFORMANCE.md` là số câu SQL và lượng cột đọc trên dữ liệu chỉ vài dòng; chưa có thử nghiệm tải, và chưa đo số người dùng đồng thời mà Blazor Server chịu được (mỗi người giữ một kết nối SignalR) | `PERFORMANCE.md` (đầu file) | Thử nghiệm tải với dữ liệu lớn hơn |

## 8.6. Hạn chế về kiểm thử và tài liệu

| # | Hạn chế | Bằng chứng | Hướng khắc phục (đề xuất) |
|---|---|---|---|
| 24 | **Phạm vi kiểm thử chưa trọn vẹn.** Không có kiểm thử giao diện; không có thử nghiệm tải; chưa đo độ phủ mã (đã cài `coverlet.collector` nhưng chưa có số liệu); test dùng mô hình thật bị bỏ qua. Một lỗ hổng về vai trò đã tồn tại trong khi hơn 600 test đều xanh, vì chính `AuthTestHelper` dựa vào nó | `tests/`; `Chuong5_KiemThu.md` | Kiểm thử giao diện (ví dụ bUnit hoặc Playwright); đo độ phủ; test mô tả quy tắc "ai được làm gì" độc lập với helper |
| 25 | **Tài liệu thiết kế ban đầu vượt quá phạm vi đã cài đặt.** `docs/SRS.md` và `docs/BACKLOG.md` ghi các tính năng không có trong mã nguồn: khóa tài khoản 15 phút sau 5 lần đăng nhập sai, refresh token, thuế VAT, nhật ký kiểm toán, xóa mềm người dùng, vai trò đặt tên `ADMIN/WAREHOUSE/SALES` (thực tế `Admin/Kho/BanHang`). `docs/ai-assistant-design.md` nhắc tới `AiToolCallLog`, nhưng không có lớp nào như vậy trong mã. Báo cáo này chỉ dựa vào mã nguồn | `docs/SRS.md` (FR-01, FR-02), `docs/BACKLOG.md`, `docs/ai-assistant-design.md`; tìm trong `src` không thấy | Cập nhật các tài liệu cho khớp với mã, hoặc đánh dấu rõ phần chưa làm |

## 8.7. Các lỗi tìm thấy khi rà soát báo cáo và đã sửa

Trong quá trình đối chiếu báo cáo với mã nguồn, ba lỗi thật đã được phát hiện, sửa và có test riêng (xem Chương 5, mục 5.3.3):

1. **Đăng ký công khai cho phép chọn vai trò `Admin`.** Trước khi sửa, `POST /api/auth/register` không cần đăng nhập và nhận vai trò từ nội dung yêu cầu, nên người lạ tự tạo được tài khoản quản trị. Nay `AuthController.Register` chỉ cấp vai trò `BanHang` cho người chưa đăng nhập; vai trò khác cần token `Admin`. Test: `RegistrationSecurityTests`.
2. **Xóa sản phẩm đã có chứng từ.** Trước khi sửa, thao tác này không được kiểm tra trước nên khóa ngoại thất bại và API không trả thông báo nghiệp vụ (giao diện đã sẵn có thông báo cho mã 409 nhưng API không trả mã đó). Nay `ProductService.DeleteProductAsync` kiểm tra bằng `IProductRepository.HasDocumentsAsync` và trả 409 kèm gợi ý chuyển sang ngừng kinh doanh. Test: `ProductDeleteTests`.

3. **Sổ kho không đầy đủ.** Trước khi sửa, tạo sản phẩm kèm tồn đầu kỳ và sửa sản phẩm với trường tồn kho (`PUT /api/products/{id}`) đặt thẳng `Products.StockQuantity` mà không ghi `StockMovements`, nên tồn và sổ kho có thể lệch. Form sửa còn gửi lại giá trị tồn đã đọc lúc mở, nên lưu muộn sau một đơn bán thì ghi đè đơn bán đó. Nay `CreateProductAsync` ghi một dòng `Adjustment` (`RefType = "InitialStock"`) cùng giao dịch khi tồn đầu kỳ lớn hơn 0; `UpdateProductAsync` bỏ qua trường `quantity` (ô "Tồn kho" của form sửa bị vô hiệu hóa); xóa sản phẩm chỉ có dòng tồn đầu kỳ thì dọn dòng đó cùng giao dịch. Test: `StockLedgerReconciliationTests` (năm ca, gồm một ca đối soát: nhập, bán, điều chỉnh và sửa rồi tổng sổ kho phải bằng tồn).

Cách kiểm chứng: gỡ tạm các thay đổi trong `src/`, ba lớp test mới có 10 ca đỏ (6 ca của hai lỗi đầu và 4 ca của lỗi sổ kho); khôi phục thì toàn bộ bộ test xanh (649 test, 1 bỏ qua). Khi sửa lỗi thứ ba, hàm dọn dẹp của `SqlInjectionTests` và test xóa sản phẩm của chính tôi vỡ vì sản phẩm tạo qua API giờ có dòng sổ kho; cả hai được sửa theo. Điều đáng rút ra là bộ test cũ không phát hiện được lỗi thứ nhất vì chính hàm hỗ trợ của test dùng lỗ hổng đó để tạo tài khoản Admin và Kho (xem hạn chế 24). Lỗi thứ ba cũng được tìm thấy khi đọc mã chứ không do test.

## 8.8. Hướng phát triển (đề xuất, chưa cài đặt)

**Ngắn hạn: làm cho dữ liệu đáng tin cậy** (xử lý hạn chế 1, 2, 4, 8, 14, 19)
- Bù dòng sổ kho cho dữ liệu có trước bản sửa; thêm thao tác hủy đơn bán có hoàn kho.
- Thêm danh sách phiếu nhập và đơn bán trên giao diện, kèm nút duyệt, hủy, in hóa đơn.
- Nâng cấp `AutoMapper`; tách dữ liệu mẫu khỏi migration.

**Trung hạn: bảo mật và vận hành** (hạn chế 11, 12, 13, 15, 20, 21, 22)
- Refresh token có thể thu hồi; giới hạn tần suất và khóa tạm theo số lần sai khi đăng nhập; bảng audit; tắt hoặc kiểm soát đăng ký công khai.
- Thêm TLS vào compose; phân trang bắt buộc; thử nghiệm tải.

**Dài hạn: mở rộng chức năng và AI** (hạn chế 5, 16, 17)
- Nhiều kho, khách lẻ, thanh toán và công nợ, thuế.
- Công cụ trợ lý rộng hơn (vẫn chỉ đọc, hoặc thao tác có xác nhận của người dùng), nạp tài liệu tự động, cơ sở dữ liệu vector, theo dõi chất lượng và chi phí.

## 8.9. Nhận xét chung

Phần lõi của hệ thống, gồm nhập hàng, bán hàng, sổ kho và xử lý đồng thời, có test chạy trên SQL Server thật (Chương 5). Giá trị của đồ án nằm ở phần này. Tuy vậy, giao diện chưa phản ánh hết các chức năng của API (mục 8.2), sổ kho của dữ liệu có trước bản sửa còn thiếu dòng giải thích (hạn chế 1), và một số cơ chế bảo mật mới ở mức ghi nhận chứ chưa ở mức chặn (hạn chế 11 và 12). Các điểm này cần được trình bày đúng như vậy khi bảo vệ.
