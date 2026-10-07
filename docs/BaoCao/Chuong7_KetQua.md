# CHƯƠNG 7. KẾT QUẢ ĐẠT ĐƯỢC

## 7.1. Đối chiếu với mục tiêu

Bảng dưới đối chiếu các mục tiêu ở Chương 1 (mục 1.3.2) với kết quả. Ba mức: **Đạt** (có cài đặt và có kiểm chứng), **Một phần** (có cài đặt nhưng thiếu một phần), **Chưa chạy thử** (có cài đặt, chưa kiểm chứng trong điều kiện thật).

| Mục tiêu | Mức | Căn cứ | Phần còn thiếu |
|---|---|---|---|
| M1. Quản lý sản phẩm, danh mục, nhà cung cấp, khách hàng | Một phần | API đầy đủ và có kiểm thử (Chương 3, 5) | Giao diện chỉ hoàn chỉnh cho sản phẩm; các trang danh mục, nhà cung cấp, khách hàng mới có tiêu đề; khách chỉ thêm nhanh được ở quầy bán hàng |
| M2. Phiếu nhập làm tăng tồn, đơn bán làm giảm tồn | Đạt (API) | Kiểm thử nghiệp vụ kho và HTTP (Chương 5) | Giao diện chỉ có lập phiếu nhập (duyệt ngay) và quầy bán hàng; chưa có danh sách, duyệt, hủy riêng lẻ |
| M3. Ghi sổ kho | Đạt (dữ liệu mới) | Nhập, bán, điều chỉnh, đảo phiếu nhập và tồn đầu kỳ đều ghi `StockMovements`; sửa sản phẩm không đổi tồn; test đối soát tổng sổ kho với tồn (`StockLedgerReconciliationTests`) | Dữ liệu có trước bản sửa (sản phẩm mẫu trong migration, sản phẩm tạo trước đây) có tồn mà thiếu dòng sổ (Chương 8, hạn chế 1) |
| M4. Tồn kho không âm khi bán đồng thời | Đạt | Test song song trên SQL Server thật; ràng buộc CHECK ở CSDL chặn tồn âm ở mọi đường | |
| M5. Phân quyền ba vai trò trên JWT | Đạt | Kiểm thử ma trận quyền; lỗi cho người lạ tự đăng ký `Admin` đã được sửa và có test | Không có refresh token, không thu hồi token (hạn chế 11) |
| M6. Dashboard, báo cáo doanh thu, xuất PDF và Excel | Một phần | Dashboard và báo cáo hiển thị trên giao diện; hóa đơn và báo cáo xuất file có ở API | Giao diện chưa có nút tải hóa đơn PDF hay báo cáo PDF, Excel |
| M7. Trợ lý AI | Một phần | Công cụ, RAG, giới hạn và lọc bí mật chạy theo thiết kế; kiểm thử với mô hình giả | Chưa kiểm chứng với mô hình thật trong test (test bị bỏ qua); chỉ ba công cụ, chỉ đọc |
| M8. Triển khai bằng Docker Compose | Đạt (máy phát triển), chưa chạy thử (VPS) | `README-deploy.md`, Chương 6 | Chưa triển khai trên VPS thật; chưa có TLS trong compose |

## 7.2. Kết quả kiểm thử

Một lần chạy `dotnet test` toàn solution (Chương 5, bảng 5.1) cho kết quả: **649 test, 648 đạt, 0 lỗi, 1 bỏ qua**. Test bị bỏ qua là test gọi mô hình AI thật, chỉ chạy khi có khóa API. Các test về tranh chấp đồng thời chạy trên SQL Server thật qua Testcontainers.

Ba lỗi được phát hiện khi đối chiếu báo cáo với mã nguồn (người lạ tự đăng ký tài khoản `Admin`; xóa sản phẩm đã có chứng từ không trả thông báo rõ; sửa hoặc tạo sản phẩm đổi tồn kho mà không ghi sổ kho) đã được sửa, mỗi lỗi có test riêng; khi gỡ bản sửa, 10 ca test chuyển sang đỏ (Chương 5, mục 5.3.3; Chương 8, mục 8.7).

> Số liệu này cần chạy lại bằng `dotnet test` ngay trước khi nộp báo cáo.

## 7.3. Kết quả về hiệu năng truy vấn

Theo `PERFORMANCE.md`, đếm sự kiện `CommandExecuted` của EF Core trên cơ sở dữ liệu thật (chỉ có vài dòng dữ liệu):

| Tình huống | Trước | Sau |
|---|---|---|
| Nạp sản phẩm khi tạo đơn bán, 8 dòng hàng khác nhau | 8 câu SQL | 1 câu |
| Kiểm tra sản phẩm tồn tại khi tạo phiếu nhập, N sản phẩm | N câu | 1 câu |
| Kiểm tra trùng SKU, barcode, mã nhà cung cấp | 1 câu tải toàn bộ bảng | 1 câu `EXISTS` |
| Chi tiết đơn bán | 32 cột | 18 cột |

Các số này chỉ chứng minh việc giảm số câu lệnh và lượng cột đọc. Do dữ liệu rất ít, **chưa có kết luận về tốc độ hay khả năng chịu tải**.

## 7.4. Minh họa hệ thống

`[chèn ảnh chụp màn hình thật của hệ thống, mỗi ảnh kèm chú thích "Hình 7.x. ..."; chỉ chụp chức năng đang chạy được]`

| Hình | Màn hình | Địa chỉ | Nội dung nên thể hiện |
|---|---|---|---|
| 7.1 | Đăng nhập | `/login` | Biểu mẫu đăng nhập |
| 7.2 | Danh sách sản phẩm | `/products` | Lọc, sắp xếp, cột Tồn kho |
| 7.3 | Lập phiếu nhập | `/purchases/create` | Chọn nhà cung cấp, thêm dòng, ô "Duyệt phiếu ngay" |
| 7.4 | Quầy bán hàng | `/pos` | Giỏ hàng, chọn khách, thông báo mã đơn |
| 7.5 | Dashboard | `/dashboard` | KPI và cảnh báo sắp hết hàng |
| 7.6 | Báo cáo doanh thu | `/reports/revenue` | Biểu đồ theo tháng, so sánh cùng kỳ |
| 7.7 | Trợ lý AI | `/assistant` | Một câu hỏi tồn kho và một câu hỏi chính sách có dòng "Nguồn" |
| 7.8 | Swagger (môi trường Development) | `/swagger` | Danh sách endpoint, để minh họa các chức năng chưa có trên giao diện |
| 7.9 | Kết quả `dotnet test` | Terminal | Dòng "Passed!" của hai project |

## 7.5. Kịch bản minh chứng nghiệp vụ

Luồng sau minh họa mối liên hệ giữa nhập, bán và tồn kho (thực hiện bằng tài khoản `Admin`, chi tiết ở `docs/DEMO_SCRIPT.md`):

1. Ghi lại số tồn hiện tại của một sản phẩm, gọi là N.
2. Lập phiếu nhập 10 sản phẩm đó và duyệt ngay: tồn thành N + 10.
3. Bán 2 sản phẩm đó tại quầy bán hàng: tồn thành N + 8.
4. Mở báo cáo doanh thu: đơn vừa bán nằm trong doanh thu tháng hiện tại.
5. Hỏi trợ lý AI về tồn kho của sản phẩm: câu trả lời lấy số từ công cụ tra cứu, cho kết quả N + 8.

`[điền: kết quả thực tế khi chạy kịch bản này (số N, mã đơn, ảnh chụp), nếu đã chạy]`

## 7.6. Đánh giá chung

Phần lõi nghiệp vụ (nhập hàng, bán hàng, sổ kho, xử lý đồng thời) và phân quyền đã được cài đặt và kiểm chứng bằng kiểm thử tự động, kể cả trên SQL Server thật. Giao diện, trợ lý AI và triển khai thật đạt ở mức một phần, với các hạn chế đã nêu rõ ở bảng 7.1 và ở Chương 8.
