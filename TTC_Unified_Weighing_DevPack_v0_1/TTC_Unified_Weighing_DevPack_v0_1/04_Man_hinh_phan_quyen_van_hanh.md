# 04. Màn hình, phân quyền và vận hành

## 1. Thiết kế giao diện theo nhiệm vụ

S01 trang 4 và 7 có bố cục gồm thông tin đăng tài/vận hành, hình camera, nút thao tác và danh sách xe. S02 trang 17 minh họa số cân nổi bật và vùng camera. Kế thừa thông tin nghiệp vụ, không bắt buộc sao chép giao diện cũ hoặc các nút lưu thủ công khi U-04/U-05 yêu cầu tự động.

| ID màn hình | Người dùng | Thông tin/chức năng bắt buộc |
|---|---|---|
| UI-01 — Tổng quan | Điều vận, quản lý | Xe chờ, đang trong nhà máy, cân 1 chưa cân 2, chuyến PAUSED, số kg theo nghiệp vụ; trạng thái bàn cân, API, AI, IoT; lọc nhà máy/ca |
| UI-02 — Đăng tài | Cổng, điều vận | Chọn hồ sơ, tra FRM/ERP, biển số/đầu kéo/rơ-moóc, tài xế/nhà vận tải, TRIP/Shipment/lệnh đốn, kế hoạch và thông tin còn thiếu; QR tạm/chính thức; số lượt, hủy trước cân |
| UI-03 — Cân hợp nhất | Người trực cân, giám sát | Số cân lớn, đơn vị, bàn cân được khóa, chuyến, lần cân/vai trò; camera và vị trí VALID/INVALID/UNKNOWN; thời gian ổn định; từng điều kiện đạt/chưa đạt; số cân trước; thông báo lý do bị chặn |
| UI-04 — Phiên kho/đối soát trực tiếp | Kho, điều vận | Mở/đóng phiên theo chuyến/SKU/thiết bị; kg IoT, số bao AI, số kho xác nhận và nguồn BOM; các chênh lệch riêng; cảnh báo thiếu/lặp/reset/chuyển SKU |
| UI-05 — Mía và chất lượng | Nhân viên mẫu, giám sát | Mã mẫu/chuyến, cân trước/sau mẫu, tạp chất, trạng thái chữ đường, chính sách FRM, cờ không đồng bộ; ghép chủ mía; lịch sử lấy lại mẫu |
| UI-06 — Chi tiết chuyến | Cổng, cân, kho, quản lý theo quyền | Timeline checkpoint; các lần cân/bản sửa; ảnh; chứng từ; BOM snapshot; đối soát; phân bổ; tích hợp; phiếu; mọi blocker |
| UI-07 — Hàng chờ ngoại lệ | Giám sát | Phiếu cân lại/nhập tay, chênh lệch, phân bổ chưa duyệt, chuyến bị từ chối, sự cố; biên bản, người duyệt, hành động hợp lệ kế tiếp |
| UI-08 — Giám sát tích hợp | IT, đầu mối FRM/ERP | Message/operation, request đã che dữ liệu nhạy cảm, response, trạng thái UNKNOWN/FAILED/ACKNOWLEDGED, số lần gửi; đối soát phản hồi, không có nút gửi lại mù quáng |
| UI-09 — Thiết bị/cấu hình | IT, quản trị có quyền | Ánh xạ làn/camera/đầu đọc/đầu cân, profile, ngưỡng đã duyệt, hiệu lực/version, cấu hình một/hai bàn, chế độ test |
| UI-10 — Báo cáo và phiếu | Người được phân quyền | Lượt cân; tổng hợp; xe trong nhà máy; cân 1 chưa cân 2; số liệu đối soát; can thiệp tay; xuất dữ liệu; in lại có đánh dấu |

Trong UI-03, trạng thái đủ điều kiện được tính ở server. Giao diện không tự cộng/trừ số cân hoặc gửi giá trị AUTO do người dùng sửa. Hiển thị rõ tín hiệu đã cũ/mất kết nối thay vì giữ số cuối như đang trực tuyến.

Nút đặc biệt như nhập tay, cân lại, chuyển bàn, giải quyết UNKNOWN và tiếp tục sau lỗi chỉ hiện khi có quyền nhưng server vẫn phải kiểm tra quyền. Không dùng chỉ ẩn nút để bảo vệ nghiệp vụ.

## 2. Trường tối thiểu trên phiếu cân

Giữ nội dung mẫu từ S02 trang 23 và S03 trang 17–19: tên đơn vị/nhà máy, số phiếu, số đăng tài, xe/tài xế/nhà vận tải theo phạm vi, loại nghiệp vụ, nguồn kế hoạch/chứng từ/Shipment/lệnh đốn, loại hàng, cân cả xe, bì, net, đơn vị, thời điểm hai lượt, bên giao/nhận và QR tra cứu.

Với mía, hiển thị tạp chất/chữ đường/trọng lượng thanh toán nếu đã nhận hợp lệ từ nguồn có thẩm quyền; không tự tính hoặc in số 0 khi kết quả đang chờ. Với tách Shipment/chủ mía, nêu rõ tổng xe và lượng phân bổ tương ứng. Phiếu điều chỉnh/in lại có revision, tham chiếu bản gốc và trạng thái.

Phiếu tạm hoặc chờ đồng bộ không có nhãn đã hoàn tất. In lại không sinh số cân mới, không gửi lại Receipt/Ship Confirm. S02 trang 13 bổ sung một phiếu đăng tài và ba QR; S02 trang 11 có mô tả hai phiếu. D-11 duyệt mẫu/số bản trước triển khai, không chọn thầm một bản.

## 3. Phân quyền đề xuất

S04 trang 78–80 cho phép nhóm quyền theo module/chức năng và gán nhà máy. Dưới đây là nhóm vai trò P để triển khai, không mặc định tên tài khoản hiện có.

| Vai trò | Quyền chủ yếu | Giới hạn |
|---|---|---|
| Cổng/đăng tài | Tra kế hoạch, đăng tài, in QR, xác minh xe tại cổng, ghi nhận qua cổng | Không sửa trọng lượng hoặc BOM |
| Vận hành cân | Theo dõi phiên cân, thực hiện bước hợp lệ, yêu cầu cân lại | Không ghi đè số đo hoặc tự duyệt ngoại lệ ngoài quyền |
| Kho | Gắn chuyến/SKU/phiên thiết bị, xác nhận checkpoint kho, xem đối soát | Không đổi số đo cân xe/đếm AI để khớp đơn |
| Nhân viên mẫu | Gắn mẫu, cân mẫu/nhận kết quả theo quyền | Không sửa chính sách FRM |
| Giám sát nghiệp vụ | Duyệt ngoại lệ/nhập tay/cân lại/chuyển bàn/tiếp tục theo phạm vi | Mọi duyệt có lý do/biên bản; không bỏ điều kiện an toàn hoặc sửa lịch sử |
| Quản trị kỹ thuật | Thiết bị, cấu hình kết nối, người dùng và giám sát | Không mặc định có quyền duyệt số lượng hàng chỉ vì là admin hệ thống |
| Đối soát/tích hợp | Kiểm tra chứng từ đích, giải quyết UNKNOWN theo thẩm quyền | Không tạo giao dịch bù chưa được nghiệp vụ cho phép |
| Kiểm tra/đọc báo cáo | Đọc lịch sử, báo cáo, bằng chứng theo nhà máy | Không cập nhật nghiệp vụ |

D-08 phải chốt người/nhóm có quyền; có cần người lập khác người duyệt cho các trường hợp rủi ro cao. Không tự giả định mọi ngoại lệ được một tài khoản ADMIN duyệt.

## 4. Nhật ký và dữ liệu vận hành

Ghi actor/service identity, timestamp, correlation_id, trip/revision, phạm vi, loại hành động, before/after, lý do, biên bản, thiết bị và kết quả. Các thao tác quan trọng: đăng/hủy, in lại, thay tham chiếu nguồn, chuyển bàn, nhập tay, cân lại, sửa kết quả mẫu, phân bổ, đổi BOM snapshot, retry, mở barie, pause/resume.

Ảnh/video bằng chứng phải liên kết sự kiện; lưu URI/định danh và hash khi phù hợp. Thời hạn giữ video/ảnh, dữ liệu thô, audit, chuyến và dữ liệu cá nhân cần D-08; không tự chọn một thời hạn lưu cho mọi loại. Cấu hình retention không được làm mất bằng chứng trước thời hạn đã duyệt.

Không đưa mật khẩu/token vào log, code, file cấu hình commit hoặc prompt AI. Khóa dịch vụ chỉ được quyền thao tác cần thiết. Yêu cầu môi trường test tách biệt; mock không được gửi barie thật hay ghi chứng từ thật.

## 5. Chuyển đổi thay thế hoàn toàn

Đề xuất phương án ưu tiên để giảm rủi ro: hoàn tất các chuyến đang mở trên hệ thống cũ trước thời điểm cắt; khóa nhận chuyến mới trên hệ thống cũ; chốt sổ thông điệp và số phiếu; đối soát với FRM/ERP; sau đó mở nhận chuyến trên hệ thống mới. Đây là cutover có kiểm soát, không phải duy trì hai hệ thống cân chính thức cùng ghi.

Nếu bắt buộc chuyển chuyến đang dở, cần nhập đầy đủ ID cũ, cân lần 1, vai trò cân, thời gian, QR, nguồn, trạng thái checkpoint, các kết quả đã gửi và bằng chứng; không chỉ nhập biển số + số kg. Chuỗi số phiếu và InterfaceReference phải tránh xung đột.

Với dữ liệu lịch sử: D-08 quyết định nhập vào hệ thống mới hay lưu kho tra cứu chỉ đọc. Thay thế hoàn toàn chức năng vận hành không có nghĩa được xóa lịch sử cũ. Khối lượng chuyển đổi và thời gian downtime chưa có, phải diễn tập với dữ liệu thực.

Chạy quan sát/so sánh trước cutover được phép nếu hệ thống mới không gửi chứng từ chính thức hoặc mở barie thật. Phải có **một bên ghi chính thức** tại từng thời điểm.

## 6. Quay lui và sự cố

Trước go-live: sao lưu được kiểm tra phục hồi, có danh sách chuyến/message và trạng thái ERP/FRM, phiên bản cấu hình, cơ chế đổi bên gửi chính thức, người có quyền quyết định rollback và điều kiện kích hoạt.

Nếu rollback sau khi đã có giao dịch mới: khóa gửi/chốt mới trước; xuất danh sách giao dịch đã ACK/UNKNOWN; đối soát hệ thống đích; nhập/đối chiếu ở bên tiếp quản với đúng tham chiếu; không chỉ khôi phục DB cũ rồi chạy tiếp vì có thể gửi trùng chứng từ. Các lệnh barie cũ hết hạn không được phát lại.

Mở khẩn cấp để xử lý an toàn tại hiện trường là quy trình riêng, có audit và đối chiếu sau; không đổi trạng thái cân/đơn hàng thành thành công để hợp thức hóa việc xe đã ra.
