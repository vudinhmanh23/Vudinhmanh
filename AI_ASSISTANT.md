# Trợ lý AI bán hàng: function calling (tool use)

Trợ lý trả lời câu hỏi về **giá, tồn kho và trạng thái đơn hàng bằng số liệu thật** trong SQL Server, thay vì để mô hình tự đoán.
Mô hình ngôn ngữ chỉ được gọi từ **backend** (`SalesInventory.Api`); khóa API nằm trong user-secrets, không bao giờ xuống Blazor.

## 1. Ba tool

| Tool | Tham số vào | Dữ liệu trả về | Nguồn dữ liệu (EF Core, `AsNoTracking`) |
|---|---|---|---|
| `get_stock` | `sku` hoặc `product_name` | tồn kho, đơn vị, trạng thái (còn hàng / sắp hết / hết hàng) | `Products` |
| `get_price` | `sku` hoặc `product_name` | giá bán `salePrice` và chuỗi định dạng `550.000 đ` (không bao giờ có giá nhập) | `Products` |
| `get_order_status` | `order_code` | trạng thái (Hoàn thành / Đã hủy), tổng tiền, ngày, số dòng hàng (không có thông tin khách) | `SalesOrders` |

Mỗi tool khai báo `name`, `description` và `input_schema` (JSON Schema) đúng định dạng của Messages API. Mã nguồn:
`src/SalesInventory.Application/Assistant/` (tool, đăng ký tool theo vai trò) và `src/SalesInventory.Infrastructure/Ai/AnthropicChatService.cs` (vòng lặp).

## 2. Vòng lặp tool use

```
Người dùng ──"Giá SKU-DT-001?"──► POST /api/assistant/ask (JWT, rate limit, giới hạn độ dài)
                                          │
                                          ▼
                              AnthropicChatService (backend, giữ khóa)
   ┌──────────────────────────────────────────────────────────────────────────────────┐
   │ 1. Gửi: system (luật) + câu hỏi + danh sách tool (lọc theo vai trò)  ──► MÔ HÌNH   │
   │ 2. MÔ HÌNH trả stop_reason = "tool_use": {name: get_price, input: {sku: ...}}      │
   │ 3. Backend chạy hàm .NET tương ứng ──► EF Core ──► SQL Server (dữ liệu THẬT)       │
   │ 4. Backend gửi lại: lượt trả lời của mô hình + tool_result (JSON gọn)  ──► MÔ HÌNH │
   │ 5. Lặp 2-4 nếu mô hình cần thêm tool (tối đa MaxToolRounds vòng)                   │
   │ 6. MÔ HÌNH trả stop_reason = "end_turn" + câu trả lời bằng văn bản                 │
   └──────────────────────────────────────────────────────────────────────────────────┘
                                          │
                                          ▼
                    { answer, toolsUsed, inputTokens, outputTokens } ──► người dùng
```

Điểm kỹ thuật chính:
- Mọi `tool_result` của một lượt được gộp vào **một** tin nhắn người dùng, kèm `tool_use_id` tương ứng.
- Lượt trả lời của mô hình được gửi lại **nguyên văn** (kể cả khối suy nghĩ), không chỉ phần chữ.
- Tool chạy **tuần tự** vì dùng chung một `DbContext` (không an toàn đa luồng).
- Tool lạ, không đủ quyền hoặc bị lỗi được báo lại cho mô hình dưới dạng `tool_result` có `is_error: true`; yêu cầu không bị hỏng.
- Vòng lặp có giới hạn (`Anthropic:MaxToolRounds`, mặc định 4) để mô hình không gọi tool vô hạn.

## 3. Bằng chứng: nhật ký một lần chạy

Nhật ký dưới đây được tạo bằng cách chạy **mã thật** của `AnthropicChatService` và các tool trên **CSDL SQL Server thật**.
Chỉ có các câu trả lời của mô hình được **kịch bản hóa** (không có khóa API trong môi trường thử), nên đây chứng minh phần backend:
tool nhận đúng lệnh, truy vấn đúng dữ liệu, và kết quả quay lại mô hình. Số liệu khớp SQL (SKU-DT-001: giá 550.000, tồn 114;
đơn SO-20261006-0001: tổng 1.650.000).

```
Cau hoi: "SKU-DT-001 gia bao nhieu va con bao nhieu?"
  [backend -> model]  POST /v1/messages #1  model-tier=sonnet  max_tokens=1024  tools=[get_stock,get_price,get_order_status]  messages=1
  [model -> backend]  stop_reason=tool_use  tool_use get_price {"sku":"SKU-DT-001"}
  [backend log Information] Assistant tool get_price finished (error: False)
  [backend -> model]  POST /v1/messages #2  ...  messages=3
                      carries tool_result id=toolu_01 is_error=False content={"sku":"SKU-DT-001","name":"Bàn phím cơ","salePrice":550000.00,"salePriceFormatted":"550.000 đ","discontinued":false}
  [model -> backend]  stop_reason=tool_use  tool_use get_stock {"sku":"SKU-DT-001"}
  [backend log Information] Assistant tool get_stock finished (error: False)
  [backend -> model]  POST /v1/messages #3  ...  messages=5
                      carries tool_result id=toolu_02 is_error=False content={"sku":"SKU-DT-001","name":"Bàn phím cơ","stockQuantity":114,"unit":"cái","status":"còn hàng","discontinued":false}
  [model -> backend]  stop_reason=end_turn  text="Bàn phím cơ (SKU-DT-001) có giá 550.000 đ và còn 114 cái."
  [backend -> nguoi dung] toolsUsed=[get_price,get_stock]

Cau hoi: "Gia SP999 la bao nhieu?"                       <- mã không tồn tại
  [model -> backend]  stop_reason=tool_use  tool_use get_price {"sku":"SP999"}
  [backend log Information] Assistant tool get_price finished (error: True)
  [backend -> model]  ...  carries tool_result id=toolu_03 is_error=True content=Không tìm thấy sản phẩm với SKU đã cho.
  [model -> backend]  stop_reason=end_turn  text="Xin lỗi, tôi không tìm thấy sản phẩm này."
  [backend -> nguoi dung] toolsUsed=[get_price]

Cau hoi: "Don SO-20261006-0001 the nao?"
  [model -> backend]  stop_reason=tool_use  tool_use get_order_status {"order_code":"SO-20261006-0001"}
  [backend log Information] Assistant tool get_order_status finished (error: False)
  [backend -> model]  ...  carries tool_result id=toolu_04 is_error=False content={"orderCode":"SO-20261006-0001","status":"Hoàn thành","orderDate":"2026-10-06","totalAmount":1650000.00,"totalFormatted":"1.650.000 đ","itemCount":1}
  [model -> backend]  stop_reason=end_turn  text="Đơn SO-20261006-0001 đã hoàn thành, tổng tiền 1.650.000 đ."
  [backend -> nguoi dung] toolsUsed=[get_order_status]
```

> **Ảnh chụp để bảo vệ (bạn tự bổ sung):** sau khi đặt khóa, gọi `POST /api/assistant/ask` trong Swagger với câu
> "SKU-DT-001 giá bao nhiêu và còn bao nhiêu?", rồi chụp (1) phản hồi có `toolsUsed` và (2) terminal của API có các dòng
> `Assistant tool ... finished`. Dán ảnh vào đây để thay cho phần mô hình kịch bản hóa ở trên.

## 4. Khi không tìm thấy dữ liệu

System prompt (cấu hình `Anthropic:SystemPrompt`) buộc: mọi con số phải lấy từ kết quả tool; nếu tool báo không tìm thấy sản phẩm, trợ lý
trả lời đúng câu **"Xin lỗi, tôi không tìm thấy sản phẩm này."** và không đoán mã hay số liệu (tương tự cho đơn hàng). Kết quả "không tìm thấy" quay lại mô hình
dưới dạng `is_error`, trong đó **không có con số nào** để mô hình lặp lại.

## 5. An toàn (tóm tắt)

- Khóa API chỉ ở server (user-secrets / biến môi trường), không có trong prompt, log hay phản hồi (nếu mô hình lặp lại khóa, server thay bằng `[đã ẩn]`).
- Tool chỉ trả dữ liệu cần thiết: không giá nhập, không thông tin khách; có test kiểm tra điều này.
- Luật trong `system` (không ghi đè được bằng nội dung người dùng); kết quả tool chỉ là dữ liệu, không phải mệnh lệnh.
- Rate limit theo người dùng (`Anthropic:RequestsPerMinute`, mặc định 10/phút), `max_tokens` cố định ở server (1024), câu hỏi tối đa 4000 ký tự.
- Mô hình chọn theo **tầng** (Sonnet mặc định); id cụ thể chỉ nằm trong `appsettings.json` mục `Anthropic:Models`.

## 6. Cách tự kiểm chứng

1. `dotnet user-secrets set "Anthropic:ApiKey" "<khóa>" --project src/SalesInventory.Api` (chạy trong terminal riêng).
2. `dotnet run --project src/SalesInventory.Api --launch-profile https`, mở `https://localhost:7028/swagger`, đăng nhập rồi Authorize.
3. Gọi `POST /api/assistant/ask`:
   - `{"question":"SKU-DT-001 giá bao nhiêu?"}` → số khớp `SELECT SalePrice FROM Products WHERE Sku='SKU-DT-001'`.
   - `{"question":"Giá SP999 là bao nhiêu?"}` → "Xin lỗi, tôi không tìm thấy sản phẩm này." (không có con số).
   - `{"question":"Đơn SO-20261006-0001 thế nào?"}` → trạng thái và tổng tiền khớp `SalesOrders`.
4. Log API hiện `Assistant tool get_price finished (error: False)`; phản hồi có `toolsUsed`.
5. Test tự động: `dotnet test tests/SalesInventory.Api.Tests --filter "FullyQualifiedName~Assistant"`.
