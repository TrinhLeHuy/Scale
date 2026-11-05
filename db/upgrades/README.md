# Script nâng cấp CSDL cho bản cài đặt

Bộ cài (`installer\scripts\configure.ps1`) chạy các tệp `*.sql` trong thư mục này khi **cài đè** lên máy
đã có dữ liệu:

- Chạy theo thứ tự tên tệp, mỗi tệp đúng một lần. Đã chạy thì ghi vào bảng `installer_schema_upgrades`.
- Trước khi chạy, tự sao lưu CSDL vào `<thư mục cài>\data\backups\before-upgrade-*.dump` (pg_dump -Fc).
- Mỗi tệp chạy trong một transaction. Lỗi thì CSDL giữ nguyên và bộ cài báo lỗi.
- Máy cài mới nạp `db/schema.sql` (đã là bản mới nhất), nên các tệp ở đây chỉ được đánh dấu là đã chạy.

Khi đổi schema:

1. Thêm tệp mới, đặt tên tăng dần: `20261105_01_add_trip_sync_status.sql`. Tên chỉ gồm chữ, số, `_ . -`.
2. Không sửa / đổi tên tệp đã phát hành.
3. Cập nhật `db/schema.sql` để máy cài mới có cùng kết quả (xem `docs/SETUP_MAY_MOI.md`).
4. Viết script không làm mất dữ liệu (`ADD COLUMN ... NULL`, `CREATE TABLE IF NOT EXISTS`...).
   Kèm cách quay lui trong chú thích đầu tệp.

Khôi phục bản sao lưu: `pg_restore -h localhost -p <port> -U ttc_app -d ttc_weighing --clean <tệp .dump>`
(port + mật khẩu ở `data\db-secrets.json`).
