# CHƯƠNG 1. ĐẶT VẤN ĐỀ VÀ MỤC TIÊU

## 1.1. Bối cảnh

Một cửa hàng bán lẻ nhỏ thường phải theo dõi cùng lúc ba việc: hàng nhập từ nhà cung cấp nào, hàng đã bán cho ai, và trong kho hiện còn bao nhiêu. Khi các thông tin này nằm rải rác ở sổ tay, bảng tính hoặc trí nhớ của nhân viên, số lượng tồn kho dễ sai lệch và rất khó truy ra nguyên nhân của sự sai lệch.

Đồ án xây dựng **Hệ thống Quản lý Bán hàng và Kho**: một ứng dụng web cho phép quản lý danh mục sản phẩm, nhà cung cấp và khách hàng, lập phiếu nhập hàng, bán hàng tại quầy, theo dõi tồn kho, xem báo cáo doanh thu và hỏi đáp nhanh với một trợ lý ứng dụng trí tuệ nhân tạo.

## 1.2. Vấn đề cần giải quyết

Từ bối cảnh trên, đồ án tập trung vào các vấn đề sau:

1. **Tồn kho sai lệch.** Nếu số tồn chỉ là một con số được sửa tay thì không biết nó tăng hoặc giảm vì lý do gì. Hệ thống cần ghi lại từng biến động (nhập, bán, điều chỉnh) kèm chứng từ nguồn.
2. **Bán vượt tồn khi nhiều người thao tác đồng thời.** Hai nhân viên cùng bán sản phẩm cuối cùng thì cả hai đơn có thể cùng được chấp nhận và tồn kho thành số âm. Đây là bài toán tranh chấp đồng thời, và là điểm kỹ thuật chính của đồ án.
3. **Phân quyền theo vai trò.** Nhân viên bán hàng, nhân viên kho và quản trị viên cần các quyền khác nhau; số liệu như doanh thu không nên mở cho mọi người.
4. **Hỗ trợ tra cứu nhanh.** Nhân viên thường phải trả lời khách các câu hỏi lặp lại (còn hàng không, giá bao nhiêu, đơn đã xong chưa, chính sách đổi trả). Một trợ lý có thể tra cứu dữ liệu thật giúp việc này, với điều kiện không lộ dữ liệu nhạy cảm và không bịa số liệu.

## 1.3. Mục tiêu

### 1.3.1. Mục tiêu chung

Xây dựng một hệ thống quản lý bán hàng và kho chạy được, có kiểm thử tự động và có thể triển khai bằng container.

### 1.3.2. Mục tiêu cụ thể

| # | Mục tiêu | Cách kiểm chứng |
|---|---|---|
| M1 | Quản lý sản phẩm, danh mục, nhà cung cấp, khách hàng | Các API tương ứng và kiểm thử API (Chương 3, 5) |
| M2 | Lập phiếu nhập (nháp, duyệt, hủy) làm tăng tồn kho; tạo đơn bán làm giảm tồn kho | Kiểm thử nghiệp vụ kho (Chương 5) |
| M3 | Ghi sổ kho cho các biến động nhập, bán, điều chỉnh | Bảng `StockMovements` (Chương 3, 4) |
| M4 | Tồn kho không bao giờ âm kể cả khi nhiều đơn bán đồng thời | Kiểm thử đồng thời trên SQL Server thật (Chương 5) |
| M5 | Phân quyền ba vai trò `Admin`, `Kho`, `BanHang` trên JWT | Kiểm thử phân quyền (Chương 5) |
| M6 | Dashboard, báo cáo doanh thu theo ngày, tháng, quý; xuất hóa đơn và báo cáo ra PDF, Excel | Chương 3, 4 |
| M7 | Trợ lý AI tra cứu tồn, giá, đơn và chính sách cửa hàng, có giới hạn chi phí và chống lộ bí mật | Chương 4, 5 |
| M8 | Triển khai được bằng Docker Compose | Chương 6 |

Mục M1 đến M8 là mục tiêu đặt ra từ đầu; mức độ đạt được của từng mục, kể cả những chỗ chưa đạt, được trình bày ở Chương 7 và Chương 8.

## 1.4. Đối tượng và phạm vi

**Đối tượng sử dụng:** cửa hàng bán lẻ nhỏ với ba nhóm người dùng: quản trị viên (`Admin`), nhân viên kho (`Kho`) và nhân viên bán hàng (`BanHang`).

**Trong phạm vi:** sản phẩm, danh mục, nhà cung cấp, khách hàng, đơn nhập, đơn bán, tồn kho và sổ kho, báo cáo doanh thu, trợ lý AI, xác thực và phân quyền, ghi log, đóng gói bằng Docker.

**Ngoài phạm vi (không có trong hệ thống):** thanh toán trực tuyến, công nợ, thuế, nhiều kho, hủy đơn bán, ứng dụng di động. Một số chức năng đã có ở API nhưng chưa có trên giao diện; chi tiết ở Chương 8.

## 1.5. Phương pháp thực hiện

- **Kiến trúc phân lớp** (Domain, Application, Infrastructure, Api) kết hợp giao diện Blazor Server riêng gọi API từ phía máy chủ.
- **Code First** với EF Core: lược đồ cơ sở dữ liệu sinh ra từ các entity qua migration.
- **Kiểm thử tự động** gồm kiểm thử đơn vị và kiểm thử tích hợp. Các kiểm thử về tranh chấp đồng thời chạy trên SQL Server thật trong container, vì các bộ cung cấp dữ liệu trong bộ nhớ không mô phỏng được `rowversion` và khóa dòng.
- **Cấu hình theo môi trường:** bí mật chỉ đi qua biến môi trường ở Production.

### Công cụ AI hỗ trợ lập trình

Mã nguồn của đồ án được viết với sự hỗ trợ của công cụ AI lập trình Claude Code (Anthropic). Lịch sử Git ghi rõ điều này: 62 trên 63 commit đầu tiên của kho mã (trước các commit hoàn thiện báo cáo) có dòng `Co-Authored-By: Claude`. `[điền: phần việc do sinh viên tự quyết định và thực hiện (yêu cầu, kiến trúc, thiết kế dữ liệu, rà soát và sửa mã, kiểm thử...); chỉ ghi những gì đúng sự thật]`. Báo cáo này cũng được soạn với sự hỗ trợ của công cụ đó, và mọi khẳng định về hệ thống đã được đối chiếu với mã nguồn; những chỗ chưa kiểm chứng được ghi rõ là "chưa chạy thử".

## 1.6. Bố cục báo cáo

- **Chương 1** nêu vấn đề, mục tiêu và phạm vi.
- **Chương 2** phân tích yêu cầu chức năng và phi chức năng.
- **Chương 3** trình bày thiết kế: kiến trúc, cơ sở dữ liệu, chức năng và API.
- **Chương 4** mô tả cài đặt và công nghệ.
- **Chương 5** trình bày kiểm thử và kết quả.
- **Chương 6** trình bày triển khai.
- **Chương 7** tổng hợp kết quả đạt được.
- **Chương 8** nêu hạn chế và hướng phát triển.
- **Chương 9** là kết luận và **Chương 10** là tài liệu tham khảo.
