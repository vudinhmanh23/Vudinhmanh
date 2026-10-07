# Tối ưu truy vấn – số liệu đo được

Tất cả con số dưới đây được **đo trên CSDL thật** (`SalesInventoryDb`, SQL Server Express) bằng cách đếm sự kiện
`RelationalEventId.CommandExecuted` của EF Core. Mỗi phép đo dùng một `DbContext` mới, giống một request của API.
"Trước" là code ở commit trước khi tối ưu, "sau" là code hiện tại.

> Dữ liệu hiện chỉ có vài dòng, nên **số câu SQL và số cột đọc** là bằng chứng chính; thời gian chạy trên bảng nhỏ như vậy
> không có ý nghĩa nên không đưa vào.

## 1. Số câu SQL

| Tình huống | Trước | Sau |
|---|---|---|
| Chi tiết đơn bán `GET /api/sales-orders/{id}` | 1 câu | 1 câu |
| Nạp sản phẩm khi tạo đơn bán, 1 dòng hàng | 1 câu | 1 câu |
| Nạp sản phẩm khi tạo đơn bán, 3 dòng hàng khác nhau | 3 câu | **1 câu** |
| Nạp sản phẩm khi tạo đơn bán, 8 dòng hàng khác nhau | 8 câu | **1 câu** |
| Kiểm tra sản phẩm tồn tại khi tạo đơn nhập, N sản phẩm | N câu | **1 câu** |
| Kiểm tra trùng SKU / barcode / mã nhà cung cấp | 1 câu, tải **toàn bộ** bảng | 1 câu `EXISTS`, trả về 1 bit |

**Lưu ý trung thực:** endpoint chi tiết đơn bán *chưa bao giờ* là 1 + N, vì trước đây đã dùng `Include/ThenInclude`
(một câu có join). Chỗ N+1 thật nằm ở luồng **tạo đơn** (mỗi sản phẩm một lần `FindAsync`), nay còn 1 câu `WHERE Id IN (...)`.

## 2. Lượng dữ liệu đọc

| Truy vấn | Trước | Sau |
|---|---|---|
| Chi tiết đơn #4 | 32 cột (cả bản ghi `Product`: mô tả, ảnh, giá nhập…) | 18 cột (chỉ tên sản phẩm, tên khách và số liệu đơn) |
| Kiểm tra trùng SKU | 18 cột × mọi sản phẩm | 1 giá trị bit |

Danh sách đơn bán/đơn nhập cũng chiếu như chi tiết đơn, không tracking, và nhận `page`/`pageSize` tùy chọn
(tổng số đơn nằm ở header `X-Total-Count`).

## 3. Index

Migration `AddPerformanceIndexes` và `AddCustomerPhoneIndex` (đã áp bằng `dotnet ef database update`, kiểm tra bằng `sys.indexes`):

| Index | Vì sao hữu ích |
|---|---|
| `StockMovements (ProductId, CreatedAt, Id)` | Sổ kho của một sản phẩm, mới nhất trước: **Index Seek** theo `ProductId`, dữ liệu đã đúng thứ tự nên không cần Sort. |
| `SalesOrders (OrderDate, Id)`, `PurchaseOrders (OrderDate, Id)` | Danh sách đơn sắp xếp `OrderDate DESC, Id DESC`: đọc ngược index và dừng sau N dòng (có `TOP`), không Sort. |
| `Products (Name)`, `Products (SalePrice)` | Sắp xếp danh sách sản phẩm theo tên / giá. |
| `Products (CategoryId)` | Lọc theo danh mục (EF cũng tự tạo cho khóa ngoại). |
| `SalesOrderItems (ProductId)` + cột đi kèm | Báo cáo Top sản phẩm gom theo sản phẩm chỉ cần đọc index hẹp, không đọc cả dòng. |
| `Customers (Phone)` | Tra khách theo số điện thoại (không unique vì nhiều khách có thể trùng số). |

Đo thử `Customers.Phone` trên bảng tạm 50.000 dòng (không đụng dữ liệu thật):

| | Cách đọc | Chi phí ước tính |
|---|---|---|
| Không index | Clustered Index Scan | 0,310 |
| Có index | Index Seek + Key Lookup | 0,0066 (**thấp hơn khoảng 47 lần**) |

Trên bảng thật (1 khách) SQL Server vẫn chọn Scan vì quét 1 dòng rẻ hơn; index chỉ phát huy khi bảng đủ lớn.

## 4. Cách tự kiểm chứng

1. Chạy API ở môi trường Development (log `Default: Information` đã hiện câu SQL của EF).
2. Gọi `GET /api/sales-orders/4` và đếm dòng `Executed DbCommand` trong terminal: 1 câu, có join sang `Customers` và `Products`.
3. Gọi `POST /api/sales-orders` với nhiều sản phẩm khác nhau: phần nạp sản phẩm chỉ là một câu `WHERE ... IN (...)`.
4. Trong SSMS, bật *Include Actual Execution Plan* rồi chạy các câu ở mục 3 để xem Index Seek.
