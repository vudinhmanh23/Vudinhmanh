# Kế hoạch triển khai theo tuần — Hệ thống Quản lý Bán hàng & Kho

**Phiên bản:** 2.0 (cập nhật do rút ngắn còn 7 tuần)
**Ngày:** 2026-09-21
**Nguồn tham chiếu:** [`docs/BACKLOG.md`](./BACKLOG.md) — kế hoạch này xếp lịch các user story đã có trong backlog vào 7 tuần phát triển, không bổ sung tính năng mới.

> **Lịch sử thay đổi:** Bản 1.0 giả định 8 tuần (7 tuần phát triển + 1 tuần kiểm thử riêng). Do timeline rút còn **7 tuần**, bản 2.0 dồn tuần kiểm thử vào chung với tuần phát triển cuối cùng và cắt/hoãn các tính năng ưu tiên thấp để không tuần nào vượt năng lực. Chi tiết lý do cắt ở §4.

## 0. Giả định & ràng buộc năng lực

- Thời gian còn lại: **7 tuần** (không còn tuần riêng cho kiểm thử).
- Năng lực xử lý: **10–13 điểm/tuần** ⇒ 7 tuần có sức chứa tối đa **91 điểm**, nhưng tuần cuối phải nhường phần lớn thời gian cho kiểm thử/tài liệu/bảo vệ nên chỉ tính là ~3 điểm dev, còn lại là hoạt động QA không tính điểm.
- Tổng điểm Must-have: **81 điểm / 36 story** — đây là phần **không được cắt**, phải hoàn thành trọn vẹn.
- Với ràng buộc này, 6 tuần đầu (Tuần 1–6) phải gánh **78/81 điểm Must** (vừa đúng mức trần 13đ/tuần × 6 = 78, không còn dư điểm nào), 3 điểm Must còn lại (RPT-01 — báo cáo doanh thu) dồn sang đầu Tuần 7 cùng kiểm thử.
- **Hệ quả:** không còn điểm dư trong Tuần 1–7 cho bất kỳ story Should/Could nào — toàn bộ 27 điểm Should và 6 điểm Could phải chuyển sang "Backlog mở rộng" (§4), thay vì chỉ cắt riêng Could như dự kiến ban đầu (xem giải thích chi tiết ở §4).

---

## 1. Milestone

| Milestone | Phạm vi tuần | Mục tiêu | Module chính |
|---|---|---|---|
| **M1 — Nền tảng & CRUD lõi** | Tuần 1–3 | Có đủ auth/phân quyền và dữ liệu chủ (danh mục, sản phẩm, NCC, khách hàng) để làm nền cho nghiệp vụ nhập/bán. | Nền tảng, Danh mục, Sản phẩm, Nhà cung cấp, Khách hàng |
| **M2 — Nghiệp vụ nhập/bán & tồn kho** | Tuần 4–6 | Vận hành được **toàn bộ** vòng đời đơn nhập, đơn bán và tồn kho (kể cả điều chỉnh kho thủ công) — không còn phần nào bị đẩy sang tuần sau. | Đơn nhập hàng, Đơn bán hàng, Tồn kho |
| **M3 — Hoàn thiện Must còn lại & Kiểm thử/Bảo vệ** | Tuần 7 | Hoàn tất báo cáo doanh thu cơ bản (Must cuối cùng), sau đó dồn toàn lực kiểm thử theo tiêu chí chấp nhận SRS, viết tài liệu, chuẩn bị bảo vệ. | Báo cáo (tối thiểu), QA toàn hệ thống |

So với bản 8 tuần cũ: M3 (Báo cáo & Trợ lý AI) và M4 (Kiểm thử & hoàn thiện) được **gộp làm một**, vì không còn tuần dư. Trợ lý AI không còn xuất hiện trong milestone chính thức nào — toàn bộ nằm ở backlog mở rộng.

---

## 2. Sơ đồ timeline (Mermaid Gantt)

```mermaid
gantt
    title Kế hoạch 7 tuần triển khai đồ án (rút ngắn từ 8 tuần)
    dateFormat  YYYY-MM-DD
    axisFormat  %d/%m
    todayMarker off

    section M1: Nền tảng & CRUD lõi
    Tuần 1 - Auth, phân quyền, cấu hình bảo mật :m1w1, 2026-09-22, 7d
    Tuần 2 - Quản lý tài khoản & Danh mục       :m1w2, after m1w1, 7d
    Tuần 3 - Sản phẩm, Nhà cung cấp, Khách hàng :m1w3, after m1w2, 7d

    section M2: Nghiệp vụ nhập/bán & tồn kho
    Tuần 4 - Đơn nhập hàng (tạo/xác nhận/hủy)   :m2w4, after m1w3, 7d
    Tuần 5 - Đơn bán hàng: dòng SP, tính tiền   :m2w5, after m2w4, 7d
    Tuần 6 - Đơn bán hàng: thanh toán & tồn kho :m2w6, after m2w5, 7d

    section M3: Hoàn thiện & Kiểm thử/Bảo vệ
    Tuần 7 - Báo cáo cơ bản + Test + Bảo vệ     :crit, m3w7, after m2w6, 7d
```

---

## 3. Kế hoạch chi tiết theo tuần

| Tuần | Milestone | Story thực hiện (mã) | Nội dung | Tổng điểm |
|---|---|---|---|---|
| **1** | M1 | PLT-06, PLT-08, PLT-05, PLT-01, PLT-04, PLT-02, CAT-02 | Băm mật khẩu, HTTPS, JWT+refresh, đăng nhập+khóa tài khoản sai 5 lần, session timeout, phân quyền RBAC + lỗi 403, sửa danh mục | 1+1+3+3+1+3+1 = **13** |
| **2** | M1 | PLT-03, PLT-07, CAT-01, CAT-03, CAT-04 SUP-01, SUP-02 | Quản lý tài khoản người dùng, audit log, tạo danh mục + chặn xóa danh mục còn sản phẩm + danh sách phân trang, thêm/tra cứu nhà cung cấp | 3+3+2+1+2+1+1 = **13** |
| **3** | M1 | PRD-01, PRD-02, PRD-03, PRD-04, CUS-01, CUS-02 | CRUD sản phẩm (validate giá, ngừng kinh doanh), tìm kiếm sản phẩm, thêm/tra cứu khách hàng theo SĐT | 2+3+2+2+2+2 = **13** |
| **4** | M2 | PO-01, PO-02, PO-03, SO-01, SO-02 | Tạo/xác nhận (atomic)/hủy đơn nhập hàng, khởi tạo đơn bán hàng, thêm dòng sản phẩm | 3+5+1+2+2 = **13** |
| **5** | M2 | SO-03, SO-04, SO-05, SO-06, SO-07, INV-01 | Gộp dòng trùng, cảnh báo vượt tồn kho, tính subtotal/thuế GTGT/chiết khấu & tổng thanh toán, xem tồn kho real-time | 1+3+2+2+2+3 = **13** |
| **6** | M2 | SO-08, SO-09, INV-02, INV-03 | Xác nhận thanh toán trừ kho (atomic), hủy đơn 24h hoàn kho, cảnh báo sắp hết hàng, phiếu điều chỉnh kho thủ công + log | 5+3+2+3 = **13** |
| **7** | M3 | RPT-01 *(story Must duy nhất còn lại)* | Đầu tuần: hoàn thành báo cáo doanh thu theo khoảng thời gian. Phần còn lại của tuần: kiểm thử tích hợp toàn hệ thống theo bảng tiêu chí chấp nhận (SRS §7), kiểm thử theo từng vai trò, sửa lỗi phát sinh, viết báo cáo đồ án, chuẩn bị slide + kịch bản demo, tổng duyệt trước bảo vệ | 3 (+ QA không tính điểm) |

**Kiểm tra ràng buộc:** Tuần 1–6 đều đúng **13/13 điểm** (chạm trần, không vượt); Tuần 7 chỉ còn 3 điểm dev, phần lớn thời gian dành cho QA — không tuần nào bị dồn quá tải hay bỏ trống. Toàn bộ 36 story Must (81 điểm) được hoàn thành chậm nhất đầu Tuần 7.

⚠️ **Lưu ý rủi ro:** lịch Tuần 1–6 không còn điểm dư (buffer = 0) do phải nén từ 7 tuần phát triển xuống 6. Nếu một tuần bất kỳ bị trễ (ốm, bug khó, bảo vệ đồ án khác), tuần 7 sẽ phải gánh thêm cả phần Must dở dang lẫn kiểm thử — rủi ro cao nhất nằm ở Tuần 4 và Tuần 6 vì chứa các story atomic phức tạp (PO-02, SO-08, đều 5 điểm).

---

## 4. Backlog mở rộng — lý do cắt/hoãn từng nhóm

Theo yêu cầu ban đầu, tôi rà lại để cắt **Could** trước. Nhưng khi tính toán năng lực thực tế cho 7 tuần, cắt riêng Could là **không đủ** — lý do và quyết định cụ thể:

### 4.1. Vì sao chỉ cắt Could là không đủ

Hai story Could (AI-02, AI-03) **đã không nằm trong lịch 8 tuần cũ từ trước** (đã ở backlog mở rộng ngay từ bản 1.0). Vì vậy, cắt chúng lần này **không giải phóng thêm điểm/tuần nào** — không giải quyết được việc thiếu 1 tuần. Cái thực sự "chiếm chỗ" trong lịch cũ là 3 story **Should** từng nằm ở Tuần 7 cũ (PLT-09, PLT-10, AI-04, tổng 6 điểm). Để nhường trọn Tuần 7 cho việc hoàn tất Must cuối cùng + kiểm thử, 3 story Should này buộc phải chuyển sang backlog mở rộng — **vượt phạm vi yêu cầu ban đầu (chỉ nhắc tới Could)**, nêu rõ ở đây để bạn quyết định có đồng ý hay không.

### 4.2. Danh sách bị cắt/hoãn khỏi lịch 7 tuần

| Mã | User Story (rút gọn) | Ưu tiên | Điểm | Lý do cắt |
|---|---|---|---|---|
| PLT-09 | Cấu hình tham số hệ thống (thuế suất, ngưỡng tồn kho) | Should | 2 | Có thể dùng giá trị mặc định hard-code (VAT 10%, ngưỡng 10) cho bản demo; không ảnh hưởng đến việc hệ thống chạy đúng, chỉ mất tính linh hoạt cấu hình. |
| PLT-10 | Chuẩn hóa thông báo lỗi tiếng Việt, ẩn stack trace | Should | 2 | Là cải thiện trải nghiệm/che dấu kỹ thuật, không phải điều kiện để nghiệp vụ chạy đúng; có thể làm dần trong lúc viết test ở Tuần 7 nếu còn dư giờ, không cần cam kết. |
| AI-04 | Xử lý lỗi khi AI không khả dụng | Should | 2 | Do AI-01 (chat AI) cũng đã bị hoãn (xem dưới), story xử lý lỗi AI không còn ý nghĩa để demo độc lập; nếu làm AI-01 sau, phải làm lại AI-04 kèm theo. |
| SUP-03 | Lịch sử đơn nhập theo nhà cung cấp | Should | 2 | Thông tin có thể tra bằng cách lọc danh sách đơn nhập theo NCC thủ công (PO-04, cũng đang hoãn) — mất tiện lợi, không mất dữ liệu. |
| CUS-03 | Lịch sử mua hàng & tổng giá trị khách hàng | Should | 2 | Tương tự SUP-03 — dữ liệu vẫn có trong đơn bán, chỉ thiếu màn hình tổng hợp riêng. |
| PO-04 | Danh sách đơn nhập + bộ lọc thời gian/NCC/trạng thái | Should | 2 | Danh sách đơn nhập cơ bản (không lọc) vẫn đủ dùng cho demo quy mô nhỏ; bộ lọc là tiện ích, không phải điều kiện nghiệp vụ. |
| SO-10 | Xuất phiếu bán hàng PDF | Should | 3 | Có thể demo bằng cách hiển thị chi tiết đơn trên màn hình thay vì xuất PDF; không ảnh hưởng đến tính đúng đắn của việc tính tiền/trừ kho. |
| RPT-02 | Top 10 sản phẩm bán chạy | Should | 2 | Là phân tích bổ sung trên cùng dữ liệu đã có ở RPT-01; có RPT-01 là đủ để chứng minh module báo cáo hoạt động. |
| RPT-03 | Xuất báo cáo Excel | Should | 2 | Tương tự SO-10 — có thể demo bằng số liệu hiển thị trên màn hình, không cần file xuất. |
| RPT-04 | Biểu đồ doanh thu 30 ngày dashboard | Should | 3 | Là hình thức trực quan hóa lại dữ liệu RPT-01; giá trị minh họa cao nhưng không phải điều kiện chấp nhận cốt lõi. |
| AI-01 | Chat tra cứu sản phẩm bằng ngôn ngữ tự nhiên | Should | 5 | Story Should nặng nhất (5đ) và phụ thuộc dịch vụ AI bên thứ ba (rủi ro tích hợp cao); NFR-08 đã yêu cầu rõ nghiệp vụ lõi không được phụ thuộc AI, nên hoãn AI không ảnh hưởng đến tiêu chí chấp nhận các module còn lại. |
| AI-02 | AI gợi ý tối đa 5 sản phẩm liên quan | Could | 3 | Đúng như đề xuất ban đầu — giá trị tăng thêm (upsell), không có trong tiêu chí chấp nhận bắt buộc nào của SRS. |
| AI-03 | AI tóm tắt báo cáo doanh thu bằng ngôn ngữ tự nhiên | Could | 3 | Đúng như đề xuất ban đầu — tiện ích, dữ liệu vẫn tra cứu được qua RPT-01 theo cách thủ công. |

**Tổng bị hoãn:** 27 điểm Should + 6 điểm Could = **33 điểm / 13 story**, không nằm trong cam kết 7 tuần chính thức.

### 4.3. Thứ tự ưu tiên nếu còn dư thời gian (sau Tuần 7 hoặc trước buổi bảo vệ)

1. AI-04 + AI-01 hoặc chỉ AI-04 (nếu muốn có ít nhất 1 điểm nhấn AI cho bảo vệ mà không rủi ro tích hợp)
2. SO-10 (xuất PDF — thường được hỏi trong demo)
3. RPT-04, RPT-02, RPT-03 (trực quan hóa báo cáo)
4. PLT-09, PLT-10 (cấu hình + UX lỗi)
5. PO-04, SUP-03, CUS-03 (tiện ích tra cứu/lọc)
6. AI-02, AI-03 (Could, thấp nhất)

---

*Tài liệu này bổ sung cho `docs/BACKLOG.md`, tập trung vào việc phân bổ thời gian; mọi thay đổi phạm vi cần cập nhật đồng thời cả hai tài liệu.*
