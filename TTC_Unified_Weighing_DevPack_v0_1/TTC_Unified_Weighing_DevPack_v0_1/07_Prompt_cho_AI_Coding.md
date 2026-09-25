# 07. Prompt giao AI hỗ trợ lập trình

Dùng nội dung dưới đây làm chỉ dẫn dự án, cùng toàn bộ bộ khung và các hợp đồng hiện hành được bổ sung. Đây không phải yêu cầu tạo hết phần mềm trong một lần sinh mã.

---

Bạn hỗ trợ DEV xây hệ thống cân hợp nhất mía và vật tư cho TTC, thay thế AMMS. Đọc README và các tệp 01–06 trước khi tạo code. Đọc tests/acceptance.feature, schema sự kiện và profile mẫu. Yêu cầu đã xác nhận của chủ dự án ưu tiên hơn tài liệu tham khảo; các điểm D chưa duyệt không được tự đặt thành quy tắc sản xuất.

## Ranh giới

Một lõi chung cho chuyến, QR, phiên cân, bằng chứng, checkpoint, đối soát, tích hợp và quyền. Mía/vật tư là profile cấu hình. FRM/ERP giữ quyền sở hữu kế hoạch/BOM/chính sách/chứng từ. Chỉ ghi nhận dữ liệu bốc hàng, không gửi lệnh điều khiển máy hoặc băng chuyền. Barie chỉ được yêu cầu mở khi điều kiện đầy đủ, không bỏ cảm biến an toàn.

Đọc API và giao thức thiết bị thực tế được cung cấp. Không tạo endpoint FRM/ERP từ trí nhớ hoặc từ tên mục của PDF. Những contract trong thư mục contracts là đề xuất nội bộ; cần adapter từ dữ liệu thật, không giả định thiết bị đã hỗ trợ.

## Những điều không được làm

Không dùng AI hoặc BOM để thay giá trị đầu cân. Không cộng kg cân xe với kg IoT cho cùng lô. Không dùng ABS để che trọng lượng net âm. Không hard-code cân lần 1 luôn là bì. Không dùng QR/AI vị trí như bằng chứng chắc chắn đúng danh tính xe. Không dùng cùng biển số làm khóa duy nhất chuyến. Không dùng hai cột cân duy nhất nếu profile cần nhiều lượt.

Không tự cho qua khi camera/IoT/API bắt buộc mất kết nối. Không chuyển nghiệp vụ sang không ERP khi ERP lỗi. Không ghi đè hoặc xóa số cân cũ. Không retry thao tác có tác dụng ghi chứng từ/mở barie khi kết quả chưa rõ. Không hard-code ±20 kg cho mọi loại hàng. Không suy diễn số bao nhân quy cách là khối lượng cân thực. Không chặn cân mía lần 2 chỉ vì chữ đường chưa có; vẫn kiểm tra tạp chất và chính sách riêng.

Không dùng mock để chứng minh đã tích hợp thiết bị thật. Không báo đã chạy kiểm thử nếu chỉ mới tạo test. Không điền null/TBD bằng số tùy ý để bật production. Không đưa secrets hoặc dữ liệu cá nhân thật vào repository/log/prompt.

## Kết quả trước khi viết code

1. Tóm tắt phạm vi/profile và mọi D ảnh hưởng đến nhóm công việc sắp làm.
2. Đề xuất cấu trúc module, database và migration theo stack đội DEV đã chọn; không tự đổi stack.
3. Lập bảng R-ID → module → operation → test; kiểm tra mâu thuẫn giữa nguồn và yêu cầu mới.
4. Viết hợp đồng nội bộ cần thiết, bao gồm quyền, kiểu dữ liệu, version, khóa chống lặp, lỗi và tác dụng phụ. Mapping API ngoài chỉ từ hợp đồng đã kiểm chứng.
5. Liệt kê điều kiện không đủ để triển khai production; tách rõ giả lập, test tích hợp và kiểm thử hiện trường.

## Cách triển khai từng gói

Làm từng work package của 05_Ke_hoach_30_ngay.md. Với mỗi gói: viết/hoàn thiện test trước hoặc cùng code, migration có phương án quay lui, kiểm tra quyền ở server, xử lý lặp và đồng thời, audit sự kiện quan trọng, log có correlation_id và che bí mật. Trạng thái phải theo 02_Quy_tac_va_trang_thai.md; UI không tự quyết định đã chốt cân/đã đồng bộ/được mở cổng.

Cung cấp patch/mã nguồn cùng bảng test đã chạy, lệnh chạy, kết quả và giới hạn. Tách test logic, test adapter với mẫu đã duyệt và test hiện trường. Các ví dụ JSON là dữ liệu giả; không dùng tổ chức/xe mẫu để gửi chứng từ thật.

## Trình tự ưu tiên

Khóa phạm vi/API/thiết bị → trip/QR/profile/quyền → lõi cân và trạng thái → FRM/ERP → AI/IoT/phiên kho → đối soát/ngoại lệ → barie → báo cáo/phiếu → kiểm thử toàn luồng → chuyển đổi.

Các nhánh có thể làm song song khi contract đã ổn định. Luôn giữ tối thiểu một luồng mía và một luồng vật tư để kiểm chứng lõi thật sự dùng chung. Không tạo hai bản sao nghiệp vụ rồi gọi đó là hợp nhất.

## Definition of Done cho mỗi chức năng

Có R-ID; hành vi bình thường và lỗi rõ; có kiểm tra quyền/phạm vi/version/chống lặp; có audit và mapping nguồn; có test với kết quả chạy thực; không còn mock trong đường đi production; người phụ trách duyệt phần liên quan. Chưa đạt một trong các điểm này thì báo trạng thái còn thiếu, không gắn hoàn thành chỉ vì code đã biên dịch.

---

**Gợi ý lần giao việc đầu:** yêu cầu AI chỉ rà soát yêu cầu và lập kế hoạch WP-01/WP-02 cùng các câu hỏi D-01/D-02/D-03/D-04. Chỉ sau khi người phụ trách duyệt các contract nền mới giao tạo migration, module và test của gói đầu.
