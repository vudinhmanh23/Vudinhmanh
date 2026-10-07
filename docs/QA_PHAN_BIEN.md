# 15 câu hỏi phản biện và gợi ý trả lời

**Cách dùng.** Mỗi câu có gợi ý trả lời 3-4 câu và bằng chứng trong code (đường dẫn kèm số dòng). Số dòng đúng tại thời điểm viết (07/10/2026, nhánh `feat/stock-ledger-and-race-safety`, chưa commit các thay đổi mới nhất); mở lại file trước khi bảo vệ để chắc chắn. Các câu trả lời chỉ nói những gì code có, không hứa tính năng chưa làm. Nhóm 5 có chỗ `[điền]` vì quy trình làm việc cá nhân của bạn không nằm trong code, bạn phải tự điền sự thật.

**Số liệu dùng trong câu trả lời** (lần chạy `dotnet test` ngày 07/10/2026): 644 test, 643 đạt, 0 lỗi, 1 bỏ qua. Chạy lại trước khi bảo vệ.

---

## Nhóm 1. Kiến trúc và thiết kế

### Câu 1. Tại sao tách thành bốn lớp (Domain, Application, Infrastructure, Api)? Với đồ án cỡ này không phải quá phức tạp sao?

**Gợi ý.** Mục đích chính là để nghiệp vụ nằm một chỗ và kiểm thử được: các service như `SalesOrderService` chỉ phụ thuộc interface (`IProductRepository`, `IUnitOfWork`), không biết EF Core. Nhờ đó giao dịch kho viết một lần ở tầng Application còn controller chỉ nhận request và trả kết quả. Em thừa nhận có chỗ chưa gọn: `ProductService` nhận cả `IRepository<Product>` lẫn `IProductRepository`, hai đường vào cùng một bảng. Với quy mô hiện tại, việc tách lớp hơi nhiều nghi thức, nhưng nó giúp em chạy được các bộ test đơn vị mà không cần CSDL.

**Bằng chứng.**
- `src/SalesInventory.Application/DependencyInjection.cs` (đăng ký service) và `src/SalesInventory.Infrastructure/DependencyInjection.cs` (đăng ký repository, `IUnitOfWork`).
- `src/SalesInventory.Application/Services/ProductService.cs:10-14` (hai kiểu repository).
- `tests/SalesInventory.Tests/Support/InventoryHarness.cs` (test đơn vị không cần CSDL thật).

### Câu 2. Vì sao dùng Blazor Server mà không phải WebAssembly? JWT được lưu ở đâu và có rủi ro gì?

**Gợi ý.** Với Blazor Server, lời gọi API xảy ra trên máy chủ nên trình duyệt không cần gọi API trực tiếp và không cần cấu hình CORS (`CLAUDE.md`). JWT lưu trong `ProtectedSessionStorage` (mã hóa, mất khi đóng tab), và `AuthMessageHandler` gắn token vào mọi request, đồng thời xóa token khi API trả 401. Đổi lại, mỗi người dùng giữ một kết nối SignalR liên tục với máy chủ, nên tốn bộ nhớ máy chủ hơn WebAssembly. Em chưa đo giới hạn số người dùng đồng thời, đó là điểm em chưa kiểm chứng.

**Bằng chứng.**
- `src/SalesInventory.Web/Program.cs:27-33` (`AuthMessageHandler`, `ApiBaseUrl`).
- `src/SalesInventory.Web/Services/TokenStore.cs:11-38` (`ProtectedSessionStorage`).
- `src/SalesInventory.Web/Services/AuthMessageHandler.cs:33-36` (xóa token khi 401).

### Câu 3. Hệ thống xử lý lỗi nghiệp vụ như thế nào? Vì sao không để controller tự bắt?

**Gợi ý.** Service ném các ngoại lệ có nghĩa (`NotFoundException`, `ConflictException`, `InsufficientStockException`, `ValidationException`...), và một middleware duy nhất đổi chúng thành `ProblemDetails` với mã 400, 404, 409, 503... Cách này giữ một quy ước lỗi thống nhất, và ở môi trường Production không lộ chi tiết ngoại lệ. Một ngoại lệ không được ánh xạ sẽ rơi vào nhánh 500. Em từng gặp đúng trường hợp này khi xóa sản phẩm đã có chứng từ, và đã sửa bằng cách kiểm tra trước rồi ném `ConflictException` (409).

**Bằng chứng.**
- `src/SalesInventory.Api/Middleware/GlobalExceptionHandlingMiddleware.cs:84-136` (bảng ánh xạ ngoại lệ sang mã HTTP).
- `src/SalesInventory.Application/Services/ProductService.cs:158` và `tests/SalesInventory.Api.Tests/ProductDeleteTests.cs` (lỗi 500 đã sửa, có test).

---

## Nhóm 2. Cơ sở dữ liệu

### Câu 4. Hai nhân viên cùng bán sản phẩm cuối cùng thì chuyện gì xảy ra? Làm sao chứng minh tồn không âm?

**Gợi ý.** Có ba lớp bảo vệ. Một, `SalesOrderService` kiểm tra cả đơn với tồn trong một giao dịch. Hai, `Product.RowVersion` (`[Timestamp]`) khiến đơn thua trong cuộc đua bị hoàn tác; service thử lại tối đa 5 lần với tồn mới, hết hàng thì trả 409. Ba, ràng buộc `CK_Products_StockQuantity_NonNegative` ở CSDL chặn tồn âm dù code có sót. Em chứng minh bằng test chạy trên SQL Server thật, vì InMemory không có `rowversion` hay khóa dòng.

**Bằng chứng.**
- `src/SalesInventory.Application/Services/SalesOrderService.cs:15` (`MaxPlaceAttempts = 5`), `:102-122` (vòng thử lại), `:136` (giao dịch), `:165` (`InsufficientStockException`).
- `src/SalesInventory.Domain/Entities/Product.cs:48` (`[Timestamp]`), `src/SalesInventory.Infrastructure/Persistence/AppDbContext.cs:78` (ràng buộc CHECK).
- `tests/SalesInventory.Api.Tests/Services/SalesOrderConcurrencyTests.cs:41` (`ParallelSales_OfTheSameProduct_NeverMakeStockNegative_AndLedgerMatches`), `:118` (`TwoWritersOnTheSameProductRow_SecondOneIsRejected...`), `RealSqlServerTests.cs:200` (ràng buộc CHECK hoạt động khi bỏ qua code ứng dụng).

### Câu 5. Tồn kho vừa lưu trong `Products.StockQuantity` vừa có sổ kho `StockMovements`. Không dư thừa sao? Nếu hai con số lệch nhau thì sao?

**Gợi ý.** Đúng là dư thừa có chủ ý: `StockQuantity` để đọc nhanh, `StockMovements` để truy vết. Mỗi dòng sổ ghi `Quantity` có dấu và `StockAfter`, và các luồng nhập, bán, điều chỉnh, đảo phiếu nhập cập nhật hai nơi trong cùng một giao dịch. Nhưng sổ kho **không đầy đủ**: tạo sản phẩm với tồn đầu kỳ và sửa trường tồn kho qua `PUT /api/products/{id}` đổi `StockQuantity` mà không ghi sổ (`ProductService.UpdateProductAsync`), nên hai con số có thể lệch. Em coi đây là một khiếm khuyết đã biết và nêu ở phần hạn chế. Sổ kho liên kết chứng từ qua `RefType` và `RefId` chứ không có khóa ngoại, đổi lại không ràng buộc ở mức CSDL. Test `ParallelSales_OfTheSameProduct_NeverMakeStockNegative_AndLedgerMatches` kiểm tra sổ kho khớp tồn sau các đơn bán song song (không bao gồm đường sửa sản phẩm); chưa có công cụ đối soát cho dữ liệu thật.

**Bằng chứng.**
- `src/SalesInventory.Domain/Entities/StockMovement.cs` (`Quantity`, `StockAfter`, `RefType`, `RefId`).
- `src/SalesInventory.Application/Services/SalesOrderService.cs:187-205` (trừ kho, ghi sổ, commit), `PurchaseOrderService.cs` (`ApprovePurchaseOrderAsync`).
- `tests/SalesInventory.Api.Tests/Services/SalesOrderConcurrencyTests.cs:41` (tên test nêu "LedgerMatches").

### Câu 6. Xóa đơn bán thì tồn kho có được hoàn không? Còn các điểm yếu khác trong mô hình dữ liệu?

**Gợi ý.** Không. `DELETE /api/sales-orders/{id}` (chỉ Admin) xóa cứng đơn và các dòng, không hoàn tồn và không ghi sổ kho. `SalesOrderStatus.Cancelled` có trong enum nhưng không có chức năng nào đặt trạng thái đó. Cột `Products.Price` là cột cũ giữ song song với `SalePrice`. Đây là hạn chế em nêu trong báo cáo, và hướng khắc phục là chức năng hủy đơn có hoàn kho và ghi sổ.

**Bằng chứng.**
- `src/SalesInventory.Application/Services/SalesOrderService.cs:252-264` (`DeleteOrderAsync`).
- `src/SalesInventory.Api/Controllers/SalesOrdersController.cs` (chú thích: "stock is not restored").
- `src/SalesInventory.Application/Mapping/ProductProfile.cs` (chú thích "legacy Price mirrors SalePrice").

---

## Nhóm 3. Bảo mật

### Câu 7. JWT cấu hình ra sao? Vì sao không có refresh token?

**Gợi ý.** Token ký bằng HMAC-SHA256, hết hạn sau 60 phút (`Jwt:ExpiryMinutes`), và khi kiểm tra bắt buộc đúng nhà phát hành, đối tượng, thời hạn, chữ ký, với `ClockSkew = 0`. Khóa phải từ 32 byte trở lên, nếu không API từ chối khởi động. Hệ thống chỉ có access token, không có refresh token, nên hết 60 phút người dùng phải đăng nhập lại; em ghi đó là hạn chế. Tài khoản bị khóa thì không nhận được token mới, nhưng token đã cấp vẫn hợp lệ đến khi hết hạn (em chưa có cơ chế thu hồi).

**Bằng chứng.**
- `src/SalesInventory.Infrastructure/Identity/TokenService.cs:29,31` (HMAC-SHA256, hạn dùng).
- `src/SalesInventory.Api/Program.cs:86-88` (kiểm tra token, `ClockSkew`), kiểm tra độ dài khóa ở khối `Jwt:Key`.
- `src/SalesInventory.Api/Controllers/AuthController.cs` (`IsLockedOutAsync` trong `Login`).

### Câu 8. Phân quyền thực hiện thế nào? Có lỗ hổng nào không?

**Gợi ý.** Có ba vai trò (`Admin`, `Kho`, `BanHang`) và ba policy; mỗi controller khai báo `[Authorize]`, sai quyền trả 403. Khi rà soát code em tìm ra một lỗ hổng thật (đã sửa): endpoint đăng ký công khai cho phép chọn vai trò `Admin`, tức người lạ tự tạo được tài khoản quản trị. Em đã sửa để người lạ chỉ nhận vai trò `BanHang`, vai trò khác cần token Admin, và thêm test `RegistrationSecurityTests` (bỏ bản sửa thì 3 ca đỏ). Điều đáng nói là bộ test cũ không phát hiện lỗi này, vì chính helper của test dùng lỗ hổng đó để tạo Admin; em đã đổi helper sang tạo tài khoản qua `POST /api/admin/users`.

**Bằng chứng.**
- `src/SalesInventory.Api/Program.cs:104-106` (ba policy), `src/SalesInventory.Api/Controllers/AuthController.cs:33,44` (kiểm tra vai trò khi đăng ký).
- `tests/SalesInventory.Api.Tests/RegistrationSecurityTests.cs`, `AuthTestHelper.cs`, `RoleAccessMatrixTests.cs`.
- Lưu ý khi trả lời: policy `SalesAccess` được đăng ký nhưng không controller nào dùng (`Program.cs:106`).

### Câu 9. Trợ lý AI có thể bị prompt injection hoặc lộ khóa API không? Test của bạn chứng minh điều gì?

**Gợi ý.** Em không dựa vào lời dặn của prompt mà thêm kiểm soát ở máy chủ: `PromptGuard` che bí mật trong dữ liệu vào và ra, phát hiện việc mô hình chép lại system prompt (cửa sổ 120 ký tự), và câu hỏi người dùng luôn bọc trong thẻ đánh dấu không đáng tin. Công cụ của trợ lý chỉ đọc và không trả giá nhập hay thông tin khách hàng. Test dùng một mô hình giả luôn tuân lệnh, nên chúng chứng minh máy chủ vẫn giữ kín dù mô hình không từ chối, chứ không chứng minh Claude từ chối. Phần chạy với mô hình thật (`LiveInjectionTests`) cần khóa API nên bị bỏ qua trong lần chạy mặc định, em chưa có kết quả cho phần đó.

**Bằng chứng.**
- `src/SalesInventory.Infrastructure/Ai/PromptGuard.cs:18,30,69` (`LeakWindow`, `Scrub`, `LeaksSystemPrompt`), `AnthropicChatService.cs:366` (`<question trust="untrusted">`).
- `src/SalesInventory.Application/Assistant/Tools/GetPriceTool.cs`, `GetOrderStatusTool.cs` (các trường trả về).
- `tests/SalesInventory.Api.Tests/InjectionCorpusTests.cs:17-20` (giải thích mô hình giả), `:234-239` (`LiveInjectionTests` chỉ chạy khi có `SALES_LIVE_ANTHROPIC_KEY`).

---

## Nhóm 4. Kiểm thử và triển khai

### Câu 10. Bạn có bao nhiêu test và chúng thực sự kiểm tra điều gì? Có gì chưa được kiểm thử?

**Gợi ý.** Lần chạy gần nhất có 644 test, 643 đạt, 0 lỗi, 1 bỏ qua (test gọi mô hình AI thật). Phần mạnh nhất là nghiệp vụ kho (nhập, bán, tranh chấp đồng thời trên SQL Server thật), phân quyền và các lớp an toàn của trợ lý. Chưa có gì kiểm thử giao diện Blazor ngoài `BlazorChatClientTests`, và chưa có đo tải; số liệu trong `PERFORMANCE.md` là số câu SQL trên dữ liệu ít dòng, không phải tốc độ dưới tải. Test xanh không chứng minh chương trình đúng: ví dụ lỗi đăng ký Admin từng nằm im trong khi hơn 600 test xanh.

**Bằng chứng.**
- `docs/BaoCao/Chuong5_KiemThu.md` (bảng 5.1, mục 5.4).
- `tests/SalesInventory.Tests/`, `tests/SalesInventory.Api.Tests/` (hơn 50 file test tích hợp).
- `PERFORMANCE.md` (đầu file nêu rõ giới hạn của dữ liệu đo).

### Câu 11. Tại sao test dùng SQL Server thật qua Docker thay vì InMemory? Nếu máy không có Docker thì sao?

**Gợi ý.** InMemory và SQLite không có `rowversion`, khóa dòng và giao dịch thật, nên không thể chứng minh các tính chất về tranh chấp đồng thời. Vì vậy `TestDatabase` tạo một CSDL riêng bằng Testcontainers và chạy các migration thật cho mỗi bộ test. Máy không có Docker thì đặt `SALESINVENTORY_TESTS_DB=InMemory`, khi đó các test cần SQL Server thật tự bị bỏ qua (`DockerSqlFact`), nên kết quả xanh trong chế độ đó không nói gì về tranh chấp đồng thời. Em ghi điều này trong báo cáo để tránh hiểu nhầm.

**Bằng chứng.**
- `tests/SalesInventory.Api.Tests/TestDatabase.cs:33-38` (`UseInMemory`, `UnavailableReason`).
- `tests/SalesInventory.Api.Tests/Services/SalesOrderConcurrencyTests.cs:13-19` (`DockerSqlFactAttribute`), `CustomWebApplicationFactory.cs` (migration thật cho mỗi host test).
- `CLAUDE.md` (mục Commands).

### Câu 12. Triển khai bằng Docker như thế nào? Bí mật được quản lý ra sao và CSDL được di trú khi nào?

**Gợi ý.** `docker-compose.yml` chạy ba dịch vụ (`sqlserver`, `api`, `blazor`) với `restart: unless-stopped`, kiểm tra sức khỏe SQL Server và volume để dữ liệu sống sau khi tắt container. Bí mật chỉ đi qua biến môi trường; ở Production, API từ chối khởi động nếu khóa JWT, chuỗi kết nối hay khóa AI nằm trong file cấu hình (`SecretSettingsGuard`). Migration tự chạy khi khởi động chỉ khi bật `Database__MigrateOnStartup` (compose bật, mặc định tắt), để CSDL thật không bị đổi chỉ vì khởi động lại. Kho mã có tài liệu hướng dẫn triển khai VPS (`README-deploy.md`). `[điền: em đã chạy Compose ở đâu (máy cá nhân hay VPS thật)? chỉ nói điều đã làm; nếu chưa triển khai lên VPS thì nói là chưa]`.

**Bằng chứng.**
- `docker-compose.yml:10,16,20,33,44,66` (`restart`, cổng chỉ nghe localhost, healthcheck, `MigrateOnStartup`).
- `src/SalesInventory.Api/Hardening/SecretSettingsGuard.cs:7-17`, `src/SalesInventory.Api/Program.cs:187-189` (di trú tùy chọn).
- `README-deploy.md` (hướng dẫn VPS).

---

## Nhóm 5. Vai trò của AI trong quá trình làm đồ án

> Nhóm này hội đồng gần như chắc chắn hỏi, và lịch sử git công khai điều đó: trong 63 commit của nhánh hiện tại, **62 commit có dòng `Co-Authored-By: Claude`** (lệnh: `git log --format=%B | grep -i co-authored-by`). Đừng phủ nhận hay giảm nhẹ. Hãy trả lời thẳng, và điền các chỗ `[điền]` bằng sự thật của bạn.

### Câu 13. Bạn đã dùng AI để làm đồ án này như thế nào? Phần nào là của bạn?

**Gợi ý.** Em dùng Claude Code làm công cụ lập trình: kho mã có file `CLAUDE.md` mô tả quy ước cho công cụ, và hầu hết commit có đồng tác giả là Claude. `[điền: phần em tự làm: quyết định phạm vi và yêu cầu, chọn kiến trúc, thiết kế dữ liệu, duyệt từng thay đổi, chạy và sửa lỗi... chỉ ghi những gì đúng]`. Em chịu trách nhiệm với mọi dòng trong kho mã, và trong buổi bảo vệ em trả lời được các quyết định thiết kế chính. `[điền: nếu có phần em không viết hay không đọc kỹ, nói thẳng thay vì khẳng định ngược lại]`.

**Bằng chứng.**
- `git log` (62 trên 63 commit có `Co-Authored-By: Claude`), `CLAUDE.md`, `docs/PLAN.md`, `docs/adr/0001-kien-truc-du-an.md` (tài liệu kế hoạch và quyết định).

### Câu 14. Làm sao bạn biết mã do AI sinh ra là đúng? Có lần nào AI sinh sai không?

**Gợi ý.** Em không tin vào mã chỉ vì nó biên dịch được: em dựa vào test (644 test, trong đó có test chạy trên SQL Server thật) `[điền: những đoạn em tự đọc lại]`. Bằng chứng cụ thể: khi rà soát báo cáo bằng cách đối chiếu với code (làm cùng công cụ AI), em và công cụ phát hiện hai lỗi nằm sẵn trong kho mã (đăng ký công khai cho chọn Admin, và xóa sản phẩm có chứng từ trả lỗi không rõ), đã sửa và thêm test, rồi gỡ bản sửa để chắc chắn test thật sự bắt được lỗi cũ (6 ca đỏ). Các tài liệu thiết kế ban đầu (`SRS.md`, `BACKLOG.md`) ghi tính năng không có trong code (ví dụ khóa tài khoản 15 phút sau 5 lần đăng nhập sai, refresh token, audit log, vai trò tên `ADMIN/WAREHOUSE/SALES` thay vì `Admin/Kho/BanHang`), nên em chỉ dựa vào code khi viết báo cáo. `[điền: nêu thêm một lỗi hay quyết định mà em tự phát hiện, nếu có]`.

**Bằng chứng.**
- `tests/SalesInventory.Api.Tests/RegistrationSecurityTests.cs`, `ProductDeleteTests.cs`.
- `docs/BaoCao/Chuong5_KiemThu.md` (mục 5.3.3), `docs/SRS.md`, `docs/BACKLOG.md` (so sánh với code).

### Câu 15. Nếu không có AI, bạn có làm được đồ án này không? Bạn học được gì và có giải thích được các phần khó không?

**Gợi ý.** Câu này kiểm tra hiểu biết, nên câu trả lời thật là tốt nhất, đừng nói quá. Em có thể giải thích các quyết định quan trọng: vì sao dùng `RowVersion` cùng giao dịch và vòng thử lại cho đơn bán, vì sao sổ kho ghi `StockAfter`, vì sao dùng SQL Server thật cho test đồng thời, và vì sao trợ lý không được tin vào lời dặn trong prompt. `[điền: điều em học được và điều em còn chưa nắm chắc, ví dụ phần RAG hay phần hardening Production]`. Em cũng biết rõ các hạn chế của hệ thống (chưa hủy đơn bán, không có refresh token, chưa thử tải) và đã ghi chúng trong báo cáo.

**Bằng chứng.**
- Các mục tương ứng ở câu 4, 5, 9, 11 và mục "Hạn chế và hướng phát triển" của báo cáo (Chương 8, chưa viết).
- Gợi ý chuẩn bị: bạn nên tự đọc `SalesOrderService.cs:96-215` và `SalesOrderConcurrencyTests.cs:41-140` đủ kỹ để giải thích bằng lời của mình, không đọc từ giấy.

---

## Mẹo khi bị hỏi điều không chắc

- Nói rõ "em chưa kiểm chứng điều này" thay vì đoán. Ví dụ: giới hạn số người dùng đồng thời của Blazor Server, hành vi mô hình AI thật trước prompt injection, hiệu năng dưới tải.
- Trỏ sang bằng chứng: mở file test hoặc chạy lệnh `dotnet test --filter` cho một lớp test cụ thể (ví dụ `SalesOrderConcurrencyTests`) nếu hội đồng muốn thấy tận mắt.
- Nếu hội đồng chỉ ra một lỗi thật, ghi nhận và nói cách sửa cùng cách kiểm chứng; hai lỗi vừa sửa là ví dụ tốt về quy trình này.
