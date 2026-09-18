# ADR 0001: Lựa chọn kiến trúc phân lớp cho hệ thống Quản lý Bán hàng & Kho

**Trạng thái:** Đã chấp nhận
**Ngày:** 2026-09-18
**Nguồn tham chiếu:** [`docs/architecture.md`](../architecture.md), [`docs/SRS.md`](../SRS.md)

## Bối cảnh

Đồ án tốt nghiệp xây dựng hệ thống Quản lý Bán hàng & Kho gồm ~8 module (sản phẩm, danh mục, nhà cung cấp, khách hàng, đơn nhập, đơn bán, tồn kho, báo cáo) và 1 module trợ lý AI bán hàng, dùng ASP.NET Core Web API + Blazor + SQL Server + EF Core. Nhóm thực hiện chỉ có 1-2 sinh viên với thời gian hạn chế, cần chọn kiến trúc:

- Đủ rõ ràng để trình bày và bảo vệ trước hội đồng
- Cho phép kiểm thử (unit test) lớp nghiệp vụ độc lập với cơ sở dữ liệu
- Không quá phức tạp so với quy mô nghiệp vụ thực tế (phần lớn module là CRUD, chỉ đơn nhập/đơn bán/tồn kho có logic tính toán)

## Quyết định

Chọn **Layered Architecture rút gọn có áp dụng Dependency Inversion** giữa lớp nghiệp vụ và lớp hạ tầng, chia thành 4 project: `Domain`, `Application`, `Infrastructure`, `Api`. Domain không phụ thuộc project nào khác; Application định nghĩa interface repository; Infrastructure hiện thực interface đó bằng EF Core; Api chỉ gọi Application và chỉ chạm Infrastructure ở bước đăng ký DI. Chi tiết bảng trách nhiệm và sơ đồ phụ thuộc xem tại `docs/architecture.md`.

## Các phương án đã cân nhắc

1. **Layered Architecture cổ điển (Controller → Service → DbContext trực tiếp, không tách interface)**
   Đơn giản, làm nhanh nhất, nhưng Service dễ bị phụ thuộc thẳng vào EF Core `DbContext` nếu không kỷ luật, khiến việc mock để unit test khó khăn (phải dùng InMemory DB thay vì mock thuần).

2. **Clean Architecture đầy đủ (thêm CQRS/MediatR, Domain Events, Specification pattern)**
   Tách biệt tối đa, dễ mở rộng dài hạn, nhưng lượng boilerplate (DTO/mapping riêng từng tầng, handler cho từng command/query) quá lớn so với nghiệp vụ chủ yếu là CRUD của 8 module — rủi ro không đủ thời gian hoàn thành trong khuôn khổ đồ án, và khó giải thích trước hội đồng lý do cần toàn bộ pattern đó.

3. **Layered Architecture rút gọn có Dependency Inversion (đã chọn)**
   Cân bằng giữa hai phương án trên: giữ số lượng project/abstraction tối thiểu nhưng đủ để cô lập lớp nghiệp vụ khỏi EF Core, phù hợp thời gian và quy mô nhóm.

## Hệ quả

**Tốt:**
- Lớp `Application` không phụ thuộc EF Core nên unit test business logic (tính tồn kho, tạo đơn nhập/bán) chỉ cần mock interface repository, chạy nhanh, không cần SQL Server
- Ranh giới giữa các lớp rõ ràng, dễ vẽ sơ đồ và giải thích Dependency Rule trước hội đồng
- Đổi công nghệ truy cập dữ liệu (VD: EF Core sang Dapper) chỉ ảnh hưởng `Infrastructure`, không đụng vào nghiệp vụ

**Đánh đổi:**
- Nhiều project hơn phương án cổ điển (4 thay vì 1-2), tăng nhẹ chi phí thiết lập ban đầu và số lượng interface/DTO phải viết
- Không có CQRS/MediatR nên nếu sau này nghiệp vụ phức tạp hơn nhiều (nhiều loại query/report đặc thù), có thể cần bổ sung pattern riêng cho từng trường hợp thay vì có sẵn khung xử lý chung
- Cần kỷ luật khi code (đặc biệt module thêm gấp rút) để không vô tình gọi thẳng `DbContext` từ `Api`, phá vỡ Dependency Rule đã đặt ra
