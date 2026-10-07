# Dàn ý slide bảo vệ đồ án (15 phút, 14 slide)

**Đề tài:** Hệ thống Quản lý Bán hàng & Kho
**Nguồn:** `BAO_CAO_DO_AN.md` chưa tồn tại (báo cáo chưa ghép), nên dàn ý dựa vào `docs/BaoCao/Chuong3_ThietKe.md`, `Chuong5_KiemThu.md` và code trong `src/`. Số liệu ở slide 11 lấy từ lần chạy `dotnet test` ngày 07/10/2026; chạy lại trước khi bảo vệ.
**Quy ước:** mỗi bullet tối đa 12 từ. Hình hoặc sơ đồ là nội dung chính, chữ chỉ hỗ trợ.

## Phân bổ thời gian

| Slide | Nội dung | Thời gian |
|---|---|---|
| 1-3 | Bìa, vấn đề, phạm vi | 1:30 |
| 4-6 | Công nghệ, kiến trúc, CSDL | 3:00 |
| 7-8 | Nghiệp vụ trọng tâm, trợ lý AI | 3:30 |
| 9-10 | Bảo mật và kiểm thử, triển khai | 1:30 |
| 11-12 | Kết quả, hạn chế | 1:30 |
| 13 | Demo | 3:00 |
| 14 | Cảm ơn, Q&A | 0:30 |
| | **Tổng** | **14:30** |

---

## Slide 1. Trang bìa

**Nội dung trên slide**
- Tên đề tài: Hệ thống Quản lý Bán hàng & Kho
- Họ tên, MSSV, lớp, giảng viên hướng dẫn (điền)
- Trường, khoa, năm 2026
- Logo trường

**Hình:** ảnh chụp màn hình trang `/dashboard` làm nền mờ.

**Ghi chú trình bày (khoảng 15 giây)**
Chào hội đồng, giới thiệu tên. Nêu tên đề tài và nói đề tài là một hệ thống chạy được, không chỉ là mô hình.

---

## Slide 2. Đặt vấn đề và mục tiêu

**Nội dung trên slide**
- Cửa hàng nhỏ quản lý hàng bằng sổ, bảng tính
- Hậu quả: tồn kho sai, bán vượt tồn, kho âm
- Mục tiêu: hệ thống ghi vết biến động kho

**Hình:** sơ đồ "trước và sau": bảng tính rời rạc, rồi một hệ thống duy nhất.

**Ghi chú trình bày (khoảng 45 giây)**
Nêu vấn đề bằng một ví dụ cụ thể: hai nhân viên cùng bán cái cuối cùng của một mặt hàng. Nhấn mạnh điểm khác biệt của đề tài là *toàn vẹn tồn kho khi nhiều người dùng đồng thời*, chứ không chỉ CRUD. Đây là hạt nhân của đồ án nên nhắc lại ở slide 7.

---

## Slide 3. Phạm vi và yêu cầu chức năng

**Nội dung trên slide**
- Sản phẩm, danh mục, nhà cung cấp, khách hàng
- Đơn nhập (nháp, duyệt, hủy) và đơn bán (qua API)
- Tồn kho có sổ kho; dashboard và báo cáo doanh thu
- Trợ lý AI tra cứu tồn, giá, đơn, chính sách
- Ba vai trò: Admin, Kho, BanHang

**Hình:** use case diagram (sơ đồ thứ 3 đã tạo; có Admin, Kho, BanHang).

**Ghi chú trình bày (khoảng 45 giây)**
Đi qua sơ đồ use case, nói ngắn quyền của mỗi vai trò. Chỉ nêu cái có thật: BanHang không xem được dashboard và báo cáo; Kho không quản lý khách. Giao diện Blazor hoàn chỉnh cho: sản phẩm, quầy bán hàng, lập phiếu nhập, dashboard, báo cáo doanh thu, trợ lý AI; danh mục, nhà cung cấp, khách hàng chỉ quản lý được qua API (trang tương ứng mới có tiêu đề). Nói rõ ngoài phạm vi: thanh toán, công nợ, nhiều kho.

---

## Slide 4. Công nghệ sử dụng

**Nội dung trên slide**
- Backend: ASP.NET Core 8 Web API
- Giao diện: Blazor Server
- Dữ liệu: SQL Server, EF Core Code First
- Xác thực: ASP.NET Identity và JWT
- AI: Anthropic API, Voyage embeddings
- Triển khai: Docker Compose

**Hình:** hàng logo công nghệ (không dùng đoạn văn).

**Ghi chú trình bày (khoảng 30 giây)**
Không giải thích từng công nghệ. Chỉ nói vì sao: Blazor Server gọi API từ phía máy chủ, trình duyệt không gọi API trực tiếp (token được mã hóa trong sessionStorage); SQL Server vì cần `rowversion` và giao dịch.

---

## Slide 5. Kiến trúc hệ thống

**Nội dung trên slide**
- Blazor Server gọi Web API bằng JWT
- API gọi SQL Server qua EF Core
- Bốn lớp: Domain, Application, Infrastructure, Api
- Dịch vụ ngoài: Anthropic, Voyage, Open Food Facts

**Hình:** sơ đồ kiến trúc `flowchart` ở `Chuong3_ThietKe.md` mục 3.1 (Hình 3.1). Có thể đơn giản hóa còn 4 khối.

**Ghi chú trình bày (khoảng 1 phút)**
Đi theo mũi tên từ trình duyệt đến CSDL. Nhấn: nghiệp vụ nằm ở lớp Application, nên controller mỏng. Nhắc mẫu Repository và Unit of Work vì giao dịch kho cần Unit of Work.

---

## Slide 6. Thiết kế cơ sở dữ liệu

**Nội dung trên slide**
- 12 bảng nghiệp vụ và AI; 29 migration
- Đơn nhập, đơn bán có dòng chi tiết
- `StockMovements`: sổ kho nhập, bán, điều chỉnh
- Ràng buộc: tồn không âm, SKU duy nhất, `RowVersion`

**Hình:** ERD ở `Chuong3_ThietKe.md` mục 3.2.2 (chỉ bảng chính, bỏ bảng AI cho gọn).

**Ghi chú trình bày (khoảng 1 phút 15 giây)**
Trỏ vào `Products` rồi `StockMovements`. Giải thích ba tầng bảo vệ tồn kho: mã nghiệp vụ, `RowVersion`, và ràng buộc CHECK ở CSDL. Nếu bị hỏi: liên kết từ sổ kho đến chứng từ qua `RefType` và `RefId`, không có khóa ngoại.

---

## Slide 7. Nghiệp vụ trọng tâm: nhập, bán, tồn kho

**Nội dung trên slide**
- Nhập: nháp không đổi kho; duyệt mới cộng tồn
- Bán: kiểm tra cả đơn, trừ kho, ghi sổ
- Tất cả trong một giao dịch, hoàn tác nếu lỗi
- Thua cuộc đua thì thử lại tối đa 5 lần
- Thiếu hàng trả 409, nêu rõ sản phẩm

**Hình:** sequence diagram "Tạo đơn bán và trừ tồn kho" (sơ đồ thứ 2 đã tạo), kèm sơ đồ trạng thái đơn nhập (Hình 3.3).

**Ghi chú trình bày (khoảng 2 phút)**
Đây là slide quan trọng nhất. Đi theo sequence diagram: kiểm tra đầu vào, tính lại tiền ở server, mở giao dịch, kiểm tra tồn, trừ kho, ghi sổ, commit. Nhấn hai ý: server không tin số tiền từ client, và cùng sản phẩm nhiều dòng thì được cộng lại trước khi so với tồn. Nếu có thời gian, kể kịch bản hai đơn đồng thời: đơn thua trên `RowVersion` bị hoàn tác và chạy lại với tồn mới. Chỉ nói những gì test đã chứng minh (xem slide 9).

---

## Slide 8. Trợ lý AI bán hàng

**Nội dung trên slide**
- Ba công cụ: tồn kho, giá bán, trạng thái đơn
- RAG: trả lời chính sách từ tài liệu cửa hàng
- Chỉ đọc dữ liệu; không tạo hay sửa đơn
- Chặn lộ bí mật, giới hạn chi phí và tần suất

**Hình:** sequence diagram một lượt chat (Hình 3.5 trong `Chuong3_ThietKe.md`), hoặc ảnh chụp màn hình `/assistant`.

**Ghi chú trình bày (khoảng 1 phút 30 giây)**
Nói trợ lý lấy số liệu từ công cụ chứ không từ trí nhớ của mô hình. Công cụ không trả giá nhập và thông tin khách. Nêu các giới hạn có thật: 10 lượt mỗi phút cho mỗi người dùng, tối đa 1024 token cho một câu trả lời, câu ngắn dùng mô hình rẻ hơn. Nếu bị hỏi về tấn công prompt injection: các test dùng mô hình giả luôn tuân lệnh để chứng minh *máy chủ* giữ kín luật, và không khẳng định gì về mô hình thật.

---

## Slide 9. Bảo mật và kiểm thử

**Nội dung trên slide**
- JWT hết hạn sau 60 phút; phân quyền theo vai trò
- Production từ chối chạy nếu bí mật nằm trong file
- Log không chứa mật khẩu, token, khóa API
- 644 test, 643 đạt, 0 lỗi, 1 bỏ qua
- Test đồng thời chạy trên SQL Server thật

**Hình:** ảnh chụp kết quả `dotnet test` (dòng "Passed! ... Total: 587"), và biểu đồ cột hai project: 57 và 587.

**Ghi chú trình bày (khoảng 1 phút)**
Nêu số test chính xác như trên màn hình. Giải thích vì sao dùng SQL Server thật cho test đồng thời: InMemory không có `rowversion` và khóa dòng. Chủ động nói test bị bỏ qua là test gọi mô hình thật, cần khóa API. Không nói "hệ thống an toàn tuyệt đối".

---

## Slide 10. Triển khai

**Nội dung trên slide**
- `docker compose up`: SQL Server, API, Blazor
- Bí mật chỉ qua biến môi trường
- Tự áp migration khi khởi động (tùy chọn)
- Production: HTTPS, HSTS, CORS chặt

**Hình:** sơ đồ ba container với cổng 5080, 5081 và các volume.

**Ghi chú trình bày (khoảng 30 giây)**
Có hướng dẫn VPS trong `README-deploy.md`. **Chỉ nói đã triển khai lên VPS nếu bạn thực sự đã làm**; nếu chưa, nói là đã chạy được bằng Docker Compose trên máy cá nhân và có tài liệu triển khai.

---

## Slide 11. Kết quả đạt được

**Nội dung trên slide**
- 74 endpoint trên 16 controller
- 12 bảng, 29 migration
- 644 test tự động, 0 lỗi
- Truy vấn tạo đơn: 8 sản phẩm còn 1 câu SQL
- Đủ quy trình nhập, bán, kiểm kho, báo cáo

**Hình:** bảng bốn ô số liệu lớn (74, 12, 644, 1 câu SQL), và ảnh dashboard.

**Ghi chú trình bày (khoảng 45 giây)**
Số 74 là số thuộc tính HTTP đếm trong các controller; số 8→1 câu SQL lấy từ `PERFORMANCE.md` (đo trên dữ liệu ít dòng, nên chỉ nói về số câu lệnh, không nói về tốc độ). Không dùng từ "hoàn hảo" hay "vượt trội".

---

## Slide 12. Hạn chế và hướng phát triển

**Nội dung trên slide**
- Chưa hủy đơn bán đúng nghĩa; xóa không hoàn kho
- Chưa có thanh toán, công nợ, nhiều kho
- Giao diện thiếu danh sách đơn, tải hóa đơn, quản lý người dùng
- Hướng phát triển: hủy đơn, báo cáo lãi lỗ, kho vector

**Hình:** bảng hai cột "Hạn chế" và "Hướng khắc phục".

**Ghi chú trình bày (khoảng 45 giây)**
Nói hạn chế trước khi hội đồng hỏi; điều đó tạo uy tín. Có thể kể ngắn rằng khi rà soát báo cáo bằng cách đối chiếu với code, bạn đã phát hiện và sửa hai lỗi (đăng ký công khai cho chọn vai trò Admin; xóa sản phẩm đã có chứng từ không trả thông báo rõ), mỗi lỗi có test riêng. Điều đó cho thấy quy trình kiểm chứng, chứ không phải điểm yếu còn sót lại. Nhắc đăng ký công khai vẫn còn và chỉ tạo tài khoản BanHang.

---

## Slide 13. Demo (khoảng 3 phút)

**Nội dung trên slide**
- Kịch bản: nhập, bán, tồn kho, báo cáo, hỏi trợ lý
- (Không cần chữ; chuyển sang ứng dụng chạy thật)

**Hình:** ảnh chụp dự phòng từng bước, đề phòng mạng hoặc Docker lỗi.

**Ghi chú trình bày: kịch bản demo**
Kịch bản chi tiết, có mốc thời gian và phương án dự phòng, nằm ở `docs/DEMO_SCRIPT.md`; dùng một tài khoản `Admin` cho cả buổi. Tóm tắt luồng:
1. `/products`: đọc tồn "Bàn phím cơ" (N). `/purchases/create`: nhập 10 cái, phiếu được duyệt ngay; quay lại `/products` thấy N+10.
2. `/pos`: bán 2 cái cho "Khách demo" (tạo trước); thông báo hiện mã đơn `SO-yyyyMMdd-NNNN`; tồn còn N+8.
3. `/reports/revenue`: bấm "Xem báo cáo", đơn vừa bán nằm trong tháng này.
4. `/assistant`: hỏi tồn kho và chính sách đổi trả (câu sau cần đã nạp tài liệu và có khóa Voyage).

Giao diện **không có** trang xem sổ kho, danh sách đơn hay nút tải hóa đơn PDF; chỉ nói những chức năng này khi chỉ ra là có ở API (Swagger). Trợ lý cần `ANTHROPIC_API_KEY` và `VOYAGE_API_KEY` trong `.env`; nếu thiếu, quay trước một video ngắn làm dự phòng.

---

## Slide 14. Cảm ơn và Q&A

**Nội dung trên slide**
- Cảm ơn hội đồng đã lắng nghe
- Mã nguồn và tài liệu: (đường dẫn kho mã)
- Liên hệ: email

**Hình:** ảnh sơ đồ kiến trúc thu nhỏ (để quay lại khi trả lời).

**Ghi chú trình bày (khoảng 30 giây)**
Cảm ơn, mời đặt câu hỏi. Giữ sẵn slide 5 và 7 để quay lại.

---

## Phụ lục: câu hỏi hội đồng có thể hỏi (chuẩn bị trả lời)

| Câu hỏi | Trả lời dựa trên code (đã xác nhận trừ khi ghi khác) |
|---|---|
| Hai người bán cùng một sản phẩm cuối cùng thì sao? | `Product.RowVersion` phát hiện xung đột; đơn thua bị hoàn tác và chạy lại tối đa 5 lần với tồn mới; còn thiếu thì trả 409 (`SalesOrderService`). Có test trên SQL Server thật |
| Sao không dùng InMemory để test? | Không có `rowversion`, khóa dòng, giao dịch thật |
| Tồn kho có thể âm không? | Ba lớp: kiểm tra trong service, `RowVersion`, và `CK_Products_StockQuantity_NonNegative` ở CSDL |
| Hủy đơn bán thì tồn kho có được hoàn không? | **Không có chức năng hủy đơn bán.** Xóa đơn (`Admin`) xóa cứng và không hoàn tồn. Đây là hạn chế đã nêu |
| Trợ lý có bịa số liệu không? | Số liệu lấy từ công cụ đọc CSDL; system prompt buộc dùng kết quả công cụ. Chưa kiểm chứng bằng mô hình thật trong test |
| Trợ lý có lộ giá nhập hay thông tin khách không? | Công cụ không trả các trường đó (`GetPriceTool`, `GetOrderStatusTool`); trợ lý không có công cụ truy cập khách hàng |
| Vì sao Blazor Server mà không phải WebAssembly? | Token và gọi API nằm phía máy chủ; không cần cấu hình CORS (theo `CLAUDE.md`) |
| Lưu vector ở đâu, có mở rộng được không? | `KnowledgeChunks.Embedding` (`varbinary`), so cosine trong bộ nhớ ứng dụng (`RagRetriever`); chỉ hợp quy mô nhỏ, đã nêu ở hạn chế |
| Có refresh token, khóa tài khoản khi đăng nhập sai nhiều lần không? | Không. Chỉ có access token 60 phút; Admin khóa tài khoản thủ công |
| Có audit log không? | Không có audit log theo nghĩa bảng ghi vết thao tác; có log có cấu trúc (Serilog) và sổ kho cho biến động tồn |
