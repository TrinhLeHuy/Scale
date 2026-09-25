# TTC | Bộ khung giao DEV: Hệ thống cân hợp nhất mía và vật tư

**Phiên bản:** 0.1 — bản mô tả để duyệt và triển khai.  
**Cơ sở:** 6 PDF do anh Ân cung cấp và 15 câu trả lời làm rõ trong cuộc trao đổi.  
**Mục tiêu thời gian:** hoàn thành dự án trong 1 tháng; kế hoạch sử dụng mốc tương đối Ngày 1–30, không phải cam kết đã xác minh về năng lực đội dự án.  
**Trạng thái:** tài liệu thiết kế, không phải phần mềm đã chạy, không phải bộ API FRM/ERP hiện hành đã kiểm chứng.

## Đọc theo thứ tự

| Tệp | Công dụng |
|---|---|
| 01_SRS_Pham_vi_va_chuc_nang.md | Phạm vi, ranh giới hệ thống, yêu cầu có mã R-001…R-040 |
| 02_Quy_tac_va_trang_thai.md | Điều kiện chốt cân, công thức, đối soát, lỗi và chuyển trạng thái |
| 03_Du_lieu_va_tich_hop.md | Mô hình dữ liệu, hợp đồng sự kiện đề xuất, đối chiếu API cũ |
| 04_Man_hinh_phan_quyen_van_hanh.md | Màn hình, quyền, vận hành, chuyển đổi hệ thống |
| 05_Ke_hoach_30_ngay.md | Công việc, thứ tự phụ thuộc, cổng nghiệm thu và cutover |
| 06_Nguon_va_diem_can_chot.md | Truy vết tài liệu, các điểm khác nhau/chưa rõ, quyết định D-01…D-12 |
| 07_Prompt_cho_AI_Coding.md | Chỉ dẫn đầu vào cho AI hỗ trợ lập trình |
| tests/acceptance.feature | Kịch bản Given/When/Then để DEV triển khai kiểm thử |
| contracts/device-events.schema.json | JSON Schema đề xuất cho sự kiện thiết bị, không phải API sẵn có |
| contracts/device-events.examples.json | Dữ liệu giả minh họa để kiểm tra cấu trúc schema |
| config/workflow-profiles.example.json | Cấu hình mẫu; production_enabled=false, các ngưỡng chưa biết để null |
| requirements.json | Danh mục 40 yêu cầu ở dạng máy đọc được, cùng mã nguồn căn cứ |
| artifact_checks.json | Kết quả kiểm tra cấu trúc gói; phân biệt rõ với kiểm thử phần mềm/hiện trường chưa thực hiện |
| source_manifest.json | Tên và SHA-256 của các PDF nguồn; hai file QT06 trùng byte |

## Phân biệt căn cứ

- **U:** yêu cầu anh Ân đã xác nhận trong hội thoại; được ưu tiên khi khác tài liệu tham khảo.
- **S:** nội dung có trong các PDF, được dẫn bằng mã nguồn và trang PDF, không suy ra đây là cấu hình đang vận hành.
- **P:** thiết kế đề xuất để chuyển yêu cầu thành phần mềm; cần người phụ trách duyệt.
- **D:** thông tin chưa đủ để lập trình/đưa lên vận hành một cách xác định; không tự điền giá trị kinh doanh.

Mọi tên bảng, tên trạng thái, mã lỗi, schema và cấu trúc phần mềm trong gói là **P**, trừ khi có ghi rõ trường đó trích từ tài liệu tích hợp. Các mốc kỹ thuật chưa xác nhận không được xem là tính năng API hiện hữu.

## Định hướng đã chốt

Một nền tảng thay thế hoàn toàn AMMS, dùng chung quản lý chuyến và bộ máy cân. Cân mía và cân vật tư là các hồ sơ nghiệp vụ cấu hình trên cùng mô hình dữ liệu; không làm hai phần mềm riêng. FRM/ERP là nguồn kế hoạch, quy cách/BOM và chính sách. Hệ thống cân nhận số thực tế từ đầu cân, IoT và AI đếm bao; lưu bằng chứng, đối soát và gửi kết quả qua các luồng tích hợp hiện có. Không điều khiển bốc dỡ hay tự dừng băng chuyền. Có yêu cầu mở barie tự động khi xác thực đúng xe/chuyến và đủ điều kiện. Gặp lỗi thì tạm ngưng, không chạy nghiệp vụ offline để bỏ qua lỗi.

## Cách sử dụng với AI coding

Đưa toàn bộ gói vào thư mục tài liệu của repository. Bắt đầu với 07_Prompt_cho_AI_Coding.md. Yêu cầu AI lập bảng R-ID → mã nguồn → kiểm thử, sau đó làm từng nhóm nhỏ. Cung cấp thêm hợp đồng API hiện hành, mẫu tín hiệu thiết bị và các quyết định D đã duyệt. Không đưa mật khẩu, token, dữ liệu cá nhân thật hoặc tài liệu nội bộ vào công cụ AI chưa được đơn vị cho phép.

Các kịch bản .feature chưa có step definitions và chưa được chạy trên hệ thống thật. Kiểm tra cấu trúc JSON của gói không thay thế kiểm thử đầu cân, camera, IoT, FRM, ERP, barie hay nghiệm thu hiện trường.
