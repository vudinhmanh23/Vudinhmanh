# Backlog tính năng — Hệ thống Quản lý Bán hàng & Kho

**Phiên bản:** 1.0
**Ngày:** 2026-09-21
**Nguồn tham chiếu:** [`docs/SRS.md`](./SRS.md), [`docs/UseCases.md`](./UseCases.md) — backlog này chỉ chuyển hóa các FR/UC đã có sang dạng user story kèm ưu tiên và ước lượng, không bổ sung tính năng ngoài phạm vi đồ án.

## Quy ước

- **Ưu tiên (MoSCoW):** Must (bắt buộc để bảo vệ đồ án) / Should (nên có, tăng giá trị) / Could (có thì tốt, có thể lược bỏ nếu thiếu thời gian).
- **Story point:** thang Fibonacci rút gọn 1, 2, 3, 5, 8 — 1-2 = CRUD đơn giản, 3 = có logic nghiệp vụ/validate, 5 = có giao dịch nhiều bước, 8 = có yêu cầu atomic/rollback hoặc tích hợp phức tạp.
- Mã user story theo tiền tố module: `PLT` Nền tảng, `CAT` Danh mục, `PRD` Sản phẩm, `SUP` Nhà cung cấp, `CUS` Khách hàng, `PO` Đơn nhập hàng, `SO` Đơn bán hàng, `INV` Tồn kho, `RPT` Báo cáo, `AI` Trợ lý AI bán hàng.

---

## 1. Nền tảng (Xác thực, Phân quyền, Cấu hình, Logging)

| Mã | User Story | Ưu tiên | Điểm | Tham chiếu |
|---|---|---|---|---|
| PLT-01 | Là người dùng, tôi muốn đăng nhập bằng tài khoản/mật khẩu và bị tạm khóa 15 phút nếu sai quá 5 lần liên tiếp, để hệ thống chống dò mật khẩu. | Must | 3 | FR-01, UC-01 |
| PLT-02 | Là Admin, tôi muốn hệ thống phân quyền chức năng theo 3 vai trò (ADMIN/WAREHOUSE/SALES) và trả lỗi 403 khi vượt quyền, để đảm bảo mỗi nhân sự chỉ thao tác đúng phạm vi. | Must | 3 | FR-02 |
| PLT-03 | Là Admin, tôi muốn tạo, khóa/mở khóa và xóa mềm tài khoản người dùng, để kiểm soát ai được truy cập hệ thống mà không mất dữ liệu lịch sử. | Must | 3 | FR-03, UC-02 |
| PLT-04 | Là người dùng, tôi muốn được tự động đăng xuất sau 30 phút không thao tác, để bảo vệ phiên làm việc khi rời máy. | Must | 1 | FR-04 |
| PLT-05 | Là hệ thống, tôi cần phát hành và làm mới JWT (access + refresh token, hết hạn tối đa 60 phút), để mọi API được xác thực an toàn. | Must | 3 | NFR-02 |
| PLT-06 | Là hệ thống, tôi cần băm mật khẩu người dùng bằng thuật toán an toàn (BCrypt/PBKDF2), để không lưu mật khẩu dạng plaintext. | Must | 1 | NFR-01 |
| PLT-07 | Là Admin, tôi muốn hệ thống ghi audit log cho các thao tác nhạy cảm (đăng nhập, tạo/hủy đơn, điều chỉnh kho, đổi phân quyền), để truy vết khi cần. | Must | 3 | NFR-03 |
| PLT-08 | Là hệ thống, tôi cần bắt buộc toàn bộ giao tiếp client-server qua HTTPS, để bảo vệ dữ liệu truyền tải. | Must | 1 | NFR-04 |
| PLT-09 | Là Admin, tôi muốn cấu hình các tham số hệ thống (thuế suất VAT mặc định, ngưỡng cảnh báo tồn kho mặc định) tại một màn hình, để không phải sửa cứng trong code khi chính sách thay đổi. | Should | 2 | FR-BAN-06, FR-27 |
| PLT-10 | Là người dùng, tôi muốn mọi thông báo lỗi hiển thị bằng tiếng Việt, rõ nguyên nhân và không lộ mã lỗi kỹ thuật, để dễ hiểu và xử lý. | Should | 2 | NFR-12 |

**Tổng module:** 22 điểm (Must: 18, Should: 4)

---

## 2. Danh mục sản phẩm

| Mã | User Story | Ưu tiên | Điểm | Tham chiếu |
|---|---|---|---|---|
| CAT-01 | Là Admin/Quản lý kho, tôi muốn tạo danh mục sản phẩm với tên duy nhất, để chuẩn hóa cách phân loại hàng hóa. | Must | 2 | FR-05, UC-03 |
| CAT-02 | Là Admin/Quản lý kho, tôi muốn sửa tên và mô tả danh mục, để cập nhật khi cách phân loại thay đổi. | Must | 1 | FR-06 |
| CAT-03 | Là Admin/Quản lý kho, tôi muốn hệ thống chặn xóa danh mục đang có sản phẩm, để tránh sản phẩm bị mất danh mục gốc. | Must | 1 | FR-06 |
| CAT-04 | Là Admin/Quản lý kho, tôi muốn xem danh sách danh mục dạng phân trang kèm số lượng sản phẩm mỗi danh mục, để nắm nhanh cơ cấu hàng hóa. | Must | 2 | FR-07 |

**Tổng module:** 6 điểm (Must: 6)

---

## 3. Sản phẩm

| Mã | User Story | Ưu tiên | Điểm | Tham chiếu |
|---|---|---|---|---|
| PRD-01 | Là Admin/Quản lý kho, tôi muốn thêm sản phẩm mới với giá bán bắt buộc lớn hơn 0, để tránh sai sót dữ liệu giá ngay khi nhập liệu. | Must | 2 | FR-08, UC-04 |
| PRD-02 | Là Nhân viên bán hàng/Quản lý kho, tôi muốn tìm kiếm sản phẩm theo tên gần đúng và lọc theo danh mục, để tra cứu nhanh khi tư vấn hoặc kiểm kho. | Must | 3 | FR-09, UC-05 |
| PRD-03 | Là Admin/Quản lý kho, tôi muốn cập nhật tên/giá/danh mục sản phẩm nhưng không được sửa trực tiếp tồn kho từ màn hình này, để tồn kho luôn phản ánh đúng qua nghiệp vụ nhập/bán/điều chỉnh. | Must | 2 | FR-10 |
| PRD-04 | Là Admin/Quản lý kho, tôi muốn sản phẩm đã từng giao dịch chỉ bị chuyển "Ngừng kinh doanh" thay vì xóa cứng, để không phá vỡ dữ liệu đơn hàng lịch sử. | Must | 2 | FR-11 |

**Tổng module:** 9 điểm (Must: 9)

---

## 4. Nhà cung cấp

| Mã | User Story | Ưu tiên | Điểm | Tham chiếu |
|---|---|---|---|---|
| SUP-01 | Là Admin/Quản lý kho, tôi muốn thêm nhà cung cấp với số điện thoại đúng định dạng 10 số, để dữ liệu liên hệ luôn hợp lệ. | Must | 1 | FR-12, UC-06 |
| SUP-02 | Là Quản lý kho, tôi muốn tra cứu nhà cung cấp theo tên hoặc số điện thoại, để nhanh chóng tạo đơn nhập hàng. | Must | 1 | FR-13 |
| SUP-03 | Là Quản lý kho, tôi muốn xem lịch sử toàn bộ đơn nhập hàng của một nhà cung cấp, sắp xếp theo ngày giảm dần, để đánh giá độ tin cậy của nhà cung cấp đó. | Should | 2 | FR-14, UC-07 |

**Tổng module:** 4 điểm (Must: 2, Should: 2)

---

## 5. Khách hàng

| Mã | User Story | Ưu tiên | Điểm | Tham chiếu |
|---|---|---|---|---|
| CUS-01 | Là Nhân viên bán hàng, tôi muốn thêm khách hàng mới với số điện thoại duy nhất, để tránh tạo trùng hồ sơ khách hàng. | Must | 2 | FR-15, UC-08 |
| CUS-02 | Là Nhân viên bán hàng, tôi muốn tra cứu nhanh khách hàng theo số điện thoại ngay tại màn hình lập đơn bán hàng, để không phải nhập lại thông tin khách quen. | Must | 2 | FR-16 |
| CUS-03 | Là Nhân viên bán hàng, tôi muốn xem lịch sử mua hàng và tổng giá trị đã mua của một khách hàng, để có cơ sở tư vấn/chăm sóc khách thân thiết. | Should | 2 | FR-17, UC-09 |

**Tổng module:** 6 điểm (Must: 4, Should: 2)

---

## 6. Đơn nhập hàng

| Mã | User Story | Ưu tiên | Điểm | Tham chiếu |
|---|---|---|---|---|
| PO-01 | Là Quản lý kho, tôi muốn tạo đơn nhập hàng từ một nhà cung cấp với nhiều dòng sản phẩm, để bổ sung tồn kho khi hàng sắp hết. | Must | 3 | FR-18, UC-10 |
| PO-02 | Là Quản lý kho, tôi muốn xác nhận đơn nhập để hệ thống tự động cộng tồn kho cho toàn bộ dòng một cách nguyên tử (atomic), để tránh phải cập nhật tay và tránh lệch kho khi có lỗi giữa chừng. | Must | 5 | FR-19, UC-11 |
| PO-03 | Là Quản lý kho, tôi muốn hủy đơn nhập đang ở trạng thái "Nháp", để loại bỏ đơn lập sai mà chưa ảnh hưởng tồn kho. | Must | 1 | FR-20, UC-12 |
| PO-04 | Là Quản lý kho, tôi muốn xem danh sách đơn nhập hàng với bộ lọc theo thời gian, nhà cung cấp và trạng thái, để theo dõi tiến độ nhập hàng. | Should | 2 | FR-21 |

**Tổng module:** 11 điểm (Must: 9, Should: 2)

---

## 7. Đơn bán hàng

| Mã | User Story | Ưu tiên | Điểm | Tham chiếu |
|---|---|---|---|---|
| SO-01 | Là Nhân viên bán hàng, tôi muốn tạo đơn bán hàng mới gắn với khách hàng đã chọn hoặc "Khách lẻ" mặc định, để bắt đầu lập đơn nhanh chóng. | Must | 2 | FR-BAN-01, UC-13 |
| SO-02 | Là Nhân viên bán hàng, tôi muốn thêm nhiều dòng sản phẩm khác nhau vào cùng một đơn bán, để phục vụ khách mua nhiều mặt hàng cùng lúc. | Must | 2 | FR-BAN-02 |
| SO-03 | Là Nhân viên bán hàng, tôi muốn hệ thống tự gộp số lượng khi thêm sản phẩm đã có sẵn trong đơn, để tránh tạo dòng trùng gây nhầm lẫn. | Must | 1 | FR-BAN-03 |
| SO-04 | Là Nhân viên bán hàng, tôi muốn được cảnh báo ngay khi số lượng đặt vượt tồn kho khả dụng, để không hứa bán hàng không có sẵn. | Must | 3 | FR-BAN-04 |
| SO-05 | Là Nhân viên bán hàng, tôi muốn hệ thống tự tính tổng tiền hàng (subtotal) mỗi khi thêm/sửa/xóa dòng, để không phải tính tay. | Must | 2 | FR-BAN-05 |
| SO-06 | Là Nhân viên bán hàng, tôi muốn hệ thống tự tính thuế GTGT theo thuế suất cấu hình và hiển thị tách riêng, để đảm bảo minh bạch số liệu thuế. | Must | 2 | FR-BAN-06 |
| SO-07 | Là Nhân viên bán hàng, tôi muốn nhập chiết khấu trên tổng đơn và hệ thống tự tính tổng thanh toán, để rút ngắn thao tác chốt đơn. | Must | 2 | FR-BAN-07 |
| SO-08 | Là Nhân viên bán hàng, tôi muốn khi xác nhận thanh toán, hệ thống tự động trừ tồn kho cho toàn bộ dòng một cách nguyên tử (atomic) — nếu một dòng thất bại thì toàn đơn không xác nhận, để tồn kho luôn chính xác kể cả khi có giao dịch tranh chấp. | Must | 5 | FR-BAN-08, UC-14 |
| SO-09 | Là Nhân viên bán hàng, tôi muốn hủy đơn bán đã hoàn tất trong vòng 24 giờ và được hoàn trả đúng tồn kho đã trừ, để xử lý các trường hợp đặt nhầm/khách đổi ý. | Must | 3 | FR-BAN-09, UC-15 |
| SO-10 | Là Nhân viên bán hàng, tôi muốn xuất phiếu bán hàng dạng PDF cho đơn đã hoàn tất, để giao cho khách hàng làm chứng từ. | Should | 3 | FR-BAN-10, UC-16 |

**Tổng module:** 25 điểm (Must: 22, Should: 3)

---

## 8. Tồn kho

| Mã | User Story | Ưu tiên | Điểm | Tham chiếu |
|---|---|---|---|---|
| INV-01 | Là Quản lý kho, tôi muốn xem tồn kho hiện tại của tất cả sản phẩm, cập nhật gần thời gian thực sau mỗi giao dịch, để nắm chính xác số lượng còn lại. | Must | 3 | FR-26, UC-17 |
| INV-02 | Là Quản lý kho, tôi muốn hệ thống tự động gắn nhãn "Sắp hết hàng" cho sản phẩm dưới ngưỡng cấu hình, để kịp thời đặt hàng bổ sung. | Must | 2 | FR-27 |
| INV-03 | Là Quản lý kho, tôi muốn tạo phiếu điều chỉnh kho thủ công (tăng/giảm) kèm lý do bắt buộc và được ghi log, để phản ánh đúng thực tế khi hàng hỏng hoặc lệch kiểm kê. | Must | 3 | FR-28, UC-18 |

**Tổng module:** 8 điểm (Must: 8)

---

## 9. Báo cáo doanh thu & Thống kê

| Mã | User Story | Ưu tiên | Điểm | Tham chiếu |
|---|---|---|---|---|
| RPT-01 | Là Admin, tôi muốn xem báo cáo doanh thu theo khoảng thời gian tùy chọn (tổng doanh thu, số đơn, doanh thu trung bình/đơn), để đánh giá hiệu quả kinh doanh. | Must | 3 | FR-29, UC-19 |
| RPT-02 | Là Admin, tôi muốn xem Top 10 sản phẩm bán chạy theo số lượng và doanh thu, để ra quyết định nhập hàng/khuyến mãi. | Should | 2 | FR-30 |
| RPT-03 | Là Admin, tôi muốn xuất báo cáo doanh thu ra file Excel, để lưu trữ và trình bày cho cấp trên. | Should | 2 | FR-31, UC-20 |
| RPT-04 | Là Admin, tôi muốn xem biểu đồ doanh thu theo ngày trong 30 ngày gần nhất trên dashboard, để theo dõi xu hướng kinh doanh trực quan. | Should | 3 | FR-32 |

**Tổng module:** 10 điểm (Must: 3, Should: 7)

---

## 10. Trợ lý AI bán hàng

| Mã | User Story | Ưu tiên | Điểm | Tham chiếu |
|---|---|---|---|---|
| AI-01 | Là Nhân viên bán hàng, tôi muốn hỏi trợ lý AI bằng tiếng Việt tự nhiên để tra cứu sản phẩm và tồn kho, để tư vấn khách hàng nhanh hơn mà không cần thao tác tìm kiếm thủ công. | Should | 5 | FR-33, UC-21 |
| AI-02 | Là Nhân viên bán hàng, tôi muốn trợ lý AI gợi ý tối đa 5 sản phẩm liên quan khi đang lập đơn, để tăng cơ hội bán kèm. | Could | 3 | FR-34, UC-22 |
| AI-03 | Là Nhân viên bán hàng, tôi muốn yêu cầu trợ lý AI tóm tắt báo cáo doanh thu bằng ngôn ngữ tự nhiên dựa trên dữ liệu thật, để nắm nhanh tình hình mà không cần mở màn hình báo cáo. | Could | 3 | FR-35, UC-23 |
| AI-04 | Là Nhân viên bán hàng, tôi muốn nhận thông báo lỗi rõ ràng khi dịch vụ AI không khả dụng thay vì bị treo hoặc nhận câu trả lời bịa, để vẫn tin tưởng và tiếp tục dùng các chức năng cốt lõi khác. | Should | 2 | FR-36, NFR-08 |

**Tổng module:** 13 điểm (Should: 7, Could: 6)

---

## Tổng hợp toàn backlog

| Ưu tiên | Số story | Tổng điểm |
|---|---|---|
| Must | 36 | 81 |
| Should | 11 | 27 |
| Could | 2 | 6 |
| **Tổng** | **49** | **114** |

Kế hoạch triển khai theo tuần (bao gồm việc chọn tập con nào được xếp lịch trong 8 tuần, phần nào để ở backlog mở rộng) nằm trong [`docs/PLAN.md`](./PLAN.md).

---

## 11. Definition of Done — 5 tính năng Must-have quan trọng nhất

Mỗi tiêu chí dưới đây được viết ở dạng có thể kiểm chứng bằng test case hoặc quan sát trực tiếp (pass/fail rõ ràng), không dùng các cụm mơ hồ như "hoạt động tốt" hay "ổn định".

| # | Tính năng | Story liên quan | Definition of Done |
|---|---|---|---|
| 1 | Đăng nhập & phân quyền theo vai trò | PLT-01, PLT-02 | • API `POST /auth/login` trả JWT khi đúng thông tin, trả HTTP 401 khi sai.<br>• Test giả lập 5 lần đăng nhập sai liên tiếp trong 15 phút → tài khoản bị khóa, response chứa đúng chuỗi "Tài khoản tạm khóa do đăng nhập sai nhiều lần"; lần thử thứ 6 dù đúng mật khẩu vẫn bị từ chối.<br>• Mọi endpoint được gắn `[Authorize(Roles=...)]` đúng theo bảng vai trò SRS §3.2; có tối thiểu 1 test/vai trò × 1 nhóm chức năng bị chặn, gọi API ngoài quyền trả đúng HTTP 403 **và** dữ liệu trong DB không đổi trước/sau request.<br>• Màn hình đăng nhập Blazor tồn tại; sau đăng nhập, layout/menu hiển thị đúng theo vai trò (kiểm bằng 3 tài khoản test, mỗi vai trò 1 tài khoản). |
| 2 | Đơn bán hàng: tạo, tính tiền, thanh toán trừ kho | SO-01 → SO-09 | • API tạo đơn, thêm/sửa/xóa dòng, xác nhận thanh toán, hủy đơn — mỗi API có ≥ 1 unit test pass.<br>• Test thêm sản phẩm A 2 lần (số lượng 2, sau đó 3) → đơn chỉ còn 1 dòng A với số lượng 5.<br>• Test số liệu cụ thể theo AC của SRS: subtotal 180.000đ, chiết khấu 10.000đ, thuế 10% → tổng thanh toán trả đúng 187.000đ.<br>• Test nhập số lượng vượt tồn kho (còn 4, nhập 10) → API trả lỗi kèm đúng chuỗi "Số lượng tồn kho không đủ (còn lại: 4)", dòng trong DB không đổi.<br>• Test giả lập 1 trong 3 dòng lỗi khi xác nhận thanh toán → toàn bộ giao dịch rollback, tồn kho cả 3 sản phẩm không đổi so với trước khi xác nhận (transaction test, không chỉ test happy path).<br>• Test hủy đơn đã hoàn tất cách đây 2 giờ → tồn kho được cộng hoàn đúng số lượng đã trừ.<br>• Màn hình Blazor: thêm/sửa/xóa dòng sản phẩm, subtotal/thuế/tổng thanh toán cập nhật ngay không cần tải lại trang, nút Thanh toán và Hủy đơn hoạt động đúng luồng trạng thái. |
| 3 | Đơn nhập hàng: tạo, xác nhận cộng kho, hủy | PO-01, PO-02, PO-03 | • API tạo đơn nhập (trạng thái Nháp), xác nhận, hủy — mỗi API có ≥ 1 unit test pass.<br>• Test lưu đơn nhập với 0 dòng sản phẩm → bị từ chối, trả lỗi rõ ràng.<br>• Test xác nhận đơn 3 dòng hợp lệ → tồn kho cả 3 sản phẩm tăng đúng số lượng đã nhập (so khớp số liệu trước/sau bằng query DB).<br>• Test giả lập 1 dòng lỗi khi xác nhận → toàn bộ rollback, tồn kho không đổi ở bất kỳ dòng nào.<br>• Test hủy đơn ở trạng thái "Đã hoàn tất" → bị từ chối (chỉ hủy được đơn "Nháp").<br>• Màn hình Blazor: tạo đơn nhập (chọn NCC + thêm dòng sản phẩm), danh sách đơn theo trạng thái, nút Xác nhận/Hủy hiển thị đúng theo trạng thái đơn. |
| 4 | Tồn kho: xem real-time & cảnh báo sắp hết hàng | INV-01, INV-02 | • API `GET` tồn kho trả đúng số lượng hiện tại; test thực hiện 1 giao dịch nhập/bán rồi gọi lại API trong vòng 5 giây → số liệu đã phản ánh giao dịch đó.<br>• Test biên ngưỡng cảnh báo: sản phẩm tồn kho đúng bằng ngưỡng cấu hình → có nhãn "Sắp hết hàng"; tồn kho = ngưỡng + 1 → không có nhãn.<br>• Có API cập nhật ngưỡng cảnh báo theo từng sản phẩm, mặc định = 10 khi chưa cấu hình.<br>• Màn hình Blazor danh sách tồn kho hiển thị nhãn cảnh báo rõ ràng (badge/màu khác biệt), kiểm bằng cách so sánh với danh sách sản phẩm dưới ngưỡng lấy từ DB. |
| 5 | Quản lý sản phẩm & danh mục | PRD-01 → PRD-04, CAT-01 → CAT-04 | • Có API CRUD đầy đủ (list có phân trang, GET theo id, POST, PUT, soft-delete/ngừng kinh doanh) cho cả Sản phẩm và Danh mục.<br>• Test nhập giá bán ≤ 0 → API trả 400 kèm đúng thông báo "Giá bán phải lớn hơn 0"; sản phẩm không được lưu.<br>• Test tạo danh mục trùng tên (không phân biệt hoa/thường) → bị từ chối kèm thông báo "Tên danh mục đã tồn tại".<br>• Test xóa danh mục đang có ≥ 1 sản phẩm → bị từ chối; test xóa sản phẩm đã từng nằm trong 1 đơn nhập/bán → sản phẩm chuyển trạng thái "Ngừng kinh doanh" (không bị xóa khỏi DB) và biến mất khỏi danh sách bán hàng nhưng vẫn hiện trong lịch sử đơn hàng liên quan.<br>• Test tìm kiếm sản phẩm theo từ khóa gần đúng, không phân biệt hoa/thường, kết hợp lọc danh mục → trả đúng tập kết quả mong đợi.<br>• Có màn Blazor list/thêm/sửa/xóa cho cả Sản phẩm và Danh mục; danh sách danh mục phân trang tối đa 20 mục/trang kèm số lượng sản phẩm mỗi danh mục. |
