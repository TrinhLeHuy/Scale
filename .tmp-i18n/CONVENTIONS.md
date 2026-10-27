# Quy ước i18n cho đợt sửa ngôn ngữ (dùng nội bộ, xóa sau khi xong)

Workspace: D:\Works\Scale (WinForms .NET 8). Ngôn ngữ: vi (chuẩn), en, lo (Lào), km (Khmer).

## Cơ chế có sẵn
- UI (project TTC.Weighing.WinForms): `Loc.T("key")`, `Loc.T("key", arg0, arg1)` (string.Format, placeholder `{0}`).
  Namespace `TTC.Weighing.WinForms.Localization`.
- Enum hiển thị: `Loc.Enum(value)` → khóa `enum.{TênKiểu}.{GiáTrị}` (ví dụ `enum.TripLifecycle.Registered`).
  Lưới (DataGridView) đã TỰ dịch enum/bool — không cần thêm code cho cột enum.
- Có/Không: `Loc.YesNo(bool)`.
- Domain/Application/Infrastructure (KHÔNG được tham chiếu WinForms): `TTC.Weighing.Domain.Common.DomainText.T("key", "bản tiếng Việt", args...)`.
  Bản tiếng Việt truyền vào là fallback khi chưa cài resolver (unit test). Giữ nguyên câu tiếng Việt cũ làm fallback
  để test hiện có (Assert.Contains("...tiếng Việt...")) vẫn qua.
- Font in phiếu: `LocalizedFont.Print` (thay cho "Times New Roman" cứng). Font UI: `AgrisTheme.Body(size, style)` /
  `AgrisTheme.Ui(size)` (thay cho `new Font("Segoe UI", ...)` cứng — Segoe UI không có chữ Lào/Khmer).

## Khóa
- Kiểu khóa theo file hiện có: chữ thường, chấm, snake_case: `reg.btn_save`, `weigh.qr_invalid`, `col.plate`, `print.field_so_xe`.
- TRƯỚC khi tạo khóa mới, tìm trong `src/TTC.Weighing.WinForms/Localization/Resources/strings.vi.json`
  xem đã có khóa cùng nghĩa chưa (common.*, col.*, field.*, print.*...) — dùng lại nếu có.
- Tiền tố theo màn hình: reg.* (Đăng tài), weigh.* (Cân), quality.*, ccs.*, warehouse.*, frm.*, erp.*, report.*,
  detail.*, exception.*, integration.*, config.*, login.*, main.*, print.*, rule.* (Domain rules), app.* (Application
  use case), infra.* (Infrastructure), process.* (trạng thái xử lý chuyến).

## KHÔNG được sửa trực tiếp 4 file strings.*.json
Mỗi người viết khóa mới / bản dịch vào MỘT file fragment riêng:
`D:\Works\Scale\.tmp-i18n\fragments\<tên-nhóm>.json`, dạng:
```json
{
  "reg.btn_check_plate": { "vi": "Kiểm tra", "en": "Check", "lo": "ກວດສອບ", "km": "ពិនិត្យ" }
}
```
- Bắt buộc đủ 4 ngôn ngữ, bản dịch thật (không `[TODO]`, không để tiếng Việt trong lo/km/en).
- Giữ nguyên placeholder `{0}`, `{1}`, `\n`, emoji/biểu tượng đầu chuỗi (✅ ⏳ 🌾 ...), phím tắt "(F5)", mã kỹ thuật
  (QR, FRM, ERP, CCS, DigiScale, B-001.3, D-04, R-024, PAUSED...).
- Thuật ngữ: Đăng tài = Registration / ລົງທະບຽນ / ការចុះឈ្មោះ; Cân lần 1/2 = First/Second weighing;
  Tạp chất = Impurity / ສິ່ງເຈືອປົນ / ភាពមិនបរិសុទ្ធ; Chữ đường (CCS) = Sugar content (CCS);
  Chủ mía = Cane grower / ເຈົ້າຂອງອ້ອຍ / ម្ចាស់អំពៅ; Mía = Sugarcane / ອ້ອຍ / អំពៅ; Vật tư = Materials / ວັດສະດຸ / សម្ភារៈ;
  Phiếu cân = Weigh ticket / ໃບຊັ່ງ / សំបុត្រថ្លឹង; Biển số = License plate / ປ້າຍທະບຽນ / ស្លាកលេខ;
  Lệnh đốn = Harvest order / ຄຳສັ່ງຕັດ / បញ្ជាកាប់; Bàn cân = Weighbridge / ຂົວຊັ່ງ / ស្ពានថ្លឹង.
  Xem thêm cách dịch đã dùng trong strings.en/lo/km.json để nhất quán.

## Phạm vi đổi
ĐỔI sang khóa: mọi chuỗi người dùng NHÌN THẤY: Text/HeaderText/PlaceholderText/AccessibleName/ToolTip, MessageBox
(nội dung + tiêu đề), nhãn trạng thái, thông báo lỗi trả lên UI, chữ vẽ trên phiếu in, tiêu đề dialog, item ComboBox/Radio.

KHÔNG đổi:
- Comment, log (Debug.WriteLine, logger), message exception kỹ thuật nội bộ không hiện cho người dùng.
- Giá trị DỮ LIỆU so sánh/gửi API/lưu DB (ví dụ so sánh `commodity.Contains("mía")`, tên loại mía từ FRM
  "Mía cháy" khi dùng làm giá trị dữ liệu, mã template "SUGARCANE"). Nếu một chuỗi vừa là dữ liệu vừa là nhãn
  (ví dụ ô checkbox "Mía cháy" trên phiếu) thì chỉ dịch phần NHÃN, giữ logic so sánh trên dữ liệu.
- Thư mục Development/ (dữ liệu giả lập) và Migrations/.

## Lỗi hay gặp cần sửa kèm
- Chuỗi tiếng Việt ghép: `$"Lỗi in: {ex.Message}"` → `Loc.T("print.error", ex.Message)` với vi "Lỗi in: {0}".
- Ngày tháng kiểu "Ngày dd tháng MM năm yyyy" → khóa có placeholder: "Ngày {0} tháng {1} năm {2}" / "Date {0}/{1}/{2}".
- Đơn vị "tấn" → khóa (common.unit_ton nếu có, hoặc tạo).
- Text control đặt ở field initializer (`new() { Text = "..." }`) vẫn đổi được thành `Text = Loc.T("...")`.
- So sánh chuỗi hiển thị (ví dụ `if (btn.Text == "Lưu")`) phải đổi sang so sánh trạng thái/Tag, không so sánh chuỗi đã dịch.

## Không build
KHÔNG chạy `dotnet build`/`dotnet test` (nhiều người làm song song sẽ khóa file obj). Dùng tool get_diagnostics
trên các file đã sửa để kiểm tra lỗi biên dịch. Fragment JSON phải hợp lệ (kiểm tra bằng cách đọc lại).
