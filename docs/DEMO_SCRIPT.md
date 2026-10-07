# Kịch bản demo 5 phút

**Câu chuyện:** nhập hàng vào kho → bán hàng → thấy tồn kho giảm → xem báo cáo → hỏi trợ lý AI.
**Tài khoản dùng:** chỉ `Admin` (tài khoản duy nhất được tạo sẵn qua `SeedAdmin`). Không dùng `Kho` hay `BanHang` trong demo: chưa xác nhận `Kho` bán được trên `/pos` (xem `Chuong3_ThietKe.md`, mục 3.4).
**Địa chỉ mặc định khi chạy `docker compose up`:** Blazor `http://localhost:5081`, API và Swagger `http://localhost:5080/swagger` (theo `docker-compose.yml`).

> **Về độ tin cậy của kịch bản.** Tên nút, nhãn và đường dẫn bên dưới lấy từ mã nguồn Razor (`src/SalesInventory.Web/Components/Pages/`), tôi chưa mở ứng dụng để chạy thử. **Hãy diễn tập đủ một lần** và sửa lại mọi chỗ lệch trước khi bảo vệ.

## Dữ liệu sẵn có và điều chuẩn bị bắt buộc

Dữ liệu seed trong code (`AppDbContext.OnModelCreating`, `HasData`): 5 danh mục, 1 nhà cung cấp `SUP-001` ("Nhà cung cấp mặc định"), 8 sản phẩm (ví dụ "Bàn phím cơ" SKU-DT-001, "Chuột không dây" SKU-DT-002). **Không có khách hàng nào được seed** và **chưa có đơn hàng nào**. Vì vậy cần chuẩn bị trước khi demo:

| # | Việc chuẩn bị (làm trước demo, không làm trên sân khấu) | Vì sao |
|---|---|---|
| 1 | `docker compose up --build`, đợi API và Blazor chạy; đã đặt `SEEDADMIN__EMAIL` và `SEEDADMIN__PASSWORD` trong `.env` | Có tài khoản Admin để đăng nhập |
| 2 | Đăng nhập, vào `/pos`, bấm "+ Thêm khách hàng mới", tạo khách tên "Khách demo" (trang `/customers` chỉ có tiêu đề, không dùng được) | Đơn bán bắt buộc có khách (`SalesOrder.CustomerId`) |
| 3 | Mở `http://localhost:5080/swagger`, đăng nhập lấy token, nhấn *Authorize*, gọi `POST /api/assistant/knowledge/ingest` | Nạp tài liệu chính sách vào RAG. Code không tự nạp khi khởi động, nên nếu bỏ bước này, câu hỏi chính sách sẽ không có đoạn tài liệu để trả lời |
| 4 | Đặt `ANTHROPIC_API_KEY` và `VOYAGE_API_KEY` trong `.env` | Trợ lý và RAG cần hai khóa này (`CLAUDE.md`) |
| 5 | Mở `/products`, **ghi lại số tồn hiện tại của "Bàn phím cơ"** (gọi là `N`) | Kịch bản dùng số tương đối (`N+10`, `N+8`), không phụ thuộc số seed |
| 6 | Mở sẵn 6 tab: `/login`, `/products`, `/purchases/create`, `/pos`, `/reports/revenue` và `/assistant` | Đỡ gõ địa chỉ trên sân khấu |
| 7 | Chạy thử toàn bộ kịch bản một lần, sau đó **không cần xóa dữ liệu**; mỗi lần chạy lại chỉ làm `N` tăng thêm | Tránh phải reset CSDL |

Swagger chỉ bật khi `ASPNETCORE_ENVIRONMENT=Development` (mặc định của compose). Nếu đã đặt Production thì bước 3 phải gọi bằng công cụ khác.

## Ba rủi ro lớn nhất khiến demo có thể sập

Đọc kịch bản với vai một giám khảo khó tính, ba chỗ dễ sập nhất, xếp theo mức nguy hiểm:

| # | Rủi ro | Vì sao có thật (từ code) | Hậu quả trước hội đồng | Kế hoạch B |
|---|---|---|---|---|
| R1 | **Trợ lý AI không trả lời** (bước cuối, điểm nhấn) | Phụ thuộc Anthropic, Voyage và mạng; giới hạn 10 lượt mỗi 60 giây mỗi người (`AiSafety:RateLimit`); thiếu khóa thì API trả 503; RAG chỉ có tài liệu khi đã gọi `knowledge/ingest` thủ công | Kết thúc demo bằng thông báo lỗi | Trước giờ bảo vệ hỏi thử đúng hai câu để chắc chắn chạy. Có sẵn ảnh chụp và video 40 giây câu trả lời thật (`docs/demo-backup/07-*`). Chỉ hỏi tối đa hai câu trên sân khấu để không chạm giới hạn tần suất. Nếu chỉ câu chính sách lỗi, bỏ câu đó. Nếu cả hai lỗi, nói thẳng "trợ lý cần dịch vụ bên ngoài" và chiếu video |
| R2 | **Phiên đăng nhập hết hạn hoặc hệ thống chưa sẵn sàng giữa buổi** | JWT hết hạn sau 60 phút (`Jwt:ExpiryMinutes`); khi API trả 401, giao diện xóa token và chuyển về `/login` (`AuthMessageHandler.cs`). API thoát nếu SQL Server chưa sẵn sàng khi khởi động rồi Docker khởi động lại (chú thích trong `docker-compose.yml`), có thể mất vài chục giây | Đang thao tác bị đẩy ra trang đăng nhập, hoặc mở trang thấy lỗi kết nối | Khởi động Compose từ 10 phút trước; **đăng nhập lại ngay trước khi lên sân khấu** để token còn gần 60 phút. Nếu bị chuyển về `/login`, đăng nhập lại (15 giây) và làm tiếp từ bước hiện tại. Nếu API không lên: `docker compose ps` rồi `docker compose logs api`; quá 20 giây thì chuyển sang video |
| R3 | **Giao diện hoặc dữ liệu khác với kịch bản** | Không có trang danh sách đơn; mã đơn chỉ hiện trong một thông báo thoáng qua; phiếu nhập duyệt lỗi thì giao diện không có nút duyệt lại (xem bước 3); không có khách nào được seed; số tồn `N` đổi sau mỗi lần chạy thử | Nói sai số, không tìm thấy khách, không chỉ lại được đơn vừa bán | Ghi `N` ngay trước khi lên. Tạo sẵn "Khách demo". Không cần mã đơn để đi tiếp: chứng minh bằng số tồn trên thẻ sản phẩm ở `/pos` và `/products`. Muốn xem lại đơn thì dùng Swagger `GET /api/sales-orders` (cần token). Các ảnh `docs/demo-backup/04-*` và `05-*` chứa mã đơn và số tồn đúng |

**Quy tắc quyết định:** một bước quá 20 giây hoặc báo lỗi hai lần thì chuyển sang kế hoạch B của bước đó ngay, không debug trên sân khấu. Hai bước hỏng liên tiếp thì chuyển hẳn sang video toàn bộ.

## Bảng thời gian

| Mốc | Bước | Màn hình |
|---|---|---|
| 0:00-0:30 | Giới thiệu và đăng nhập | `/login` |
| 0:30-1:00 | Xem tồn kho ban đầu | `/products` |
| 1:00-2:00 | Lập phiếu nhập, duyệt ngay | `/purchases/create` |
| 2:00-3:15 | Bán hàng tại quầy | `/pos` |
| 3:15-3:45 | Tồn kho đã giảm | `/products` |
| 3:45-4:15 | Báo cáo doanh thu | `/reports/revenue` |
| 4:15-5:00 | Hỏi trợ lý AI | `/assistant` |

---

## Bước 1. Giới thiệu và đăng nhập (0:00-0:30)

- **Mở:** `http://localhost:5081/login`
- **Thao tác:** nhập email và mật khẩu Admin, bấm đăng nhập.
- **Nói:** "Em xin demo một ngày làm việc của cửa hàng: nhập hàng, bán hàng, xem tồn kho, xem báo cáo và hỏi trợ lý. Em đăng nhập bằng tài khoản Admin; hệ thống dùng JWT và có ba vai trò."
- **Lưu ý:** gõ mật khẩu nhanh hoặc dán sẵn; đừng để lộ mật khẩu trên màn chiếu nếu có thể.
- **Ảnh dự phòng:** `docs/demo-backup/01-dang-nhap.png`
- **Kế hoạch B:** nếu bị đẩy về `/login` giữa buổi vì token hết hạn (60 phút), đăng nhập lại rồi tiếp tục; nếu đăng nhập lỗi, kiểm tra `SEEDADMIN__*` trong `.env` và log của container `api`. Nếu vẫn lỗi, chuyển sang slide ảnh chụp màn hình các bước tiếp theo.

## Bước 2. Xem tồn kho ban đầu (0:30-1:00)

- **Mở:** `/products`
- **Thao tác:** gõ "Bàn phím" vào ô tìm kiếm (nếu có), chỉ vào dòng "Bàn phím cơ", cột **Tồn kho** đang là `N`.
- **Nói:** "Đây là sản phẩm 'Bàn phím cơ', tồn kho hiện là N. Mọi thay đổi số này từ giờ đều có đơn nhập hoặc đơn bán đứng sau."
- **Ảnh dự phòng:** `docs/demo-backup/02-ton-kho-ban-dau.png`
- **Kế hoạch B:** nếu danh sách không tải, tải lại trang một lần. Nếu vẫn trống, API chưa chạy: xem `docker compose ps`.

## Bước 3. Lập phiếu nhập và duyệt (1:00-2:00)

- **Mở:** `/purchases/create` (tiêu đề "Lập phiếu nhập hàng")
- **Thao tác:**
  1. Ô *Nhà cung cấp*: chọn "Nhà cung cấp mặc định".
  2. Phần *Thêm dòng*: *Sản phẩm* chọn "Bàn phím cơ (SKU-DT-001)", *Số lượng* 10, *Giá nhập* ví dụ 400000, bấm **Thêm vào phiếu**.
  3. Ô **"Duyệt phiếu ngay (cộng số lượng vào tồn kho)"** đã được chọn sẵn (`_approveNow = true` trong `CreatePurchase.razor`); chỉ cần kiểm tra còn chọn.
  4. Bấm **Lưu phiếu nhập**.
- **Kết quả mong đợi:** thông báo "Đã lưu và duyệt phiếu nhập PO-yyyyMMdd-NNN" rồi chuyển về `/purchases`; trang này **gần như trống** (chỉ tiêu đề và nút), đừng giới thiệu nó là danh sách phiếu. Quay lại `/products` để xem tồn tăng.
- **Nói:** "Phiếu nhập có hai trạng thái: nháp không đổi tồn kho, chỉ khi duyệt mới cộng. Em chọn duyệt ngay. Việc cộng tồn và ghi sổ kho nằm trong một giao dịch, nên không xảy ra tình trạng tồn tăng mà không có chứng từ."
- **Ảnh dự phòng:** `docs/demo-backup/03-lap-phieu-nhap.png` (kèm thông báo "Đã lưu và duyệt phiếu nhập")
- **Kế hoạch B:**
  - Nếu lưu báo lỗi, đọc thông báo (toast) và sửa dữ liệu rõ ràng (thiếu nhà cung cấp, số lượng ≤ 0).
  - Nếu phiếu lưu được nhưng không duyệt được, trang sẽ báo "đã lưu ở trạng thái nháp" và gợi ý "duyệt lại từ danh sách phiếu nhập", **nhưng giao diện không có danh sách đơn nhập hay nút duyệt** (`Purchases.razor` chỉ có tiêu đề và nút "Lập phiếu nhập"). Phương án thật duy nhất: mở Swagger và gọi `POST /api/purchase-orders/{id}/approve` (cần token Admin hoặc Kho).
  - Nếu cả bước này hỏng, bỏ qua và coi `N` là tồn hiện tại; bước 4 vẫn chạy được.

## Bước 4. Bán hàng tại quầy (2:00-3:15)

- **Mở:** `/pos` (tiêu đề "Quầy bán hàng")
- **Thao tác:**
  1. Ô tìm "Tìm theo tên hoặc mã SKU": gõ "Bàn phím", bấm vào thẻ sản phẩm một lần để thêm vào giỏ.
  2. Trong *Giỏ hàng*, bấm **+** một lần để số lượng thành 2.
  3. *Khách hàng*: chọn "Khách demo".
  4. Bấm **Chốt đơn**, rồi xác nhận trong hộp thoại "Xác nhận chốt đơn cho khách …".
  5. Đọc mã đơn trong thông báo "Đã tạo đơn #SO-..." (chỉ hiện vài giây; giỏ hàng được xóa và số tồn trên các thẻ sản phẩm được cập nhật ngay tại `/pos`). Giao diện không có danh sách đơn, nên không xem lại mã sau đó được.
- **Nói:** "Em chọn sản phẩm, đặt số lượng 2 và chọn khách. Khi chốt đơn, server kiểm tra cả đơn có đủ tồn không, tự tính lại tiền, trừ kho và ghi sổ kho trong một giao dịch. Nếu hai nhân viên cùng bán sản phẩm cuối cùng, đơn nào thua sẽ được chạy lại với tồn mới; nếu hết hàng, hệ thống từ chối với thông báo rõ."
- **Lưu ý:** mã đơn dùng ngày theo giờ UTC (`SalesOrderService.GenerateOrderNumberAsync`), nên gần nửa đêm có thể lệch ngày địa phương. Đọc đúng mã hiển thị trên màn hình, đừng tự gõ.
- **Ảnh dự phòng:** `docs/demo-backup/04-pos-chot-don.png` (kèm thông báo mã đơn)
- **Kế hoạch B:**
  - Nếu danh sách khách trống hoặc có lỗi tải khách, bấm "+ Thêm khách hàng mới", nhập tên, **Lưu khách hàng**, rồi chọn khách vừa tạo.
  - Nếu thẻ sản phẩm bị mờ, sản phẩm hết hàng: chọn sản phẩm khác (ví dụ "Chuột không dây") và sửa lời thoại cho khớp.
  - Nếu chốt đơn báo lỗi, đọc thông báo; nếu là lỗi hết hạn đăng nhập, đăng nhập lại và làm lại từ đầu bước 4.

## Bước 5. Tồn kho đã giảm (3:15-3:45)

- **Mở:** `/products`, tải lại trang.
- **Thao tác:** chỉ vào "Bàn phím cơ"; tồn kho là `N+8` (cộng 10 ở bước 3, trừ 2 ở bước 4).
- **Nói:** "Tồn kho đã khớp: nhập 10, bán 2, còn N cộng 8. Mỗi biến động này có một dòng trong sổ kho `StockMovements`, nên truy vết được nhập từ phiếu nào, bán từ đơn nào."
- **Lưu ý:** hiện **chưa có trang xem sổ kho trên giao diện Blazor** (không có trang nào cho `StockMovements`). Nếu hội đồng muốn xem, mở Swagger và gọi `GET /api/products/{id}/movements` (cần token Admin hoặc Kho).
- **Ảnh dự phòng:** `docs/demo-backup/05-ton-kho-sau-ban.png`
- **Kế hoạch B:** nếu số không như mong đợi, đừng cố giải thích ngay; chuyển sang Swagger, xem sổ kho của sản phẩm, đó chính là bằng chứng truy vết.

## Bước 6. Báo cáo doanh thu (3:45-4:15)

- **Mở:** `/reports/revenue` (tiêu đề "Báo cáo doanh thu")
- **Thao tác:** khoảng thời gian mặc định là 12 tháng gần nhất, nhóm theo *Tháng*; bấm **Xem báo cáo**. Chỉ vào tháng hiện tại có đơn vừa bán.
- **Nói:** "Báo cáo chỉ tính các đơn hoàn thành, gom theo ngày, tháng hoặc quý, có thể so với cùng kỳ năm trước. Đơn em vừa bán đã nằm trong doanh thu tháng này."
- **Ảnh dự phòng:** `docs/demo-backup/06-bao-cao-doanh-thu.png`
- **Kế hoạch B:** nếu biểu đồ trống, kiểm tra khoảng ngày có chứa hôm nay; chọn *Nhóm theo = Ngày* để thấy đơn rõ hơn. Nếu vẫn trống, mở `/dashboard` (cũng cho Admin) làm phương án thay thế. Chỉ nói các số có trên màn hình.

## Bước 7. Hỏi trợ lý AI (4:15-5:00)

- **Mở:** `/assistant`
- **Thao tác:** gõ lần lượt hai câu và bấm **Gửi**:
  1. "Bàn phím cơ còn bao nhiêu cái?"
  2. "Chính sách đổi trả của cửa hàng như thế nào?"
- **Nói:** "Trợ lý không tự nhớ số liệu: nó gọi công cụ tra cứu tồn kho và trả đúng số hiện tại, `N+8`. Với câu về chính sách, nó tìm trong tài liệu của cửa hàng và ghi nguồn. Trợ lý chỉ đọc dữ liệu, không tạo hay sửa đơn."
- **Kỳ vọng:** câu 1 trả đúng số tồn từ công cụ `get_stock`; câu 2 trả nội dung từ `chinh-sach-doi-tra.md` kèm dòng "(Nguồn: …)", nếu đã nạp tài liệu ở bước chuẩn bị 3 và có khóa Voyage.
- **Ảnh dự phòng:** `docs/demo-backup/07-tro-ly-ai.png` và `07-tro-ly-ai.mp4` (video khoảng 40 giây)
- **Kế hoạch B:**
  - Nếu báo lỗi hoặc không phản hồi (thiếu khóa, mạng, hết hạn mức 10 lượt mỗi phút), nói thẳng: "Trợ lý cần dịch vụ bên ngoài; em xin chiếu video ghi sẵn" và mở video quay trước.
  - Nếu câu 2 trả "Xin lỗi, tôi không có thông tin này trong tài liệu của cửa hàng", chưa nạp tài liệu: bỏ câu 2, chỉ làm câu 1.
  - Không thử câu hỏi ngoài kịch bản (ví dụ hỏi giá nhập hay thông tin khách) trên sân khấu, trừ khi bạn đã thử và biết kết quả.

---

## Phương án tổng thể nếu hệ thống không chạy được

1. **Video dự phòng:** quay trước toàn bộ kịch bản (5 phút) bằng cùng dữ liệu. Chuẩn bị trước, đặt trên máy trình chiếu.
2. **Ảnh chụp từng bước:** 7 ảnh tương ứng 7 bước, lưu trong `docs/demo-backup/` (tên file ghi ở mỗi bước) và chèn vào slide 13.
3. **Quyết định nhanh:** nếu một bước lỗi quá 20 giây, chuyển sang dự phòng của bước đó, đừng debug trên sân khấu. Nếu hai bước lỗi liên tiếp, chuyển hẳn sang video.

## Việc không nên làm khi demo

- Không xóa đơn bán: xóa đơn không hoàn tồn kho (đã nêu ở hạn chế). Xóa sản phẩm đã có chứng từ nay bị từ chối 409, nhưng cũng không cần thử trên sân khấu.
- Không bán vượt tồn để "khoe" thông báo hết hàng, nếu chưa diễn tập: kết quả đúng nhưng thêm một chỗ có thể sai dữ liệu.
- Không cần đăng ký tài khoản mới trên sân khấu.
- Không dùng tài khoản `Kho` hay `BanHang` nếu chưa thử.
- Không mở file cấu hình, `.env` hay user-secrets trên màn chiếu.

## Danh sách kiểm tra 10 phút trước khi vào phòng

- [ ] `docker compose ps`: ba container `sqlserver`, `api`, `blazor` đang chạy.
- [ ] Đã đăng nhập được; có khách "Khách demo"; **đăng nhập lại ngay trước khi lên sân khấu** (token chỉ sống 60 phút).
- [ ] Đã chụp đủ 7 ảnh dự phòng vào `docs/demo-backup/` và quay video trợ lý (xem ba rủi ro ở đầu file).
- [ ] Đã nạp tài liệu RAG (gọi `knowledge/ingest` một lần).
- [ ] Đã hỏi trợ lý thử một câu và có trả lời.
- [ ] Biết số `N` hiện tại của "Bàn phím cơ".
- [ ] Video dự phòng mở được trên máy trình chiếu.
- [ ] Tắt thông báo hệ điều hành; phóng to trình duyệt (Ctrl và +) cho dễ đọc.
