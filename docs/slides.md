---
marp: true
theme: default
size: 16:9
paginate: true
header: "Hệ thống Quản lý Bán hàng & Kho"
style: |
  section { font-size: 30px; padding: 56px 70px; }
  section.lead { text-align: center; justify-content: center; }
  h1 { font-size: 1.5em; color: #1f4e79; }
  h2 { color: #1f4e79; }
  li { margin: 0.25em 0; }
  table { font-size: 0.72em; margin: 0 auto; }
  th, td { padding: 6px 14px; }
  small, .note { font-size: 0.6em; color: #555; }
---
<!--
Bản Marp của docs/SLIDE_OUTLINE.md. Ghi chú trình bày nằm trong các khối chú thích HTML như khối này (xuất ra PDF kèm ghi chú bằng cờ --pdf-notes).
Chỗ [điền] là thông tin cá nhân bạn phải tự điền. Sơ đồ Mermaid (ERD, sequence, kiến trúc, use case) Marp không vẽ được: xuất ảnh PNG từ các sơ đồ trong docs/BaoCao/Chuong3_ThietKe.md rồi chèn bằng ![w:900](img/ten-anh.png) vào đúng slide.
-->

<!-- _class: lead -->
<!-- _paginate: false -->
<!-- _header: "" -->

# Hệ thống Quản lý Bán hàng & Kho

ASP.NET Core Web API · Blazor Server · SQL Server

**[Họ và tên]** · MSSV [MSSV] · Lớp [Lớp]
GVHD: [Họ tên giảng viên]
[Tên trường] · 2026

<!-- Chào hội đồng, nêu tên. Nói đề tài là hệ thống chạy được, không chỉ là mô hình. (15 giây) -->

---

# Đặt vấn đề và mục tiêu

- Cửa hàng nhỏ quản lý hàng bằng sổ, bảng tính
- Hậu quả: tồn kho sai, bán vượt tồn, kho âm
- Mục tiêu: hệ thống ghi vết biến động kho

<!-- Ví dụ cụ thể: hai nhân viên cùng bán cái cuối cùng của một mặt hàng. Điểm khác biệt của đề tài là toàn vẹn tồn kho khi nhiều người dùng đồng thời, chứ không chỉ CRUD. Nhắc lại ở slide 7. (45 giây) -->

---

# Phạm vi và chức năng

- Sản phẩm, danh mục, nhà cung cấp, khách hàng
- Đơn nhập (nháp, duyệt, hủy) và đơn bán
- Tồn kho có sổ kho; dashboard; báo cáo doanh thu
- Trợ lý AI tra cứu tồn, giá, đơn, chính sách
- Ba vai trò: Admin, Kho, BanHang

<!-- Chèn use case diagram nếu có chỗ. Đơn nhập/đơn bán đầy đủ ở API; giao diện chỉ có lập phiếu nhập và quầy bán hàng. Danh mục, nhà cung cấp, khách hàng quản lý qua API (trang giao diện mới có tiêu đề). BanHang không xem dashboard; Kho không quản lý khách. Ngoài phạm vi: thanh toán, công nợ, nhiều kho. (45 giây) -->

---

# Công nghệ sử dụng

| Lớp | Công nghệ |
|---|---|
| API | ASP.NET Core 8 Web API |
| Giao diện | Blazor Server |
| Dữ liệu | SQL Server, EF Core Code First |
| Xác thực | ASP.NET Identity, JWT |
| AI | Anthropic API, Voyage embeddings |
| Triển khai | Docker Compose |

<!-- Chỉ nói vì sao: Blazor Server gọi API từ phía máy chủ, trình duyệt không gọi API trực tiếp (token mã hóa trong sessionStorage); SQL Server vì cần rowversion và giao dịch. (30 giây) -->

---

# Kiến trúc hệ thống

| Trình duyệt | → | Blazor Server | → (JWT) | Web API | → (EF Core) | SQL Server |
|---|---|---|---|---|---|---|

- Bốn lớp: Domain, Application, Infrastructure, Api
- Dịch vụ ngoài: Anthropic, Voyage, Open Food Facts

<!-- Thay bằng ảnh sơ đồ kiến trúc (Hình 3.1 trong Chuong3_ThietKe.md) nếu có. Nghiệp vụ nằm ở lớp Application nên controller mỏng. Repository và Unit of Work vì giao dịch kho cần Unit of Work. (1 phút) -->

---

# Cơ sở dữ liệu

| Bảng chính | Vai trò |
|---|---|
| `Products` | Tồn kho, `RowVersion`, CHECK ≥ 0 |
| `PurchaseOrders` / `…Items` | Đơn nhập và dòng hàng |
| `SalesOrders` / `…Items` | Đơn bán và dòng hàng |
| `StockMovements` | Sổ kho: nhập, bán, điều chỉnh |

12 bảng nghiệp vụ và AI · 29 migration

<!-- Thay bằng ảnh ERD (mục 3.2.2 trong Chuong3_ThietKe.md). Ba tầng bảo vệ tồn kho: mã nghiệp vụ, RowVersion, ràng buộc CHECK ở CSDL. Sổ kho liên kết chứng từ qua RefType và RefId, không có khóa ngoại. (1 phút 15 giây) -->

---

# Nghiệp vụ trọng tâm

- **Nhập:** nháp không đổi kho; duyệt mới cộng tồn
- **Bán:** kiểm tra cả đơn → trừ kho → ghi sổ
- Tất cả trong một giao dịch, lỗi thì hoàn tác
- Thua cuộc đua: thử lại tối đa 5 lần
- Thiếu hàng: trả 409, nêu rõ sản phẩm

<!-- Slide quan trọng nhất. Chèn sequence diagram "Tạo đơn bán và trừ tồn kho". Đi theo: kiểm tra đầu vào, tính lại tiền ở server, mở giao dịch, kiểm tra tồn, trừ kho, ghi sổ, commit. Cùng sản phẩm nhiều dòng được cộng lại trước khi so với tồn. Chỉ nói những gì test đã chứng minh (slide 9). (2 phút) -->

---

# Trợ lý AI bán hàng

- Ba công cụ: tồn kho, giá bán, trạng thái đơn
- RAG: trả lời chính sách từ tài liệu cửa hàng
- Chỉ đọc dữ liệu; không tạo hay sửa đơn
- Che bí mật; giới hạn chi phí và tần suất

<!-- Số liệu lấy từ công cụ, không từ trí nhớ mô hình. Công cụ không trả giá nhập hay thông tin khách. Giới hạn: 10 lượt mỗi phút mỗi người dùng, tối đa 1024 token một câu trả lời, câu ngắn dùng mô hình rẻ hơn. Về prompt injection: test dùng mô hình giả luôn tuân lệnh, chỉ chứng minh máy chủ giữ kín luật, không khẳng định gì về mô hình thật. (1 phút 30 giây) -->

---

# Bảo mật và kiểm thử

- JWT hết hạn sau 60 phút; phân quyền ba vai trò
- Production từ chối chạy nếu bí mật nằm trong file
- **644 test, 643 đạt, 0 lỗi, 1 bỏ qua**
- Test đồng thời chạy trên SQL Server thật

<!-- Chạy lại dotnet test trước khi bảo vệ và cập nhật số. Test bị bỏ qua là test gọi mô hình AI thật, cần khóa API. Dùng SQL Server thật vì InMemory không có rowversion và khóa dòng. Không nói an toàn tuyệt đối. (1 phút) -->

---

# Triển khai

- `docker compose up`: SQL Server, API, Blazor
- Bí mật chỉ qua biến môi trường
- Tự áp migration khi khởi động (tùy chọn)
- Production: HTTPS, HSTS, CORS chặt

<!-- Có hướng dẫn VPS trong README-deploy.md. CHỈ nói đã triển khai lên VPS nếu bạn thực sự đã làm; nếu chưa, nói đã chạy bằng Docker Compose và có tài liệu triển khai. [điền: sự thật của bạn] (30 giây) -->

---

# Kết quả đạt được

| 74 | 12 | 644 | 8 → 1 |
|---|---|---|---|
| endpoint | bảng | test tự động | câu SQL khi tạo đơn |

- Đủ quy trình nhập, bán, kiểm kho, báo cáo

<!-- 74 là số thuộc tính HTTP trong 16 controller. "8 → 1 câu SQL" lấy từ PERFORMANCE.md, đo trên dữ liệu ít dòng nên chỉ nói về số câu lệnh, không nói về tốc độ. Không dùng từ hoàn hảo hay vượt trội. (45 giây) -->

---

# Hạn chế và hướng phát triển

- Chưa hủy đơn bán; xóa đơn không hoàn kho
- Chưa có thanh toán, công nợ, nhiều kho
- Giao diện thiếu danh sách đơn, tải hóa đơn
- Hướng phát triển: hủy đơn có hoàn kho, báo cáo lãi lỗ

<!-- Nói hạn chế trước khi hội đồng hỏi. Có thể kể ngắn: khi rà soát báo cáo bằng cách đối chiếu với code, đã phát hiện và sửa hai lỗi (đăng ký công khai cho chọn Admin; xóa sản phẩm đã có chứng từ không trả thông báo rõ), mỗi lỗi có test riêng. Đăng ký công khai vẫn còn và chỉ tạo tài khoản BanHang. (45 giây) -->

---

<!-- _class: lead -->

# Demo

nhập hàng → bán hàng → tồn kho → báo cáo → trợ lý AI

<!-- Kịch bản chi tiết: docs/DEMO_SCRIPT.md. Dùng một tài khoản Admin. Có ảnh chụp và video dự phòng cho từng bước. (3 phút) -->

---

<!-- _class: lead -->
<!-- _header: "" -->

# Cảm ơn hội đồng

Mời quý thầy cô đặt câu hỏi

[Đường dẫn kho mã] · [Email]

<!-- Giữ sẵn slide 5 (kiến trúc) và slide 7 (nghiệp vụ trọng tâm) để quay lại khi trả lời. Phụ lục câu hỏi: docs/QA_PHAN_BIEN.md. (30 giây) -->
