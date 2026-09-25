# Kịch bản Test — Chế độ Preview (dữ liệu giả)

## Chuẩn bị

1. Đảm bảo `appsettings.Development.json` có `"Preview": { "Enabled": true }`
2. Chạy: `dotnet run --project src/TTC.Weighing.WinForms`
3. App mở ra ở tab **UI-01 Tổng quan**

**Dữ liệu sẵn có:**
- 2 chuyến demo: `TTC-DEMO-202609-000001` (biển số 51C-123.45) và `TTC-DEMO-202609-000002` (biển số 51C-999.11)
- Profile: CANE_IN (Nhập mía) + MATERIAL_OUT_BAGGED (Xuất vật tư)
- 2 bàn cân, 6 thiết bị, cổng, kho
- QR token sẵn sàng quét

---

## Test 1: Tổng quan (UI-01)

**Mở:** Tab đầu tiên đã mở sẵn

| Bước | Hành động | Kết quả mong đợi |
|------|-----------|-------------------|
| 1.1 | Quan sát 8 ô chỉ số | Hiện số liệu: xe chờ cân, trong nhà máy, v.v. |
| 1.2 | Quan sát luồng nghiệp vụ 7 chặng | Phiếu tạm → Đã đăng tài → ... → Kết thúc |
| 1.3 | Quan sát lưới chuyến | 2 chuyến demo hiện trong lưới |
| 1.4 | Double-click dòng chuyến đầu tiên | Mở tab **UI-06 Chi tiết chuyến** |

---

## Test 2: Đăng tài mía nhập (UI-02)

**Mở:** Menu Vận hành → Đăng tài (hoặc bấm tab)

| Bước | Hành động | Kết quả mong đợi |
|------|-----------|-------------------|
| 2.1 | Kiểm tra ComboBox "Loại nghiệp vụ" | Có 2 lựa chọn: "Mía nhập (FRM)" và "Vật tư (ERP)" |
| 2.2 | Chọn "Mía nhập (FRM)" | Hiện trường: Loại mía, Giống mía, Chủ mía, Hình thức thu hoạch, Trạm, Vụ mùa |
| 2.3 | Chọn "Vật tư (ERP)" | Ẩn trường mía, hiện: Mã hàng, Kho |
| 2.4 | Chọn lại "Mía nhập (FRM)" | Trường mía hiện lại |
| 2.5 | Nhập Biển số: `54Y-7573` (có trong FRM) hoặc biển khác | — |
| 2.6 | Bấm "Kiểm tra" | Gọi FRM API thật (POST /api/v1/integration/frm entities=the-xe). Tìm thẻ xe theo biển số |
| 2.6a | (Biển số `54Y-7573` — có thẻ xe trong FRM) | Tự động điền: Mã thẻ (T210000832), Trạm (AGRIC-TN-TRAMCT), Hợp đồng vận chuyển |
| 2.6b | (Biển số KHÔNG có trong FRM) | Hỏi: "Phát QR tạm?" |
| 2.7 | Chọn "Phát QR tạm" khi được hỏi (nếu không có kế hoạch) | — |
| 2.8 | Chọn Loại xe: Container hoặc Truck | — |
| 2.9 | Nhập: Loại mía = Tươi, Giống = ROC22, Chủ mía = Test | — |
| 2.10 | Bấm **Lưu phiếu (F4)** | MessageBox hiện: "Đăng tài thành công" + Mã QR |
| 2.11 | Quan sát lưới phía dưới | Chuyến mới xuất hiện trong lưới "Trong nhà máy" |
| 2.12 | Bấm **Esc** | Form làm mới, sẵn sàng đăng tài tiếp |

---

## Test 3: Cân lần 1 (UI-03)

**Mở:** Menu Vận hành → Cân hợp nhất

| Bước | Hành động | Kết quả mong đợi |
|------|-----------|-------------------|
| 3.1 | Nhập bất kỳ ký tự vào ô QR, nhấn Enter | Hệ thống quét QR → hiện thông tin chuyến demo |
| 3.2 | Quan sát panel giữa | Số cân realtime hiện (giả lập ~30,050 kg), đang dao động |
| 3.3 | Chờ 3-5 giây | Số cân chuyển sang xanh "Ổn định", đếm ngược hiện |
| 3.4 | Quan sát Checklist điều kiện | 4 dòng: QR ✓, Vị trí AI ✓, Cân ổn định ✓/✗, Điều kiện trước ✓ |
| 3.5 | Bấm **Lưu phiếu (F5)** | Chốt cân lần 1 thành công, hiện trọng lượng |
| 3.6 | Quan sát thông báo | "Đã chốt cân FirstWeight: 30,050.000 kg — Hướng dẫn đi đo tạp chất" |
| 3.7 | Quan sát lưới xe đã cân | Có dòng mới hiện cân vào |
| 3.8 | Bấm tab "Cân cả vào và ra" | Lưới lọc theo chuyến có cả 2 lượt |
| 3.9 | Quan sát thanh trạng thái dưới cùng | Chấm xanh/đỏ cho từng thiết bị |

---

## Test 4: Đo tạp chất (UI-05)

**Mở:** Menu Vận hành → Mía và chất lượng

| Bước | Hành động | Kết quả mong đợi |
|------|-----------|-------------------|
| 4.1 | Nhập ký tự vào ô QR, nhấn Enter | Hiện thông tin: Biển số, Chủ mía, Loại mía, Giống mía |
| 4.2 | Quan sát số cân mẫu | Hiện ~8.500 kg (giả lập), đang dao động |
| 4.3 | Chờ ổn định ≥ 3 giây | Tự ghi "Mẫu trước: 8.500 kg" |
| 4.4 | Chờ hệ thống nhận mẫu 2 | Tự ghi "Mẫu sau" khi ổn định |
| 4.5 | Quan sát preview tạp chất | Hiện % tạp chất dự kiến |
| 4.6 | Bấm **Ghi kết quả tạp chất** | MessageBox: "Đã ghi tạp chất X.XXXX %" |
| 4.7 | Nhập CCS = 12.5, bấm **Tiếp nhận chữ đường** | MessageBox: "Đã ghi nhận chữ đường" |
| 4.8 | (Tùy chọn) Bấm "Nhập tay" | Panel nhập tay hiện ra, yêu cầu lý do + phê duyệt |

---

## Test 5: Cân lần 2 (UI-03)

**Mở:** Quay lại tab Cân hợp nhất, bấm **Esc** để làm mới

| Bước | Hành động | Kết quả mong đợi |
|------|-----------|-------------------|
| 5.1 | Nhập ký tự vào ô QR, nhấn Enter | Hệ thống nhận diện đây là cân lần 2 (đã có cân lần 1) |
| 5.2 | Quan sát | Gọi FRM đồng bộ → kiểm tra tạp chất → cho phép cân |
| 5.3 | Nếu thiếu tạp chất | Hiện "Phải đo tạp chất trước" → CHẶN |
| 5.4 | Nếu thiếu CCS | Hiện cảnh báo vàng, cho phép tiếp tục |
| 5.5 | Chờ ổn định → bấm **Lưu phiếu (F5)** | Chốt cân lần 2, hiện TL hàng |
| 5.6 | Quan sát nút In phiếu (F6) và Sync ERP | Được bật sau cân lần 2 |
| 5.7 | Bấm **In phiếu (F6)** | Dialog chọn máy in hiện ra |
| 5.8 | Bấm **Sync ERP** | Hiện trạng thái: Held hoặc New |

---

## Test 6: Kho vận (UI-04)

**Mở:** Menu Vận hành → Kho vận

| Bước | Hành động | Kết quả mong đợi |
|------|-----------|-------------------|
| 6.1 | Nhập ký tự vào ô QR, nhấn Enter | Hiện thông tin chuyến: biển số, hàng hóa, kho |
| 6.2 | Bấm **Vào kho (F4)** | Thông báo "Xe đã vào kho thành công" |
| 6.3 | Bấm **Mở phiên đo** | Phiên đo được tạo trong lưới |
| 6.4 | Bấm **Sinh dữ liệu bình thường** | 10 sự kiện IoT + 600 bao được sinh |
| 6.5 | Bấm **Đóng phiên đo** | Tổng hợp: kg IoT, bao thực, đủ dữ liệu |
| 6.6 | Bấm **Chạy đối soát** | Lưới đối soát hiện 4 phép so sánh |
| 6.7 | Bấm **Hoàn thành ra kho (F5)** | Thông báo "Xe ra kho thành công" |
| 6.8 | Quan sát lưới "Trong kho" / "Đã ra kho" | Xe chuyển sang tab "Đã ra kho" |
| 6.9 | Form có thể cuộn xuống | Tất cả sections hiện đầy đủ khi cuộn |

---

## Test 7: Cổng ra (UI-11)

**Mở:** Menu Vận hành → Cổng Đăng Tài

| Bước | Hành động | Kết quả mong đợi |
|------|-----------|-------------------|
| 7.1 | Nhập ký tự vào ô QR, nhấn Enter | Hiện thông tin chuyến + timeline mốc thời gian |
| 7.2 | Quan sát timeline | Vòng tròn xanh/vàng/xám cho từng checkpoint |
| 7.3 | Nếu chuyến đã Reconciled/Posted/Closed | Hiện "Xe đã hoàn thành cân. Xác nhận cho xe qua!" |
| 7.4 | Nếu chưa đủ checkpoint | Từ chối + danh sách bước thiếu |
| 7.5 | Bấm **Xác nhận (F6)** | Mở barie 30s → xác nhận → "Xe đã qua cổng" |
| 7.6 | Quan sát lưới phía dưới | Tab "Trong nhà máy" và "Đã ra" |

---

## Test 8: Chi tiết chuyến (UI-06)

**Mở:** Double-click chuyến trên UI-01, hoặc Menu Tra cứu → Chi tiết chuyến

| Bước | Hành động | Kết quả mong đợi |
|------|-----------|-------------------|
| 8.1 | Chọn chuyến từ ComboBox | Timeline + 8 tab hiện dữ liệu |
| 8.2 | Tab Checkpoint | Danh sách: GateIn, FirstWeight, ImpuritySample, v.v. |
| 8.3 | Tab Các lần cân | Gross + Tare, bản cũ xám |
| 8.4 | Tab Chất lượng | Tạp chất + CCS |
| 8.5 | Tab Phân bổ → bấm **Phân bổ** | Dialog chọn phương pháp + nhập dòng chủ mía |
| 8.6 | Tab Tích hợp | Thông điệp FRM/ERP |
| 8.7 | Tab Audit | Lịch sử đầy đủ: đăng tài, cân, tạp chất, v.v. |

---

## Test 9: Xử lý ngoại lệ trên form cân (UI-03)

| Bước | Hành động | Kết quả mong đợi |
|------|-----------|-------------------|
| 9.1 | Quan sát nút "Nhập tay" | Hiện (preview user có mọi quyền) |
| 9.2 | Bấm "Nhập tay" | Dialog: số cân, đơn vị, vai trò, lý do, mã phê duyệt |
| 9.3 | Để trống lý do → bấm Xác nhận | "Lý do nhập tay là bắt buộc" |
| 9.4 | Quan sát nút "Cân lại" | Hiện (preview user có quyền reweigh) |
| 9.5 | Bấm "Cân lại" | Dialog: vai trò (Gross/Tare), lý do, tài liệu sự cố |

---

## Test 10: Các màn hình quản lý

| Màn hình | Cách mở | Kiểm tra |
|----------|---------|----------|
| UI-07 Hàng chờ ngoại lệ | Menu Vận hành | Lưới hiện approvals chờ duyệt, sự cố mở |
| UI-08 Giám sát tích hợp | Menu Kỹ thuật | Lưới thông điệp FRM/ERP + trạng thái |
| UI-09 Cấu hình | Menu Kỹ thuật | 3 tab: Profile, Bàn cân, Thiết bị |
| UI-10 Báo cáo | Menu Tra cứu | Lọc theo ngày → xuất CSV |

---

## Test 11: Menu Preview (thanh menu)

| Bước | Hành động | Kết quả mong đợi |
|------|-----------|-------------------|
| 11.1 | Tắt "Đã đo tạp chất" | Form cân chặn cân lần 2 |
| 11.2 | Tắt "Chữ đường đang chờ" | Form cân hiện cảnh báo vàng CCS |
| 11.3 | Tắt "Hợp đồng API đã xác minh" | Sync ERP → Held thay vì New |
| 11.4 | Tắt "Dung sai đã duyệt" | Đối soát → Pending/MissingData |

---

## Phím tắt tham khảo

| Form | Phím | Hành động |
|------|------|-----------|
| UI-02 Đăng tài | F4 | Lưu phiếu |
| UI-02 Đăng tài | F5 | In phiếu |
| UI-02 Đăng tài | F6 | Xác nhận |
| UI-03 Cân | F5 | Lưu phiếu (chốt cân) |
| UI-03 Cân | F6 | In phiếu cân |
| UI-04 Kho | F4 | Vào kho |
| UI-04 Kho | F5 | Hoàn thành ra kho |
| UI-11 Cổng | F6 | Xác nhận qua cổng |
| Tất cả | Esc | Làm mới form |
| Tất cả | Ctrl+W | Đóng tab |
