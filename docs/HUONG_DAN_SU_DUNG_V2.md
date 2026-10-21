# TÀI LIỆU HƯỚNG DẪN SỬ DỤNG

## Hệ thống cân hợp nhất mía và vật tư — DigiScale

| Thông tin | Chi tiết |
|---|---|
| Phần mềm | Hệ thống cân hợp nhất mía và vật tư |
| Phiên bản | 1.0 |
| Ngày tạo | 21/10/2026 |

---

## MỤC LỤC

1. Quy trình tổng hợp
2. Đăng nhập hệ thống
3. Đăng tài mía nhập (FRM)
4. Đăng tài vật tư nhập (ERP - Type 01)
5. Đăng tài vật tư xuất (ERP - Type 06)
6. Cân hợp nhất — Cân lần 1 (cân vào)
7. Cân hợp nhất — Cân lần 2 (cân ra)
8. Đo tạp chất (mía)
9. Đo chữ đường CCS (mía)
10. In phiếu cân
11. Đẩy lại dữ liệu lên DigiScale
12. Nhập tay số cân (sự cố thiết bị)
13. Quản lý dữ liệu FRM
14. Quản lý dữ liệu ERP
15. Phụ lục — Mã lỗi thường gặp

---

## 1. QUY TRÌNH TỔNG HỢP

### 1.1 Luồng mía nhập (FRM)

```
Đăng tài → Cân lần 1 (xe + mía) → Đo tạp chất → Cân lần 2 (xe trống)
→ Đo CCS → Đẩy DigiScale → In phiếu cân
```

### 1.2 Luồng vật tư nhập (ERP - Type 01: Purchasing Order for Receiving)

```
Kéo dữ liệu ERP → Đăng tài → Cân lần 1 (xe + hàng) → Cân lần 2 (xe trống)
→ Đẩy oracle-tickets lên DigiScale → In phiếu cân
```

### 1.3 Luồng vật tư xuất (ERP - Type 06: Sales Order for Ship Confirm)

```
Kéo dữ liệu ERP → Đăng tài → Cân lần 1 (xe trống) → Xe vào kho lấy hàng
→ Cân lần 2 (xe + hàng) → Đẩy oracle-tickets lên DigiScale → In phiếu cân
```

**Lưu ý quan trọng:**
- Mía: đơn vị cân = **tấn**
- Vật tư: đơn vị cân = **kg**
- Mía đẩy DigiScale qua endpoint `new_phieucan` (phiếu cân FRM)
- Vật tư đẩy DigiScale qua endpoint `oracle-tickets` (phiếu cân ERP)

---

## 2. ĐĂNG NHẬP HỆ THỐNG

### Các bước thực hiện

- **Bước 1:** Mở ứng dụng "Hệ thống cân hợp nhất mía và vật tư"
- **Bước 2:** Nhập tên đăng nhập (username) và mật khẩu (password)
  - Tài khoản do admin DigiScale cấp
  - Mỗi tài khoản gắn với một BU (Business Unit / nhà máy)
- **Bước 3:** Bấm nút **Đăng nhập**
- **Bước 4:** Chờ hệ thống xác thực và tải dữ liệu FRM/ERP

**Lưu ý:**
- Nếu mất kết nối DigiScale, hệ thống cho phép đăng nhập offline với tài khoản admin cục bộ
- Ngôn ngữ hiển thị phụ thuộc vào BU của tài khoản

> 📷 *[Ảnh: Màn hình đăng nhập]*

---

## 3. ĐĂNG TÀI MÍA NHẬP (FRM)

### a. Điều kiện
- Xe có lệnh đốn (lệnh thu mua mía) hợp lệ trên FRM
- Xe có thẻ xe đã được gán trong hệ thống FRM

### b. Các bước thực hiện

- **Bước 1:** Chọn tab **"Mía nhập (FRM)"** trên dropdown Loại nghiệp vụ
- **Bước 2:** Chọn khung ngày lệnh (mặc định: ngày hiện tại)
- **Bước 3:** Nhập biển số xe vào ô "Tìm nhanh"
  - Hệ thống tự gợi ý từ danh sách xe FRM đã kéo
  - Chọn xe từ dropdown hoặc gõ đầy đủ biển số
- **Bước 4:** Hệ thống tự động điền thông tin từ FRM:
  - Lệnh đốn, chủ mía, giống mía, loại mía, trạm, vụ đầu tư, loại xe
- **Bước 5:** Kiểm tra thông tin → Bấm **Đăng tài (F4)**
- **Bước 6:** Hệ thống:
  - Lưu chuyến vào DB local
  - Đẩy phiếu đăng tài lên DigiScale
  - Hiện popup kết quả (thành công / thất bại)
  - Tạo mã QR cho chuyến
- **Bước 7:** Bấm **In phiếu (F5)** để in phiếu đăng tài (có barcode)

**Trường hợp không tìm thấy lệnh đốn:**
- Hệ thống hỏi: "Có muốn phát QR tạm?"
- Bấm "Có" → đăng tài tạm, QR tạm. Xe vẫn được vào nhà máy và cân.

> 📷 *[Ảnh: Giao diện đăng tài mía]*
> 📷 *[Ảnh: Popup đăng tài thành công]*
> 📷 *[Ảnh: Phiếu đăng tài in ra]*

---

## 4. ĐĂNG TÀI VẬT TƯ NHẬP (ERP - Type 01)

### a. Điều kiện
- Đã kéo dữ liệu ERP (PO - Purchasing Order) từ form "Quản lý dữ liệu ERP"
- Có số Shipment/PO hợp lệ

### b. Các bước thực hiện

- **Bước 1:** Chọn tab **"Vật tư (ERP)"** trên dropdown Loại nghiệp vụ
- **Bước 2:** Chọn hướng **"Nhập"**
- **Bước 3:** Nhập **Số TRIP** (= Số Shipment/PO từ ERP)
- **Bước 4:** Nhập **Biển số xe**
- **Bước 5:** Bấm nút **Kiểm tra**
  - Hệ thống tìm đơn hàng trong DB local (đã kéo từ ERP)
  - Nếu tìm thấy → tự động điền: Shipment Number, hàng hóa, KH/NCC, kho, ngày nhận
  - Nếu không tìm thấy → hỏi phát QR tạm
- **Bước 6:** Bấm **Đăng tài (F4)**
  - Lưu DB local (không đẩy DigiScale — vật tư đẩy khi cân)
  - Tạo mã QR
- **Bước 7:** Bấm **In phiếu (F5)** để in phiếu đăng tài

> 📷 *[Ảnh: Giao diện đăng tài vật tư nhập]*
> 📷 *[Ảnh: Kết quả kiểm tra ERP thành công]*

---

## 5. ĐĂNG TÀI VẬT TƯ XUẤT (ERP - Type 06)

### a. Điều kiện
- Đã kéo dữ liệu ERP (SO - Sales Order) từ form "Quản lý dữ liệu ERP"
- Có số Shipment hợp lệ (ERP type 06 thường có biển số xe)

### b. Các bước thực hiện

- **Bước 1:** Chọn tab **"Vật tư (ERP)"** → Hướng **"Xuất"**
- **Bước 2:** Nhập **Số TRIP** (= Số Shipment) HOẶC **Biển số xe**
  - Nếu nhập biển số → hệ thống tìm ERP order theo biển số → tự điền shipment
  - Nếu nhập shipment → hệ thống tìm ERP order → tự điền biển số (nếu có)
- **Bước 3:** Bấm nút **Kiểm tra**
- **Bước 4:** Bấm **Đăng tài (F4)**
- **Bước 5:** Bấm **In phiếu (F5)**

**Khác biệt xuất vs nhập:**
- Xuất: xe vào trống → lấy hàng → cân ra có hàng
- Nhập: xe vào có hàng → dỡ hàng → cân ra trống

> 📷 *[Ảnh: Giao diện đăng tài vật tư xuất]*

---

## 6. CÂN HỢP NHẤT — CÂN LẦN 1 (CÂN VÀO)

### a. Điều kiện
- Xe đã đăng tài (có QR hoặc mã đăng tài)
- Xe đã lên bàn cân

### b. Các bước thực hiện

- **Bước 1:** Mở tab **"Cân hợp nhất"** từ menu Vận hành
- **Bước 2:** Quét mã QR trên phiếu đăng tài vào ô barcode (hoặc nhập mã đăng tài)
  - Hệ thống load thông tin chuyến: biển số, KH/NCC, hàng hóa, shipment
  - Hiện trạng thái: **CÂN VÀO** (label xanh)
  - Đơn vị hiển thị tự động: mía = tấn, vật tư = kg
- **Bước 3:** Chờ cân ổn định (checklist ✅ xanh):
  - ✅ QR hợp lệ
  - ✅ Vị trí AI (nếu có camera)
  - ✅ Cân ổn định
  - ✅ Điều kiện trước đã đủ
- **Bước 4:** Bấm **Xác nhận (F5)** để chốt cân lần 1
- **Bước 5:** Hệ thống:
  - Lưu WeighEvent (Gross) vào DB
  - Mía: đẩy phiếu cân lần 1 lên DigiScale
  - Vật tư: chỉ lưu local (đẩy khi cân lần 2)
  - Hiện popup kết quả

**Nhập tay (sự cố thiết bị):**
- Bấm nút **Nhập tay** → Nhập số cân → Chọn vai trò (Gross/Tare) → Xác nhận

> 📷 *[Ảnh: Giao diện cân lần 1]*
> 📷 *[Ảnh: Checklist điều kiện chốt cân]*

---

## 7. CÂN HỢP NHẤT — CÂN LẦN 2 (CÂN RA)

### a. Điều kiện
- Đã cân lần 1
- Mía: đã đo tạp chất (bắt buộc)
- Xe đã lên bàn cân

### b. Các bước thực hiện

- **Bước 1:** Quét lại mã QR (hoặc nhập mã đăng tài)
  - Hệ thống tự nhận biết: đã cân lần 1 → chuyển sang **CÂN RA** (label đỏ)
  - Load dữ liệu cân lần 1 vào ô "TL cân vào"
- **Bước 2:** Chờ cân ổn định
- **Bước 3:** Bấm **Xác nhận (F5)**
- **Bước 4:** Hệ thống:
  - Lưu WeighEvent (Tare) vào DB
  - Tính TL hàng = |Cân vào - Cân ra|
  - **Mía:** Đẩy phiếu cân lần 2 lên DigiScale (kèm lấy chính sách thu mua)
  - **Vật tư:** Đẩy oracle-tickets lên DigiScale (kèm WeightIn, WeightOut, NetWeight, biển số, tài xế, NCC)
  - Hiện popup kết quả + chính sách (nếu có)
- **Bước 5:** Bấm **In phiếu (F6)** để in phiếu cân

**Cột "Đồng bộ" trên grid:**
- "Chưa đẩy" = chưa đẩy lên DigiScale
- "✅ Đã đẩy" = đã đẩy thành công

> 📷 *[Ảnh: Giao diện cân lần 2]*
> 📷 *[Ảnh: Popup cân lần 2 thành công]*

---

## 8. ĐO TẠP CHẤT (MÍA)

### a. Điều kiện
- Xe đã cân lần 1 (có mẫu mía)
- Bắt buộc đo trước khi cân lần 2

### b. Các bước thực hiện

- **Bước 1:** Mở tab **"Đo tạp chất"** từ menu Vận hành
- **Bước 2:** Quét mã QR hoặc nhập mã đăng tài
- **Bước 3:** Hệ thống load thông tin chuyến
- **Bước 4:** Đặt mẫu trước lên cân mẫu → Chờ ổn định → Tự động ghi
- **Bước 5:** Lấy mẫu ra, đặt mẫu sau lên → Chờ ổn định → Tự động ghi
- **Bước 6:** Hệ thống tính: Tạp chất (%) = (Trước - Sau) / Trước × 100
- **Bước 7:** Bấm **Ghi kết quả** → Đẩy phiếu tạp chất lên DigiScale
- **Bước 8:** Bấm **In phiếu** để in phiếu tạp chất

> 📷 *[Ảnh: Giao diện đo tạp chất]*

---

## 9. ĐO CHỮ ĐƯỜNG CCS (MÍA)

### a. Điều kiện
- Xe đã cân (có mẫu mía)

### b. Các bước thực hiện

- **Bước 1:** Mở tab **"Đo chữ đường (CCS)"** từ menu Vận hành
- **Bước 2:** Quét mã QR hoặc chọn xe từ dropdown (lọc theo khung ngày)
- **Bước 3:** Nhập kết quả phân tích:
  - Brix, Pol, Pol cải chính, Fibre (xơ %)
  - CCS tự tính theo công thức
- **Bước 4:** Bấm **Ghi nhận CCS**
- **Bước 5:** Đẩy phiếu CCS lên DigiScale
- **Bước 6:** Bấm **In phiếu** để in

**Lịch sử CCS (bên phải):**
- Hiện tất cả xe đã đo CCS trong khung ngày
- Bấm vào dòng → load thông tin lên form → in lại phiếu

> 📷 *[Ảnh: Giao diện đo CCS]*

---

## 10. IN PHIẾU CÂN

### Từ màn hình Cân hợp nhất:

- **Bước 1:** Chọn xe đã cân đủ trên lưới "Đã cân đủ" (bấm vào dòng)
- **Bước 2:** Bấm **In phiếu (F6)**
- **Bước 3:** Xem trước phiếu cân (preview GDI+)
- **Bước 4:** Bấm **In** để gửi ra máy in

**Phiếu cân bao gồm:**
- Thông tin công ty, địa chỉ
- Số đăng tài, biển số xe, chủ mía/KH
- Trọng lượng cân vào, cân ra, trọng lượng hàng
- Tạp chất, CCS (nếu có)
- Barcode
- 3 khung chữ ký

> 📷 *[Ảnh: Preview phiếu cân]*

---

## 11. ĐẨY LẠI DỮ LIỆU LÊN DIGISCALE

### Khi nào cần đẩy lại:
- Lần đẩy trước bị lỗi (mạng, timeout, DigiScale bảo trì)
- Cần cập nhật lại dữ liệu

### Các bước:

- **Bước 1:** Chọn xe trên lưới "Đã cân đủ" (bấm vào dòng)
- **Bước 2:** Bấm **🔄 Đẩy lại Digi**
- **Bước 3:** Hệ thống tự detect mía/vật tư:
  - **Mía:** Đẩy lại `new_phieucan` (phiếu cân FRM)
  - **Vật tư:** Đẩy lại `oracle-tickets` (phiếu cân ERP)
- **Bước 4:** Hiện popup kết quả

> 📷 *[Ảnh: Popup đẩy lại thành công]*

---

## 12. NHẬP TAY SỐ CÂN (SỰ CỐ THIẾT BỊ)

### Khi nào dùng:
- Thiết bị cân mất kết nối hoặc hỏng
- Cần nhập số cân thủ công từ phiếu đọc trực tiếp

### Các bước:

- **Bước 1:** Quét QR xe cần nhập tay
- **Bước 2:** Bấm **Nhập tay**
- **Bước 3:** Nhập thông tin:
  - Số cân (kg hoặc tấn tùy loại)
  - Đơn vị (tự chọn: mía = tấn, vật tư = kg)
  - Vai trò cân (Gross = cân vào, Tare = cân ra) — tự detect theo lần cân
  - Lý do (tùy chọn)
  - Mã phê duyệt (tùy chọn)
- **Bước 4:** Bấm **Xác nhận nhập tay**
- **Bước 5:** Hệ thống ghi WeighEvent nguồn "Manual" vào DB

> 📷 *[Ảnh: Dialog nhập tay]*

---

## 13. QUẢN LÝ DỮ LIỆU FRM

### Mở từ: Menu Kỹ thuật → Quản lý dữ liệu FRM

### Chức năng:
- Kéo dữ liệu lệnh đốn (mía) từ DigiScale/FRM API
- Hiển thị danh sách lệnh đốn đã kéo
- Lọc theo khung ngày, tìm kiếm nhanh
- Tự động kéo theo chu kỳ (bật/tắt)

### Các bước:

- **Bước 1:** Kiểm tra BU (readonly — theo tài khoản đăng nhập)
- **Bước 2:** Bấm **🔄 Kéo dữ liệu FRM mới nhất**
- **Bước 3:** Chờ hệ thống kéo → hiện kết quả trong log

> 📷 *[Ảnh: Giao diện quản lý FRM]*

---

## 14. QUẢN LÝ DỮ LIỆU ERP

### Mở từ: Menu Kỹ thuật → Quản lý dữ liệu ERP

### Chức năng:
- Kéo đơn hàng ERP (Oracle) từ DigiScale API
- Hiển thị: Shipment, Trip, biển số, loại GD, hàng hóa, NCC/KH, số lượng
- Lọc theo loại GD (01-06), khung ngày, BU
- Column filter popup (bấm header cột)

### Các bước:

- **Bước 1:** Kiểm tra BU (readonly)
- **Bước 2:** Chọn khung ngày
- **Bước 3:** Chọn Loại GD (mặc định: Tất cả)
  - 01 - Purchasing Order for Receiving (Mua hàng)
  - 02 - InterOrg Intransit Shipped (Chuyển kho xuất)
  - 03 - InterOrg Intransit for Receiving (Chuyển kho nhận)
  - 04 - Transfer Order for Ship Confirm (Xuất kho)
  - 05 - Transfer Order for Receiving (Nhận kho)
  - 06 - Sales Order for Ship Confirm (Bán hàng)
- **Bước 4:** Bấm **🔄 Kéo dữ liệu ERP mới nhất**
- **Bước 5:** Kiểm tra dữ liệu trên grid

> 📷 *[Ảnh: Giao diện quản lý ERP]*

---

## 15. PHỤ LỤC — MÃ LỖI THƯỜNG GẶP

| Lỗi | Nguyên nhân | Cách xử lý |
|---|---|---|
| "Không tìm thấy đơn hàng trên ERP" | Chưa kéo dữ liệu ERP hoặc sai số TRIP | Mở form ERP → Kéo dữ liệu → Thử lại |
| "QR tạm không đủ điều kiện" | Xe chưa có lệnh đốn/shipment chính thức | Liên hệ bộ phận kế hoạch để tạo lệnh |
| "DigiScale HTTP 422" | Dữ liệu không hợp lệ (thiếu BU, shipment...) | Kiểm tra log, đẩy lại |
| "Mất tín hiệu cân" | Thiết bị cân mất kết nối | Kiểm tra cáp, restart thiết bị |
| "Cân ổn định chưa đủ" | Xe rung, chưa dừng hẳn | Chờ xe dừng hẳn trên bàn cân |
| "Chưa đo tạp chất" | Mía phải đo tạp chất trước khi cân lần 2 | Đo tạp chất trước |

---

## PHÍM TẮT

| Phím | Chức năng |
|---|---|
| **F4** | Đăng tài |
| **F5** | Xác nhận cân / In phiếu đăng tài |
| **F6** | In phiếu cân |
| **Esc** | Lượt mới / Hủy |

---

*Tài liệu này là khung hướng dẫn. Hình ảnh minh họa sẽ được bổ sung sau.*
