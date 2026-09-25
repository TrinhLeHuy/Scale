# Hướng dẫn sử dụng — AGRIS Hệ thống cân hợp nhất mía và vật tư

## 1. Khởi động

### Chạy nhanh — dữ liệu giả, không cần database

Chế độ xem trước (Preview) dùng dữ liệu giả trong bộ nhớ, không cần cài PostgreSQL.

**Bước 1:** Đảm bảo file `src/TTC.Weighing.WinForms/appsettings.Development.json` có:
```json
{
  "Preview": {
    "Enabled": true
  }
}
```

**Bước 2:** Build và chạy:
```powershell
dotnet build
dotnet run --project src/TTC.Weighing.WinForms
```

### Chạy trên database thật (PostgreSQL)

1. Cài PostgreSQL, tạo database `ttc_weighing`
2. Đổi `Preview:Enabled` thành `false` trong `appsettings.Development.json`
3. Cập nhật connection string trong `appsettings.Development.json`
4. Chạy migration:
```powershell
dotnet ef database update --project src/TTC.Weighing.Infrastructure --startup-project src/TTC.Weighing.WinForms
```
5. Chạy kịch bản dữ liệu mẫu:
```powershell
dotnet run --project tools/TTC.Weighing.Scenario
```
6. Khởi động ứng dụng:
```powershell
dotnet run --project src/TTC.Weighing.WinForms
```

**Lỗi thường gặp:**
- "transient failure" → PostgreSQL chưa khởi động hoặc connection string sai. Dùng chế độ Preview nếu chỉ xem giao diện.
## 2. Bố cục chung

- **Dải thương hiệu AGRIS** phía trên: tên hệ thống, nhà máy, tên người dùng
- **Menu chính**: Vận hành, Quản lý, Kỹ thuật, Tab
- **Vùng làm việc**: mỗi màn hình mở thành một tab
- **Thanh trạng thái thiết bị** (trên form cân): hiện trạng thái kết nối barcode, đồng hồ cân 1/2, camera LPR 1/2, camera toàn cảnh — cập nhật mỗi 2 giây
- **Chân trang**: chế độ chạy, các điểm D chưa duyệt

### Phím tắt chung

| Phím | Hành động |
|---|---|
| Ctrl+W | Đóng tab hiện tại |
| Ctrl+Tab / Ctrl+Shift+Tab | Chuyển tab |
| Chuột giữa lên tab | Đóng tab |

---

## 3. Luồng vận hành chuyến nhập mía

### Bước 1 — Đăng tài (UI-02)

1. Chọn **Loại nghiệp vụ**: "Mía nhập (FRM)" hoặc "Vật tư (ERP)"
   - Mía nhập: hiện các trường Loại mía, Giống mía, Chủ mía, Hình thức thu hoạch, Trạm, Vụ mùa
   - Vật tư: hiện các trường Mã hàng / Hàng hóa, Kho
2. Nhập **Biển số**, bấm **Kiểm tra** để tra kế hoạch FRM/ERP
3. Nếu có kế hoạch: hệ thống tự điền thông tin từ FRM
4. Bấm **Lưu phiếu (F4)** → hệ thống cấp số đăng tài và phát QR
5. Bấm **In phiếu (F5)** để in phiếu đăng tài có mã QR cho tài xế

| Phím | Hành động |
|---|---|
| F4 | Lưu phiếu đăng tài |
| F5 | In phiếu |
| F6 | Xác nhận |
| Esc | Làm mới form |

**Lưu ý:**
- Thiếu chứng từ nguồn → chỉ phát QR tạm → QR tạm không đủ điều kiện cân chính thức
- Lưới phía dưới hiển thị chuyến đã đăng tài với 3 tab: Trong nhà máy / Đã ra / Đã hủy

### Bước 2 — Vào cổng (UI-11 Cổng Đăng Tài)

1. **Quét QR** phiếu đăng tài tại ô "Quét QR" (hoặc nhập mã, nhấn Enter)
2. Hệ thống tự tra cứu chuyến, hiển thị thông tin + **timeline mốc thời gian**
3. Chọn hướng **Nhập** (vào cổng)
4. Bấm **Xác nhận (F6)** → hệ thống kiểm tra điều kiện → mở barie 30 giây
5. Xe qua cổng → hệ thống tự ghi nhận

**Kiểm tra trước khi mở barie:**
- Trip đã Reconciled/Posted/Closed → cho ra cổng
- Chưa đủ checkpoint → từ chối, hiện danh sách bước thiếu
- Có lệnh barie treo (Unknown/Requested) → từ chối

### Bước 3 — Cân lần 1 (UI-03 Cân hợp nhất)

1. **Quét QR** phiếu đăng tài tại ô barcode trên form cân
2. Hệ thống tự:
   - Giải mã QR, tra cứu chuyến
   - Hiển thị thông tin chuyến (biển số, khách hàng, hàng hóa, kho)
   - Chọn đúng bàn cân từ thông tin trong QR
   - Mở phiên cân, bắt đầu hiển thị số cân realtime
3. **Số cân realtime** cập nhật mỗi 250ms:
   - Vàng = đang đo, chưa ổn định
   - Xanh = ổn định, hiện đếm ngược "Ổn định 3.2s / 5.0s"
   - Mất tín hiệu → hiện "---" + thời gian mất
4. **Checklist điều kiện** cập nhật mỗi 800ms: QR hợp lệ ✓/✗, Vị trí AI ✓/✗, Cân ổn định ✓/✗, Điều kiện trước đã đủ ✓/✗
5. Khi đủ điều kiện → bấm **Lưu phiếu (F5)** → chốt cân
6. Hệ thống hướng dẫn tài xế đi đo tạp chất

**Xử lý QR lỗi:**
- QR hết hạn/thu hồi → hiện mã lỗi + lý do, không mở phiên
- QR tạm → "Xe chưa đăng ký kế hoạch, cần quay lại cổng để lấy QR chuyến"

| Phím | Hành động |
|---|---|
| F5 | Lưu phiếu (chốt cân) |
| F6 | In phiếu cân |
| Esc | Lượt mới |

### Bước 4 — Đo tạp chất (UI-05 Mía và chất lượng)

1. **Quét QR** tại trạm lấy mẫu → hiện thông tin chuyến mía (biển số, chủ mía, loại mía, giống mía)
2. **Cân mẫu tự động 2 bước:**
   - Bước 1: Đặt mẫu trước lên cân mẫu → chờ ổn định 3-5 giây → tự động ghi SampleBeforeKg
   - Lấy mẫu ra (cân giảm về gần 0)
   - Bước 2: Đặt mẫu sau (đã loại tạp chất) lên cân → chờ ổn định → tự động ghi SampleAfterKg
3. Hệ thống tự tính tạp chất: `(trước - sau) / trước × 100`
4. Bấm **Ghi kết quả tạp chất**

**Cảnh báo và lỗi:**
- Mẫu dưới ngưỡng → cảnh báo vàng (cho phép ghi)
- Mẫu sau > mẫu trước → từ chối, cần lấy lại mẫu

**Nhập tay tạp chất:** Nút "Nhập tay" chỉ hiện khi có quyền `quality.impurity.manual`. Bắt buộc nhập lý do + mã phê duyệt.

### Bước 5 — Chữ đường (CCS)

Ở cùng màn hình UI-05, phần "Chữ đường":
- Nhập giá trị hoặc tick "Nguồn báo đang chờ" (Pending)
- Bấm **Tiếp nhận chữ đường**

**Quan trọng:**
- Thiếu chữ đường chỉ là cảnh báo vàng trên form cân, vẫn cân lần 2 được
- Kết quả có thể đến sau khi xe đã ra cổng

### Bước 6 — Cân lần 2 (UI-03)

1. **Quét QR** lại tại bàn cân → hệ thống tự nhận diện đây là lần 2
2. Tự động:
   - Gọi FRM đồng bộ thông tin chuyến (loại mía, giống, chủ mía mới nhất)
   - Kiểm tra điều kiện: đã đo tạp chất? có chữ đường chưa?
3. Nếu chưa đo tạp chất → **chặn cân**, hiện "Phải đo tạp chất trước"
4. Nếu chưa có CCS → hiện cảnh báo vàng, cho phép tiếp tục
5. Chờ ổn định → bấm **Lưu phiếu (F5)** → chốt cân lần 2
6. Sau chốt cân thành công:
   - Hiển thị trọng lượng hàng (TL hàng = chênh lệch Gross - Tare)
   - Bật nút **In phiếu (F6)** và **Sync ERP**

### Bước 7 — In phiếu cân (F6 trên UI-03)

1. Bấm **In phiếu (F6)** → hiện dialog chọn máy in, số bản, mẫu phiếu (SUGARCANE hoặc MATERIAL)
2. Bấm **In** → in phiếu chứa: số phiếu, biển số, cân vào/ra, trọng lượng hàng, tạp chất
3. In lại → phiếu đánh dấu "BẢN IN LẠI" + ghi audit

**Lỗi in không ảnh hưởng dữ liệu cân đã lưu.** Có thể in lại bất cứ lúc nào.

### Bước 8 — Đồng bộ ERP (nút Sync ERP trên UI-03)

1. Bấm **Sync ERP** → tạo thông điệp đồng bộ đến ERP/FRM
2. Trạng thái hiện: Held (hợp đồng API chưa xác minh) hoặc New (sẵn sàng gửi)

### Bước 9 — Ra cổng (UI-11)

1. **Quét QR** tại cổng ra
2. Hệ thống hiện timeline: Vào cổng → Cân 1 → Tạp chất → Cân 2 → Cổng ra
3. Nếu Trip đã Reconciled/Posted/Closed → "Xe đã hoàn thành cân. Xác nhận cho xe qua!"
4. Bấm **Xác nhận (F6)** → mở barie 30 giây → thu hồi QR → Trip chuyển sang Closed

---

## 4. Luồng vận hành vật tư (UI-04 Kho vận)

### Vào kho

1. **Quét QR** tại cổng kho → hiện thông tin chuyến (biển số, hàng hóa, kho)
2. Bấm **Vào kho (F4)** → ghi checkpoint WarehouseIn

### Phiên đo IoT (cho vật tư hàng bao)

1. Nhập mã hàng, chọn chế độ IoT (Delta/Cumulative), cơ sở khối lượng
2. Bấm **Mở phiên đo** → phiên khóa thiết bị
3. (Chế độ xem trước) Bấm các nút sinh dữ liệu để mô phỏng IoT
4. Bấm **Đóng phiên đo** → tổng hợp số liệu
5. Bấm **Chạy đối soát** → so sánh cân xe, IoT, đếm bao, quy cách

### Ra kho

1. Bấm **Hoàn thành ra kho (F5)** → ghi checkpoint WarehouseOut
2. Nếu chưa hoàn thành giao nhận → yêu cầu nhập lý do + mã phê duyệt

### Lưới xe trong kho

- Tab **Trong kho** / **Đã ra kho** + bộ lọc ngày
- Bấm **Lọc** để tải lại

| Phím | Hành động |
|---|---|
| F4 | Xác nhận vào kho |
| F5 | Hoàn thành ra kho |
| Esc | Làm mới form |

---

## 5. Xử lý ngoại lệ trên form cân (UI-03)

### Nhập tay số cân

Nút "Nhập tay" chỉ hiện khi có quyền `weighing.weight.manual`.
1. Bấm → dialog nhập: số cân, đơn vị (kg/tấn), vai trò (Gross/Tare), lý do bắt buộc, mã phê duyệt bắt buộc
2. Server từ chối nếu Trip đang Paused hoặc có giao dịch Unknown
3. Ghi audit log đầy đủ

### Cân lại sau sự cố

Nút "Cân lại" chỉ hiện khi có quyền `weighing.reweigh.request`.
1. Bấm → dialog chọn vai trò (Gross/Tare), nhập lý do bắt buộc, tài liệu sự cố
2. Nếu đã gửi ERP → cần tạo yêu cầu điều chỉnh (REWEIGH_AFTER_POST)
3. Nếu Trip đang Paused → yêu cầu Resume trước
4. Sau cân lại → cần chạy lại đối soát

---

## 6. Các màn hình quản lý

### UI-01 Tổng quan

- Luồng nghiệp vụ 7 chặng: Phiếu tạm → Đã đăng tài → Đang xử lý → Đủ lượt cân → Đã đối soát → Đã đồng bộ → Kết thúc
- 8 ô chỉ số: xe chờ cân, trong nhà máy, cân 1 chưa 2, PAUSED, chặn đối soát, tích hợp cần xử lý, tổng net, can thiệp tay
- Bấm đôi dòng chuyến → mở chi tiết (UI-06)

### UI-06 Chi tiết chuyến

- **Timeline** mốc thời gian ở đầu form (Vào cổng → Cân 1 → Tạp chất → Cân 2 → Kho → Cổng ra)
- 8 tab: Checkpoint, Các lần cân, Chất lượng, Phiên đo, Đối soát, Phân bổ, Tích hợp, Audit
- **Tab Phân bổ** (ghép xe): hiện chủ mía, trọng lượng phân bổ, tỷ lệ, phần dư làm tròn (RoundingAdjustmentKg), trạng thái duyệt
  - Nút **Phân bổ** → dialog chọn phương pháp (Theo tỷ lệ / Số lượng + phần dư), nhập dòng chủ mía, mã phê duyệt bắt buộc
- **Blocker** ở cuối: liệt kê vấn đề đang chặn chuyến

### UI-07 Hàng chờ ngoại lệ

Tổng hợp: yêu cầu cân lại chờ duyệt, sự cố đang mở, chênh lệch đối soát.

### UI-10 Báo cáo và phiếu

Lọc theo ngày, hồ sơ nghiệp vụ, biển số → Xuất CSV → Xem phiếu cân.

---

## 7. Màn hình kỹ thuật

### UI-08 Giám sát tích hợp

- Bảng thông điệp FRM/ERP: business key, trạng thái, số lần gửi
- **Giải quyết UNKNOWN**: chọn dòng → nhập căn cứ đối soát → kết luận

### UI-09 Thiết bị và cấu hình

- Hồ sơ nghiệp vụ: xem ngưỡng (cửa sổ ổn định, biên dao động, confidence AI)
- Bàn cân: mã, khả năng đo min/max
- Thiết bị: mã, loại, làn

---

## 8. Thanh trạng thái thiết bị (trên form cân UI-03)

Thanh phía dưới form cân hiển thị trạng thái kết nối realtime:
- **🟢 Xanh**: thiết bị trực tuyến
- **🔴 Đỏ**: mất kết nối — hiện thời gian mất tín hiệu
- **🟡 Vàng**: thiết bị lỗi (Fault, Overload, Underload)

Di chuột lên chỉ thị → hiện chi tiết: mô tả lỗi, thời gian tín hiệu cuối, SignalAge.

Thiết bị hiển thị: Đầu đọc barcode, Đồng hồ cân 1, Đồng hồ cân 2, Camera LPR 1, Camera LPR 2, Camera toàn cảnh.

---

## 9. Những điều quan trọng cần nhớ

1. **Số cân do server quyết định.** Form không tự tính toán điều kiện chốt cân.
2. **Quét QR để bắt đầu mọi thao tác.** Không còn chọn chuyến từ ComboBox — tất cả form đều quét QR.
3. **Net cân xe là chênh lệch**, không mặc định là khối lượng sản phẩm tinh.
4. **QR tạm không cân chính thức được.** Phải có chứng từ nguồn.
5. **Cân lại tạo bản mới**, bản cũ giữ nguyên để tra cứu.
6. **Nhập tay bắt buộc phê duyệt và lý do.** Không dùng để bỏ qua lỗi thiết bị.
7. **Thiếu tạp chất CHẶN cân lần 2. Thiếu chữ đường chỉ CẢNH BÁO.**
8. **Lỗi in phiếu không ảnh hưởng dữ liệu cân.** Có thể in lại bất cứ lúc nào.
9. **Chữ đường có thể đến sau khi xe đã ra cổng.**
10. **Lệnh mở barie có thời hạn 30 giây.** Hết hạn phải đánh giá lại.
11. **Mọi thao tác có audit.** Bao gồm cả lần bị chặn và từ chối.

---

## 10. Menu Xem trước (chỉ có ở chế độ Preview)

| Toggle | Hiệu ứng khi tắt |
|---|---|
| Đã đo tạp chất | Form cân chặn cân lần 2 |
| Đã xác thực danh tính xe | Form cân và cổng chặn |
| Chữ đường đang chờ | Form cân hiện cảnh báo vàng |
| Hợp đồng API đã xác minh | Thông điệp từ Held → New |
| Dung sai đã duyệt | Đối soát từ Pending → Pass/Mismatch |
| Bộ điều khiển barie | Cổng chặn mở barie |

---

## 11. Các điểm chờ quyết định (D)

| Điểm | Nội dung chờ | Hành vi hiện tại |
|---|---|---|
| D-02 | Hợp đồng API FRM/ERP | Thông điệp giữ HELD |
| D-03 | Cách xác thực danh tính xe | Xác nhận nhân viên tại làn |
| D-04 | Biên dao động, độ mới tín hiệu | Ngưỡng trống → chặn chốt cân |
| D-05 | Ngưỡng AI vị trí và đếm bao | Ngưỡng trống → chặn chốt cân |
| D-06 | Bảng dung sai đối soát | Đối soát kết luận Pending |
| D-08 | Vai trò, phân quyền | Dùng danh tính cục bộ |
| D-11 | Mẫu phiếu và số bản in | Phiếu đánh dấu chưa duyệt |

---

## 12. Chạy kiểm tra

```powershell
# Kiểm tra DI của toàn bộ form
dotnet run --project src/TTC.Weighing.WinForms -- --smoke

# Kiểm tra bố cục
dotnet run --project src/TTC.Weighing.WinForms -- --layout

# Kịch bản đầu cuối trên database thật
dotnet run --project tools/TTC.Weighing.Scenario

# Unit tests
dotnet test
```
