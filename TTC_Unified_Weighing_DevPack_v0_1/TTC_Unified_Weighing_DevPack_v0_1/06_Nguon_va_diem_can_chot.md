# 06. Nguồn, truy vết và các điểm cần chốt

## 1. Danh mục nguồn

Trang dưới đây là số trang của tệp PDF tính từ trang đầu, không dùng số trang cũ trong mục lục nếu lệch.

| Mã | Tài liệu | Vai trò sử dụng |
|---|---|---|
| S01 | HDSD Phần mềm đăng tài.pdf — 14 trang | Thao tác xuất hàng bán, TRIP/Shipment, QR, kho, cân và phiếu |
| S02 | TTC_AMMS_CanMia_V1.2.pdf — 24 trang | Quy trình mía, bổ sung hai bàn cân, cân mẫu, chữ đường, chính sách, ngoại lệ |
| S03 | TTC_AMMS_ERP_QT06_Sale Order for Ship Confirm_V1.0.pdf — 21 trang | Đăng tài/xuất hàng, kho, cân lần 2, dung sai và chia Shipment |
| S03-DUP | TTC_AMMS_ERP_QT06_Sale Order for Ship Confirm_V1.0 1.pdf — 21 trang | Trùng byte với S03 theo SHA-256; không coi là bản cập nhật nghiệp vụ riêng |
| S04 | TTC_AMMS_V1.0.pdf — 94 trang | Bao quát nghiệp vụ cân, nội bộ, trả hàng, không ERP, danh mục/quyền/báo cáo |
| S05 | SBT_Scale_Integration Solution Design_v1.0.pdf — 56 trang | Thiết kế tích hợp ERP–Scale, nhóm giao dịch, trường dữ liệu và mẫu trao đổi |

Các tài liệu tham khảo ghi mốc năm 2021; người phụ trách xác nhận chúng là tài liệu tham khảo. Gói này không khẳng định cấu hình hiện tại trùng PDF hoặc đã kết nối được API hiện hành. Không truy cập tài liệu ngoài bộ PDF được liên kết trong S04.

## 2. Các điểm đã phát hiện cần bảo toàn/đối chiếu

| Mã | Nội dung nguồn | Cách xử lý trong bản khung |
|---|---|---|
| C-01 | S01 tr.7,11 có thao tác bấm lưu; tr.12 có đồng bộ thủ công. U-04/U-05 muốn tự động cao. | Tách thao tác cũ khỏi nghiệp vụ; tự chốt/lưu khi đủ điều kiện, giữ can thiệp tay có quyền. Không bắt buộc sao chép mọi nút của UI cũ. |
| C-02 | S02 tr.21 yêu cầu tạp chất trước cân 2; tr.20,22 cho chữ đường đến sau và chỉ cảnh báo. | Thiếu tạp chất chặn cân 2; thiếu chữ đường không tự chặn. Không gom hai kết quả thành một cờ chất lượng bắt buộc. |
| C-03 | S04 tr.20 có phần Thảo luận về chưa có chính sách FRM thì không hoàn tất cân 2; S02 tr.22 yêu cầu gọi API khi quét QR. | Giữ điểm kiểm tra chính sách nhưng cần D-09 xác nhận trạng thái/điều kiện chặn, không biến nội dung thảo luận thành hợp đồng API chắc chắn. |
| C-04 | S02 tr.17 đồng bộ từng lượt cân; tr.18 giữ phiếu chưa đồng bộ khi chủ mía ghép chưa phân bổ. | Phân biệt loại thông điệp cân tổng và kết quả phân bổ; D-09 chốt API nào được gửi trước. |
| C-05 | S02 tr.11 ghi hai phiếu QR; tr.13 bổ sung một phiếu đăng tài và ba QR. | In cấu hình được; D-11 chốt bản mẫu và số bản chính thức. |
| C-06 | S03 tr.16 có khoảng min/max mỗi bao và biên 20 kg; S04 tr.29 ghi bảng dung sai vật tư khác chưa có. | Không áp biên 20 kg cho mọi hàng. D-06 xác nhận dung sai theo profile/SKU/cơ sở đo. |
| C-07 | S04 tr.27,38 có trao đổi về quy cách chưa được ERP gửi; U-09 xác nhận FRM/ERP có BOM/quy cách. | Ưu tiên yêu cầu mới U-09, nhưng phải có hợp đồng lấy dữ liệu và phiên bản; không tự tạo thông số. |
| C-08 | S05 tr.9–10,13 và tr.55–56 có tên WeightIn/WeightOut/TotalWeightOut chưa thống nhất với diễn giải vai trò; code kiểu chuỗi/số và response mẫu có sai cú pháp. | Giữ số cân thời gian/vai trò riêng, khóa mapping từng operation sau khi test API hiện hành; không sửa ngầm nguồn. |
| C-09 | S03 tr.15 mô tả từ chối xe, net=0, đồng bộ để hủy đơn hàng. | D-10 phải xác minh hủy SO, Shipment hay lượt giao; không cho AI tự viết lệnh hủy toàn bộ đơn. |
| C-10 | S04 tr.55–56 có QR cố định cho xe nội bộ và chú thích chưa rõ cách gắn kế hoạch theo ngày. | Thiết kế đề xuất tách vehicle_token và trip_id mỗi chu kỳ; cần duyệt, không coi QR cố định là một chuyến dùng mãi. |
| C-11 | U-13 yêu cầu lỗi thì dừng; tài liệu cũ có nhập tay, gửi lại và đổi bàn khi hỏng. | Giữ chức năng có kiểm soát nhưng không tự tiếp tục trong lúc lỗi; tạm ngưng, xác minh rồi phục hồi theo D-07. |
| C-12 | U-06 chỉ xác nhận lộ trình nhập tay biển số trước, camera sau. AI vị trí U-05 không phải ANPR. | D-03 chốt cách xác thực xe vật lý. Chưa có nhận diện độc lập thì không tuyên bố không người hoàn toàn. |

## 3. Danh mục quyết định còn thiếu

Không yêu cầu anh Ân phải tự trả lời tất cả thông số. Đây là đầu việc giao đúng đầu mối trong những ngày đầu; dữ liệu chưa có để null/TBD, không phải 0 hoặc mặc định đạt.

| Mã | Cần chốt | Chủ trì đề xuất | Thời điểm | Chặn gì khi chưa có |
|---|---|---|---|---|
| D-01 | Nhà máy triển khai, số bàn/cổng/làn, số xe/đồng thời và danh sách nghiệp vụ đang dùng phải thay thế; phạm vi profile nội bộ/trả hàng/không ERP/nhiều lượt | Anh Ân + vận hành | Ngày 1 | Kế hoạch giao hàng/ước lượng và tiêu chí hoàn thành |
| D-02 | API hiện hành: auth, request/response mẫu, các operation đọc/ghi, kiểu dữ liệu, trạng thái thành công/lỗi, chống trùng, tra cứu timeout, điều chỉnh/hủy | IT FRM/ERP + DEV | Ngày 1–3 | Adapter chính thức; retry và cutover |
| D-03 | Hiện có ANPR đã nghiệm thu hay xác nhận xe thủ công tại làn; quy tắc đầu kéo/rơ-moóc, thời hạn xác nhận, điểm mở barie và trạng thái an toàn | Vận hành cổng + IT/thiết bị | Ngày 1–3 | Auto-open an toàn và mức tự động hóa được công bố |
| D-04 | Model/giao thức đầu cân, đơn vị/độ phân giải/tải tối đa, tín hiệu stable, dao động được phép, số mẫu/độ mới, zero, vai trò hai bàn | Đơn vị cân + DEV | Ngày 1–3 | Chốt cân thực; kiểm thử chính xác |
| D-05 | Đầu ra dịch vụ AI/IoT, mapping camera/vùng và chuyến, counter semantics/reset, chiều đếm, SKU, watermark, chất lượng và ngưỡng AI | Đội AI/IoT + kho + DEV | Ngày 1–3 | Ghi số đúng phiên, chống thiếu/lặp và nghiệm thu AI |
| D-06 | BOM/quy cách thật: nguồn/phiên bản, nominal/min/max, số xác nhận kho, bì bao/pallet, mass basis, dung sai từng phép so sánh, làm tròn/phân bổ | Kho + nghiệp vụ ERP/FRM | Ngày 1–3 | Kết luận đối soát; không chặn việc lưu số đo gốc nếu lượt cân đủ điều kiện khác |
| D-07 | Ma trận lỗi: dừng toàn hệ thống hay làn/chuyến; nguồn nào bắt buộc từng bước; ai xác nhận resume; xử lý hàng vẫn di chuyển khi mất dữ liệu | Vận hành + IT + an toàn thiết bị | Ngày 1–3 | Cho chạy chính thức và phục hồi |
| D-08 | Nhóm quyền, người duyệt; nơi triển khai; lưu ảnh/video/audit/dữ liệu cá nhân; phạm vi lịch sử và chuyến đang dở; backup/restore | Anh Ân + IT + vận hành | Khung ngày 1–3, hoàn thiện trước ngày 20 | Quyền thật, bảo toàn dữ liệu và cutover |
| D-09 | FRM: chính sách hợp lệ là gì; tạp chất/chữ đường do phần nào ghi; gửi cân tổng khi ghép xe; phân bổ; số thanh toán trả về; kết quả đến muộn | Nguyên liệu + FRM | Ngày 1–3 | Luồng mía chính thức |
| D-10 | Xe bị từ chối/net=0; phạm vi hủy SO/Shipment/chuyến; đổi SO, giao và lấy hàng, bì lịch sử/đổi đầu kéo nếu muốn hỗ trợ | Kho + ERP + điều vận | Ngày 1–3 | Các nhánh ngoại lệ tương ứng; không tự suy diễn từ câu giữ toàn bộ |
| D-11 | Mẫu phiếu cân/QR, số bản, ký xác nhận, phiếu tách Shipment/chủ mía, in khi chờ dữ liệu và in lại | Cân + kho + nguyên liệu | Ngày 1–5 | In và bàn giao hồ sơ chính thức |
| D-12 | Ngưỡng nghiệm thu tải/độ trễ/AI/sai lệch, bộ dữ liệu thử, người ký; đội dự án và lịch hiện trường; go/no-go/rollback | Anh Ân + các trưởng luồng | Khung ngày 1–3, ký trước UAT | Cam kết hoàn thành và go-live |

## 4. Truy vết nhóm yêu cầu → nguồn

| Nhóm | Nguồn chính |
|---|---|
| R-003/004/005/027 | S01 tr.4–6; S02 tr.11–17; S03 tr.10–12 |
| R-007/008/009/010/011 | S02 tr.16–22; S04 tr.26; yêu cầu mới U-05/U-13 |
| R-012/029/038 | S03 tr.8,14–16; S04 tr.27–29 |
| R-013/014/015/016/023/040 | S02 tr.18–24; S04 tr.20,92 |
| R-017/018/019/020/039 | U-05/U-09/U-10; S03 tr.16; thiết kế P |
| R-021 | S03 tr.16–18 |
| R-022/024/037 | S05 mục 1–6; kiểm soát P |
| R-026/028/035 | S04 tr.75–92 |
| R-030/031/032 | S04 tr.37,45–74 |
| R-001/002/006/025/033/034/036 | U-01/03/10/13/15; S02 tr.17,22; S04 mục 12; thiết kế P |

## 5. Giới hạn của bản khung

Không có hợp đồng hiện hành FRM/ERP, tài liệu giao tiếp phần cứng hoặc bộ dữ liệu đánh giá AI trong những PDF đã đọc đủ để lấp các khoảng trống trên. Cấu trúc sự kiện, quyền, trạng thái và bộ kiểm thử ở đây là thiết kế đề xuất nhằm làm đầu vào rõ ràng cho DEV, không phải mô tả khẳng định hệ thống đang có. Mục tiêu chính xác và tự động được chuyển thành các kiểm soát/tiêu chí; kết quả thực tế còn phải được đo và nghiệm thu.
