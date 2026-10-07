# Triển khai lên VPS bằng Docker Compose

Hướng dẫn đưa hệ thống **SalesInventory** (API + Blazor + SQL Server) lên một VPS Ubuntu. Các lệnh đã được chạy thử trên máy phát triển
bằng chính `docker-compose.yml` này (khởi động từ database trống, migrate, đăng nhập, tạo/đọc dữ liệu, tự phục hồi khi SQL Server chậm).
Những chỗ **chưa chạy thử trên VPS thật** được ghi rõ.

```
 Internet ──► :5081  Blazor (giao diện)  ──┐  mạng nội bộ Docker
          ──► :5080  API (REST)  ◄─────────┘  http://api:8080
                         │
                         ▼
                    sqlserver:1433  (chỉ nghe trên 127.0.0.1 của VPS, KHÔNG ra Internet)
```

## 1. Yêu cầu

| Thứ | Yêu cầu | Ghi chú |
|---|---|---|
| Hệ điều hành | Ubuntu 22.04 trở lên | |
| Docker | Docker Engine + Compose v2 (`docker compose`) | kiểm tra: `docker --version && docker compose version` |
| RAM | **tối thiểu 2 GB** (nên 4 GB) | SQL Server đòi ≥ 2000 MB. Máy "2 GB" hay hiện `1.9Gi`: có thể không đủ, xem mục 8 |
| Swap | 2 GB (nếu RAM ≤ 2 GB) | tránh bị kill khi build |
| Git | để lấy mã nguồn | `sudo apt-get install -y git` |

Thêm swap 2 GB (một lần):
```bash
sudo fallocate -l 2G /swapfile && sudo chmod 600 /swapfile && sudo mkswap /swapfile && sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
```

## 2. Biến môi trường

Đặt trong file `.env` cạnh `docker-compose.yml`. Mẫu: `.env.example`. **Không bao giờ commit `.env`** (chứa mật khẩu `sa`, khóa ký JWT, khóa API).

| Biến | Bắt buộc | Bí mật | Ý nghĩa |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | **Có** (đặt `Production`) | không | Nếu quên, compose chạy `Development` (bật Swagger, lỗi chi tiết, CORS mở). |
| `MSSQL_SA_PASSWORD` | **Có** | **có** | Mật khẩu `sa` của SQL Server (≥ 8 ký tự, có hoa, thường, số, ký hiệu). Tránh `;` `$` `'` `"` và khoảng trắng vì nó nằm trong chuỗi kết nối. |
| `JWT__KEY` | **Có** | **có** | Khóa ký token đăng nhập, ≥ 32 ký tự ngẫu nhiên. Lộ khóa này = giả mạo được token của bất kỳ ai, kể cả Admin. |
| `SEEDADMIN__EMAIL`, `SEEDADMIN__PASSWORD` | nên có | mật khẩu: **có** | Tài khoản Admin đầu tiên, tạo khi API khởi động nếu cả hai có giá trị. Bỏ trống = không tạo. |
| `ANTHROPIC_API_KEY` | không | **có** | Khóa trợ lý AI. Thiếu thì trợ lý báo lỗi, phần còn lại vẫn chạy. |
| `VOYAGE_API_KEY` | không | **có** | Khóa embeddings cho tra cứu tài liệu (RAG). |
| `API_PORT`, `WEB_PORT` | không | không | Cổng trên VPS cho API và Blazor (mặc định `5080`, `5081`). |
| `CORS__ALLOWEDORIGINS__0` | không | không | Origin https chính xác của một web app khác gọi thẳng API từ trình duyệt. Blazor Server gọi từ máy chủ nên không cần. |
| `HARDENING__TRUSTEDPROXIES__0` | khi có reverse proxy | không | IP/CIDR của proxy kết thúc TLS (ví dụ `172.18.0.0/16`). |

Compose **tự tạo** các biến sau, bạn không đặt trong `.env`:
- `ConnectionStrings__DefaultConnection` = chuỗi kết nối tới host `sqlserver`, ghép từ `MSSQL_SA_PASSWORD`.
- `Database__MigrateOnStartup=true` (API tự áp migration khi khởi động).
- `ApiBaseUrl=http://api:8080` cho Blazor (địa chỉ API **bên trong** mạng Docker).

Dấu `__` (hai gạch dưới) trong tên biến là cách ASP.NET Core ánh xạ biến môi trường phẳng vào cấu hình lồng nhau
(`Jwt__Key` = `Jwt:Key`). Đó là đường đưa secret vào ứng dụng mà không cần sửa file cấu hình.
Bảng đầy đủ hơn (kể cả Azure) nằm trong `CLAUDE.md`.

## 3. Lấy mã nguồn
```bash
git clone -b feat/stock-ledger-and-race-safety https://github.com/vudinhmanh23/Vudinhmanh.git sales-inventory
cd sales-inventory
ls          # phải có docker-compose.yml và .env.example, KHÔNG có .env
```

## 4. Tạo `.env` ngay trên server
Script sinh mật khẩu ngẫu nhiên tại chỗ, không phải chép secret qua lại. **Sửa email** trước khi dán.
```bash
SA_PW="Sa$(openssl rand -hex 12)Aa1!"
JWT="$(openssl rand -base64 48 | tr -d '\n=+/')"
ADMIN_PW="Ad$(openssl rand -hex 8)Bb2!"

cat > .env <<EOF
ASPNETCORE_ENVIRONMENT=Production
MSSQL_SA_PASSWORD=$SA_PW
JWT__KEY=$JWT
SEEDADMIN__EMAIL=admin@tencuaban.com
SEEDADMIN__PASSWORD=$ADMIN_PW
API_PORT=5080
WEB_PORT=5081
EOF
chmod 600 .env
echo "MAT KHAU ADMIN (luu lai ngay): $ADMIN_PW"
```
Cần trợ lý AI thì thêm `ANTHROPIC_API_KEY=...` (và `VOYAGE_API_KEY=...`) vào `.env`.

## 5. Chạy
```bash
docker compose up -d --build
docker compose ps                # sqlserver: healthy; api, blazor: Up
docker compose logs api | tail   # có dòng "Hosting environment: Production"
```
Lần build đầu mất vài phút. Compose có `restart: unless-stopped`, nên cả ba dịch vụ tự chạy lại sau khi VPS khởi động lại.

## 6. Migrate cơ sở dữ liệu

### Cách 1 (mặc định): tự động
Không cần làm gì. Compose đặt `Database__MigrateOnStartup=true`: khi API khởi động, nó áp các migration (tạo database và bảng nếu chưa có), rồi mới seed role và Admin. An toàn khi chạy lại (idempotent): đã thử khởi động API trên database đã migrate.

Kiểm tra bảng đã có:
```bash
docker compose exec sqlserver bash -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d SalesInventoryDb -Q "SELECT name FROM sys.tables ORDER BY name"'
```
Phải thấy `Products`, `Categories`, `SalesOrders`, `PurchaseOrders`, `Customers`, `Suppliers`, `StockMovements`... (20 bảng, 5 danh mục seed).

### Cách 2: chạy `dotnet ef` từ máy của bạn (kiểm soát hơn)
SQL Server chỉ nghe trên `127.0.0.1` của VPS, nên mở một **SSH tunnel** từ máy bạn:
```bash
# Cửa sổ 1 (giữ mở): cổng 14330 trên máy bạn -> SQL Server trên VPS
ssh -N -L 14330:127.0.0.1:1433 <user>@<IP-VPS>
```
```bash
# Cửa sổ 2, ở thư mục gốc repo trên máy bạn
dotnet ef database update \
  --project src/SalesInventory.Infrastructure --startup-project src/SalesInventory.Api \
  --connection "Server=127.0.0.1,14330;Database=SalesInventoryDb;User Id=sa;Password=<MSSQL_SA_PASSWORD>;TrustServerCertificate=True;"
```
- **Dùng `127.0.0.1`, không dùng `localhost`.** Trên Windows `localhost` phân giải sang IPv6 trước và kết nối bị treo (đã gặp khi thử).
- Lệnh `dotnet ef` này đã chạy thử với SQL Server trong container; phần SSH tunnel chưa chạy thử trên VPS thật.
- Nếu dùng cách này, muốn API không tự migrate nữa thì sửa `Database__MigrateOnStartup` trong `docker-compose.yml`.

## 7. Mở cổng và kiểm tra

| Cổng | Mở? | Vì sao |
|---|---|---|
| 22 (SSH) | Có (nên giới hạn theo IP bạn) | quản trị |
| 5080, 5081 | Có, **tạm thời** | để truy cập bằng IP; khi có tên miền + HTTPS thì đóng và chỉ mở 80/443 |
| **1433** | **Không** | compose đã gắn vào `127.0.0.1`; mở ra Internet là mời dò mật khẩu `sa` |

```bash
sudo ufw allow OpenSSH && sudo ufw allow 5080/tcp && sudo ufw allow 5081/tcp && sudo ufw enable
```
- **Docker bỏ qua ufw** cho cổng đã publish: cổng vẫn mở dù ufw chặn. Muốn khóa thật, gắn vào localhost trong compose (`"127.0.0.1:5080:8080"`) hoặc chặn ở firewall của nhà cung cấp VPS.
- Nhiều nhà cung cấp có **firewall riêng ngoài server**: phải thêm luật inbound TCP 5080/5081 ở đó.

Kiểm tra từ máy của bạn:
```bash
curl http://<IP-VPS>:5080/api/Health/db      # {"status":"ok","database":"connected"}
```
Mở `http://<IP-VPS>:5081`, đăng nhập bằng Admin ở bước 4. Swagger **không có** ở Production (404, cố ý).

> Qua `http://IP` mật khẩu và token đi dạng chữ rõ. Chỉ để thử. Triển khai thật cần tên miền + HTTPS (reverse proxy như Caddy), khi đó
> đặt `HARDENING__TRUSTEDPROXIES__0` và đóng 5080/5081.

## 8. Khi có sự cố

| Triệu chứng | Nguyên nhân thường gặp | Cách xử lý |
|---|---|---|
| `sqlserver` thoát, log có "at least 2000 megabytes" | RAM vật lý < 2000 MB | nâng VPS lên ≥ 4 GB, hoặc dùng Azure SQL cho database (swap **không** vượt được kiểm tra này) |
| Build báo `exit code 137` / `killed` | hết RAM khi build | thêm swap (mục 1) |
| `api` khởi động lại liên tục | thiếu `JWT__KEY`, sai mật khẩu `sa`, hoặc SQL Server chưa lên | `docker compose logs api \| tail -30`; thông báo nói rõ biến thiếu |
| `curl` từ máy bạn bị treo / timeout, nhưng `curl localhost:5080` trên server chạy | firewall (ufw hoặc của nhà cung cấp) chưa mở 5080/5081 | mục 7 |
| `compose` báo `Set JWT__KEY in .env` | thiếu biến bắt buộc | bổ sung vào `.env` |
| `sqlserver` unhealthy | `MSSQL_SA_PASSWORD` không đủ mạnh | đặt lại (đủ hoa, thường, số, ký hiệu), rồi `docker compose down -v` nếu database chưa có dữ liệu thật |
| Log SQL Server có `Login failed ... SalesInventoryDb` ngay lúc đầu | bình thường: API kiểm tra database trước khi migration tạo nó | bỏ qua nếu sau đó `Health/db` là `connected` |

Xem log: `docker compose logs -f api` (ảnh API chạy không có shell nên không dùng `docker exec ... sh`).

## 9. Cập nhật, sao lưu, gỡ
```bash
git pull && docker compose up -d --build     # bản mới; dữ liệu giữ nguyên (nằm trong volume sqlserver_data)
docker compose down                           # tắt, dữ liệu còn
docker compose down -v                        # XÓA CẢ DATABASE. Đừng chạy trên production
```
Sao lưu: chụp snapshot VPS, hoặc `BACKUP DATABASE` trong container rồi `docker cp` file `.bak` ra ngoài (lệnh sao lưu chưa chạy thử).
