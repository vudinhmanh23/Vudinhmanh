# CHƯƠNG 9. KẾT LUẬN

## 9.1. Kết quả chính

Đồ án đã xây dựng được **Hệ thống Quản lý Bán hàng và Kho** gồm Web API ASP.NET Core 8, giao diện Blazor Server, cơ sở dữ liệu SQL Server qua EF Core và một trợ lý AI tra cứu dữ liệu cửa hàng. Hệ thống có 74 endpoint trên 16 controller, 12 bảng nghiệp vụ và AI cùng các bảng Identity (qua 29 migration), và được đóng gói để chạy bằng Docker Compose.

Phần cốt lõi đã đạt được:

- **Quy trình nhập, bán, tồn kho:** phiếu nhập ở trạng thái nháp, duyệt thì cộng tồn; đơn bán kiểm tra đủ tồn cho cả đơn rồi trừ kho; mọi biến động nhập, bán, điều chỉnh đều ghi vào sổ kho.
- **Tồn kho không âm khi nhiều người bán cùng lúc,** dựa trên ba lớp bảo vệ: kiểm tra trong service trong một giao dịch, khóa lạc quan `RowVersion` kèm vòng thử lại, và ràng buộc CHECK ở cơ sở dữ liệu. Điều này được kiểm chứng bằng test chạy trên SQL Server thật.
- **Phân quyền ba vai trò** trên JWT, có kiểm thử ma trận quyền.
- **Trợ lý AI** dùng công cụ tra cứu dữ liệu thật và tìm tài liệu chính sách (RAG), với các lớp bảo vệ ở phía máy chủ (che bí mật, giới hạn chi phí và tần suất).
- **Kiểm thử tự động:** 649 test, 648 đạt, 0 lỗi, 1 bỏ qua.

## 9.2. Bài học

- **Tranh chấp đồng thời cần được chứng minh trên cơ sở dữ liệu thật.** Các bộ cung cấp dữ liệu trong bộ nhớ không có `rowversion` hay khóa dòng, nên không chứng minh được tính chất này.
- **Kiểm thử xanh không đồng nghĩa với đúng.** Một lỗ hổng về vai trò đã tồn tại trong khi hơn 600 test đều xanh, vì chính hàm hỗ trợ của test dựa vào nó. Lỗi chỉ được phát hiện khi đọc mã và đối chiếu với báo cáo.
- **An toàn của trợ lý AI không nên dựa vào lời dặn trong prompt.** Các lớp kiểm soát ở máy chủ (che bí mật, giới hạn, bọc đầu vào không tin cậy) chặn được lỗi ngay cả khi mô hình không tuân theo; nhưng các test hiện chỉ chứng minh phía máy chủ, không chứng minh hành vi của mô hình thật.
- **Tài liệu yêu cầu phải được đối chiếu với mã.** Tài liệu thiết kế ban đầu mô tả nhiều tính năng chưa được cài đặt; báo cáo chỉ dựa vào mã nguồn.
- **Công cụ AI hỗ trợ lập trình cần được rà soát bằng kiểm chứng độc lập** (đọc mã, chạy thử, kiểm thử), thay vì tin kết quả chỉ vì mã biên dịch được. `[điền: nhận xét cá nhân về cách làm việc với công cụ AI, chỉ ghi điều đúng]`

## 9.3. Hạn chế

Hệ thống còn các hạn chế đã trình bày ở Chương 8, đáng kể nhất là: dữ liệu có trước bản sửa sổ kho còn thiếu dòng giải thích tồn, chưa có chức năng hủy đơn bán, nhiều chức năng của API chưa có trên giao diện, bảo mật đăng nhập mới ở mức ghi nhận chứ chưa chặn, và chưa kiểm chứng trợ lý với mô hình thật cũng như triển khai trên máy chủ thật.

## 9.4. Hướng phát triển

Ngắn hạn là bù sổ kho cho dữ liệu cũ, thêm hủy đơn bán có hoàn kho và bổ sung các trang giao diện còn thiếu. Trung hạn là refresh token, giới hạn tần suất đăng nhập, nhật ký kiểm toán và TLS trong triển khai. Dài hạn là nhiều kho, thanh toán và công nợ, cơ sở dữ liệu vector cho tìm kiếm tài liệu (Chương 8, mục 8.8). Các mục này là đề xuất, chưa được cài đặt.
