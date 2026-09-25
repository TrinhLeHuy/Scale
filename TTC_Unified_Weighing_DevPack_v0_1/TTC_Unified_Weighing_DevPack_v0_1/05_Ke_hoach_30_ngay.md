# 05. Kế hoạch triển khai 30 ngày và backlog

## 1. Điều kiện lập kế hoạch

Đây là kế hoạch mục tiêu theo U-15, chưa phải kết luận một đội DEV chưa rõ quy mô chắc chắn hoàn thành tất cả nhà máy trong 30 ngày. Ngày 1 cần chốt nhà máy, số bàn/luồng, profile và nguồn lực. Không tự cắt chức năng đã xác nhận hoặc lấy một bản demo làm hoàn thành thay thế.

Giả định U-11: thiết bị đã có. Các đầu vào vẫn cần cung cấp ngay: tài liệu giao tiếp và mẫu tín hiệu; API hiện hành và tài khoản test; mẫu BOM/dung sai; dữ liệu ảnh/bao trong điều kiện thực tế; đầu mối duyệt nghiệp vụ; quyền tiếp cận hiện trường. Những phần này không thể được thay bằng AI sinh code.

Đề xuất tổ chức các luồng công việc song song: nghiệp vụ/giao diện/dữ liệu; tích hợp FRM/ERP và thiết bị; AI/IoT; kiểm thử/vận hành. Người phụ trách nghiệp vụ phải duyệt hàng ngày cho các blocker. Không mặc định AI coding đồng nghĩa chỉ cần một người phụ trách mọi phần.

## 2. Lịch theo ngày tương đối

| Mốc | Công việc | Bàn giao/cổng kiểm tra |
|---|---|---|
| Ngày 1–3 | Chốt D-01…D-12 cần thiết; rà soát profile đang dùng; đọc API hiện hành; lấy tín hiệu đầu cân/QR/AI/IoT; kiểm tra quyền barie; dữ liệu BOM/dung sai; khóa mapping và thiết kế | Danh mục phạm vi có chủ duyệt; một kết nối thử end-to-end đọc nguồn/đầu cân và ghi vào môi trường test; danh sách blocker rõ người xử lý |
| Ngày 4–7 | Lõi trip/QR/profile, quyền/audit, checkpoint và cân 1–2; khóa bàn; in QR/phiếu; phân biệt nhập–xuất; nền outbox/incident | Hai chuyến mẫu mía và vật tư đi qua lõi, số cân được đọc từ thiết bị thử; test chặn QR sai, cân trùng, bàn cân đồng thời |
| Ngày 8–14 | Adapter ERP/FRM với dữ liệu thật test; cân mẫu/chính sách; AI vị trí, IoT, đếm bao; phiên kho; barie trong chế độ thử an toàn | Một chuyến đúng có dữ liệu đầy đủ qua tích hợp; một chuyến lỗi bị chặn; mỗi trường số lượng có mapping đã ký; test mất phản hồi |
| Ngày 15–20 | Đối soát BOM/dung sai, nhiều kho/Shipment/chủ mía, cân lại/nhập tay, profile bổ sung đã chốt; báo cáo; xử lý lỗi và phục hồi; hoàn thiện phiếu | Feature complete theo phạm vi đã duyệt; truy vết R → code → test; không còn mock trên các luồng định đưa vào nghiệm thu |
| Ngày 21–25 | Kiểm thử hệ thống và hiện trường: tải/đồng thời; hình ảnh ngày/đêm/che khuất thực tế; bao sát nhau/đảo chiều; lỗi nguồn; chuyển đổi dữ liệu và restore diễn tập | Báo cáo kết quả từng test, bằng chứng số đo/ERP/FRM, ngưỡng AI và sai số được nghiệm thu; danh sách lỗi chặn và phương án xử lý |
| Ngày 26–28 | Người dùng nghiệm thu; chạy đối chiếu không ghi kép; huấn luyện; diễn tập cutover/rollback; ký go/no-go | Người phụ trách vận hành, cân, kho, nguyên liệu, IT và tích hợp ký theo phạm vi; lịch cắt và đầu mối hỗ trợ sẵn sàng |
| Ngày 29–30 | Cutover sau go/no-go; theo dõi giao dịch thật và đối chiếu sổ, xử lý tồn đọng; bàn giao | Hệ thống mới là bên vận hành chính thức trong phạm vi; báo cáo sau chuyển đổi; tài liệu và trách nhiệm xử lý sự cố |

## 3. Cổng kiểm tra bắt buộc

**G1 — Cuối Ngày 3:** API và thiết bị phải được chứng minh bằng request/response và tín hiệu thử, không chỉ xác nhận miệng đã có. Thiếu ANPR không tự chặn dự án nếu phương án xác thực thủ công tại làn được duyệt, nhưng không được mô tả như auto hoàn toàn. Thiếu model AI vị trí/đếm bao hoặc giao thức thực tế là rủi ro tiến độ phải báo ngay.

**G2 — Cuối Ngày 14:** ít nhất luồng mía và vật tư thuộc phạm vi đi được từ nguồn kế hoạch tới dữ liệu thực tế trong môi trường test. Các điểm WeightIn/WeightOut/TotalWeightOut và Quantity theo chuyến được kiểm chứng. Phát hiện gửi trùng hoặc gán nhầm xe là lỗi chặn.

**G3 — Cuối Ngày 25:** đạt kiểm thử ngoại lệ, chất lượng AI/đầu cân và khôi phục; đã thử dữ liệu chuyển đổi. Không được tăng dung sai hoặc giảm yêu cầu AI chỉ để báo đạt tiến độ nếu chưa được chủ nghiệp vụ duyệt.

**G4 — Trước cutover:** không còn lỗi mở barie sai, ghi sai/trùng/mất số cân, nhân đôi Receipt/Ship Confirm, bỏ qua lỗi nguồn hoặc mất lịch sử. Các số liệu và quy trình có người ký. Trường hợp không đạt, báo không đạt điều kiện go-live và trình quyết định tiến độ/phạm vi; không âm thầm nghiệm thu demo thay vận hành.

## 4. Backlog theo nhóm triển khai

| Work package | R-ID chính | Phụ thuộc | Bằng chứng hoàn thành |
|---|---|---|---|
| WP-01 Phạm vi, danh mục và profile | R-001,002,030,031,032 | D-01 | Cấu hình profile và danh mục nghiệp vụ đã ký |
| WP-02 Trip/QR/checkpoint | R-003,004,005,012 | WP-01, D-02 | Đăng tài chính thức/tạm, nhiều chuyến cùng xe và checkpoint đúng |
| WP-03 Lõi cân | R-007,008,009,010,011 | WP-02, D-04 | Cân nhập/xuất, hai bàn đồng thời, cân lại và lịch sử |
| WP-04 Mía | R-013,014,015,016,023,040 | WP-02/03, D-09 | Tạp chất, chữ đường đến muộn, chính sách, ghép chủ mía đúng |
| WP-05 IoT/AI | R-017,018,019,039 | WP-02, D-03/05/06 | Phiên đúng chuyến/SKU, không lặp, lỗi/reset/UNKNOWN được chặn |
| WP-06 Đối soát | R-020,021,029,038 | WP-03/04/05 | So sánh độc lập, dung sai có phiên bản, phân bổ bảo toàn |
| WP-07 Tích hợp tin cậy | R-022,023,024,037 | D-02, WP-03/06 | ACK/FAILED/UNKNOWN, nhiều Shipment, điều chỉnh không trùng |
| WP-08 Cổng và sự cố | R-006,025,039 | D-03/07, WP-02/07 | Đúng xe mở đúng cổng; timeout không phát lại mù; resume kiểm tra lại |
| WP-09 Vận hành | R-026,027,028,034,035 | WP-02…08, D-08/11 | Quyền server, audit, phiếu/báo cáo, test không ghi thật |
| WP-10 Kiểm thử và thay thế | R-033,036 | Tất cả gói thuộc phạm vi, D-12 | UAT, chuyển đổi, restore/rollback và bàn giao |

Mỗi task triển khai cần ghi R-ID, điều kiện đầu vào, thay đổi schema, API/thiết bị liên quan, quy tắc trạng thái, trường hợp lỗi, tests và ảnh/log chứng minh. Không đóng task chỉ vì AI đã tạo file.

## 5. Hồ sơ nghiệm thu

Bao gồm danh sách profile/nhà máy/bàn cân đã nghiệm thu; mapping API đã kiểm chứng; phiên bản model AI và vùng camera; quy tắc dung sai/BOM; kết quả test và ngoại lệ; số liệu đối soát với FRM/ERP; quyền được duyệt; hồ sơ chuyển đổi; backup/restore; hướng dẫn vận hành/cân lại/nhập tay/sự cố; người và thời gian tiếp nhận.

Các chỉ số phải có mẫu số và tập thử rõ: tỷ lệ chuyến không cần thao tác ngoài quy trình chuẩn, tỷ lệ kết luận AI đúng/sai theo tình huống, chênh số đếm, thời gian cân/đối soát, số sự kiện lặp đã loại, thời gian phục hồi. Không lấy việc AI trả confidence cao thay kết quả đánh giá thực tế.
