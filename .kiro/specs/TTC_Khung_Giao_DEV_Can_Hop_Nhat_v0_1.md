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


---

# 01. SRS — Phạm vi và yêu cầu chức năng

## 1. Tóm tắt sản phẩm

Xây dựng **Hệ thống quản lý cân hợp nhất mía và vật tư**, thay thế hoàn toàn phần mềm cân hiện tại. Trọng tâm là ghi nhận trọng lượng chính xác từ thiết bị, giảm thao tác thủ công, xác thực chuyến, ghi nhận dữ liệu IoT/AI và đối soát với FRM/ERP.

Phần mềm không tính lại các định mức, giá, chính sách thanh toán của FRM/ERP; không điều khiển máy xúc, gầu ngoạm, bơm, silo hoặc băng chuyền. Việc phần mềm nhận số kg đã bốc hoặc số bao đi qua băng chuyền không mở rộng phạm vi sang điều khiển bốc dỡ.

**Nguyên tắc hợp nhất:** chung dữ liệu chuyến, phiếu, đầu cân, QR, kiểm tra, bằng chứng, đối soát, đồng bộ và phân quyền; khác nhau bằng cấu hình hồ sơ nghiệp vụ, bước bắt buộc và hệ thống đích. Hợp nhất phần mềm không có nghĩa mọi loại hàng cùng một công thức hoặc cùng một chuỗi bước.

## 2. Những yêu cầu đã xác nhận từ người phụ trách

| Mã | Nội dung |
|---|---|
| U-01 | Thay thế hoàn toàn hệ thống cân hiện tại |
| U-02 | Tài liệu cũ dùng tham khảo, không mặc định là hiện trạng đã kiểm chứng |
| U-03 | Gom cân mía và cân vật tư trên một nền tảng |
| U-04 | Ưu tiên cân chính xác và mức tự động hóa cao |
| U-05 | Tiếp tục QR; AI kiểm tra xe đúng vị trí; nhận kg từ IoT; AI đếm bao |
| U-06 | Xác nhận lộ trình biển số nhập tay trước, camera sau; câu trả lời chưa chứng minh ANPR đã sẵn sàng |
| U-07 | Giữ các quy tắc cân/ngoại lệ hiện hữu đã nêu; các điểm chưa rõ hoặc mâu thuẫn trong tài liệu vẫn cần quyết định |
| U-08 | Chỉ quan tâm phần cân, không quản lý điều khiển bốc dỡ |
| U-09 | FRM/ERP có BOM/quy cách; hệ thống cân cung cấp số thực tế để đối soát |
| U-10 | Phần mềm ghi nhận; mở barie tự động khi đúng xe/chuyến |
| U-11 | Giả định đã đủ thiết bị; giả định này không thay cho tài liệu giao tiếp và kiểm thử kết nối |
| U-12 | Tiếp tục FRM, ERP và các luồng API hiện có |
| U-13 | Gặp lỗi thì tạm ngưng |
| U-14 | Đầu ra thuận tiện cho DEV dùng AI hỗ trợ lập trình |
| U-15 | Dự án cần hoàn thành trong một tháng |

## 3. Ranh giới và quyền sở hữu dữ liệu

| Thành phần | Trách nhiệm trong giải pháp |
|---|---|
| FRM | Nguồn lệnh/kế hoạch mía, thông tin chủ mía, loại mía, chính sách và quy cách thuộc FRM; nhận kết quả theo hợp đồng hiện hành |
| ERP | Nguồn PO/SO/TO/TRIP/Shipment, dòng hàng, số thực xuất/thực nhập xác nhận, BOM/quy cách và chứng từ ERP |
| Hệ thống cân mới | Chuyến và quy trình tại nhà máy, QR, số cân thực tế, dữ liệu thiết bị, phiên đối soát, phiếu cân và tích hợp |
| Đầu cân | Nguồn trọng lượng cân xe; phần mềm không dùng ảnh hoặc BOM để thay số cân |
| Dịch vụ AI vị trí | Bằng chứng về vị trí xe trong vùng cân; không tự được xem là nhận diện danh tính xe |
| Dịch vụ AI đếm bao | Số bao qua điểm đếm trong phiên đã gán xe, chuyến và mã hàng |
| IoT tại kho | Số đo khối lượng hoặc bộ đếm thiết bị; phải khai báo rõ là khối lượng đo hay quy đổi |
| Bộ điều khiển barie | Thực thi yêu cầu mở và cơ chế an toàn tại chỗ; ứng dụng không bỏ qua cảm biến an toàn |
| Người giám sát | Xác nhận ngoại lệ có thẩm quyền, kiểm đếm ngoài hệ thống, xử lý sự cố và duyệt kết quả |

Nền tảng cân không trở thành nguồn chủ về đơn hàng hoặc BOM. Thay đổi chủ mía, loại mía, biển số kế hoạch hoặc đơn hàng phải theo quyền sở hữu của FRM/ERP và được ghi nhận phiên bản.

**Phần AI là một hạng mục phần mềm cần bàn giao:** nhóm phụ trách phải cung cấp chức năng kiểm tra vị trí và đếm bao, bằng tích hợp giải pháp đã có hoặc phát triển/hiệu chỉnh mô hình rồi kiểm thử. Giả định đủ camera và thiết bị không có nghĩa mô hình AI, dữ liệu đánh giá và đầu ra dịch vụ đã sẵn sàng. D-05 phải xác nhận hiện trạng này trong ba ngày đầu; không âm thầm đẩy trách nhiệm tạo AI ra ngoài dự án chỉ vì kiến trúc gọi đó là dịch vụ AI.

## 4. Hồ sơ nghiệp vụ dùng chung

### 4.1. Hai nhóm lõi trong phạm vi

| Hồ sơ | Nguồn | Vai trò cân lần 1 → lần 2 | Bước/đối soát đặc thù |
|---|---|---|---|
| Nhập mía | FRM | Cả xe có hàng → bì xe | Tạp chất bắt buộc trước cân lần 2; chữ đường có thể đến sau; chính sách FRM; ghép chủ mía |
| Nhập vật tư/hàng hóa | ERP | Cả xe có hàng → bì xe | Chứng từ và xác nhận nhập; so sánh trên cùng đơn vị/cơ sở khối lượng |
| Xuất vật tư/hàng hóa | ERP | Bì xe → cả xe có hàng | Xác nhận thực xuất tại kho; IoT; AI đếm bao cho hàng bao; quy cách và dung sai |

Nguồn nền: S02 mục 3, S03 mục 2–3, S04 mục 4–5. Quy tắc IoT, AI vị trí và AI đếm bao là yêu cầu mới U-05, không phải chức năng đã được chứng minh bởi PDF.

### 4.2. Các biến thể phải đưa vào danh mục rà soát thay thế

S04 còn có chuyển giữa hai nhà máy, chuyển trong nhà máy, xuất trả hàng, cân không qua ERP và một số trường hợp cân trên hai lần. Không tự xóa các biến thể này để rút tiến độ. Ngày 1 phải ký danh sách biến thể đang dùng và thuộc phạm vi thay thế. Hồ sơ đang vận hành tại nơi chuyển đổi chỉ được thay thế khi có luồng tương đương đã nghiệm thu, hoặc có quyết định loại khỏi phạm vi bằng văn bản.

Chuyển nội bộ trong một nhà máy theo S04 trang 55 là **kho nguồn → cân có hàng → kho đích → cân bì**, không bắt buộc qua cổng bảo vệ. Đây là lý do không đóng cứng mọi hồ sơ vào quy trình cổng → cân → kho → cân → cổng.

## 5. Danh mục yêu cầu chức năng

**Ghi chú trạng thái:** CORE = phần lõi phải triển khai theo yêu cầu đã chốt; PROFILE = phải giữ cho biến thể được duyệt trong D-01; CONTROL = kiểm soát kỹ thuật đề xuất cần duyệt, không phải trích nguyên văn quy trình cũ.

| ID | Yêu cầu và kết quả cần đạt | Căn cứ | Loại |
|---|---|---|---|
| R-001 | Một nền tảng, một mô hình chuyến, một lõi cân; cấu hình nhà máy, cổng, bàn cân, hồ sơ nghiệp vụ. Không nhân bản lõi cho mía/vật tư. | U-01, U-03; P | CORE |
| R-002 | Đồng bộ và tham chiếu danh mục FRM/ERP; quản lý cấu hình thiết bị, vị trí và quyền tại hệ thống cân. Lưu nguồn chủ cho từng dữ liệu. | U-09, U-12; S04 mục 12 | CORE |
| R-003 | Mỗi lượt vận chuyển có trip_id nội bộ duy nhất, liên kết nhiều chứng từ/dòng hàng khi được phép; giữ TRIP/Shipment nguồn riêng. Một biển số có nhiều chuyến theo thời gian. | S01 tr.5; S03 tr.16; P | CORE |
| R-004 | Phân biệt QR tạm, QR chuyến chính thức và QR xe nội bộ. QR tạm không đủ điều kiện cân chính thức. QR xe nội bộ phải được gán đúng chu kỳ chuyến đang hoạt động. | S01 tr.5; S02 tr.11–13; S04 tr.55–56; P | CORE/PROFILE |
| R-005 | Đăng tài, xếp lượt, ưu tiên từ nguồn và số đăng tài tăng tại server. Hồ sơ mía giữ phân loại xe container/xe tải/xe kéo. Hủy đăng tài trước cân vào có đăng nhập và gửi FRM theo quy tắc. | S02 tr.13,18 | CORE |
| R-006 | Đánh giá quyền qua cổng, yêu cầu mở barie và ghi phản hồi. Mỗi lần qua gắn đúng chuyến, đúng cổng/làn và bằng chứng xác thực. Không coi đã gửi lệnh là xe đã đi qua. | U-10; S03 tr.20; P | CORE |
| R-007 | Chốt cân tự động khi QR hợp lệ, đúng chuyến/bàn cân, vị trí AI hợp lệ, tín hiệu cân mới và ổn định liên tục 5 giây, các điều kiện bắt buộc đạt. Lưu ảnh/bằng chứng và bộ điều kiện tại thời điểm chốt. | U-04, U-05; S02 tr.17; P | CORE |
| R-008 | Ghi riêng lần cân, vai trò cả xe/bì xe, đơn vị gốc, giá trị kg và trọng lượng hàng. Không dùng ABS để che lỗi âm; không tự suy ra bì lịch sử thay cho lượt cân bắt buộc. | S02 mục 3.3,3.7; S03 mục 3.3,3.5; P | CORE |
| R-009 | Cân lại có tài khoản được cấp quyền, lý do, biên bản và liên kết lần cũ. Giữ toàn bộ lịch sử, chỉ một phiên bản có hiệu lực cho từng vai trò. | S02 tr.19; P | CORE |
| R-010 | Nhập tay số cân bằng quyền quản trị/giám sát được chỉ định, có lý do và nguồn MANUAL. Không biến chức năng này thành cách bỏ qua trạng thái lỗi hoặc thay số đo không rõ nguồn. | S02 tr.17,22; U-13; P | CORE |
| R-011 | Một máy trạm quản lý hai bàn cân, chọn đúng bàn theo chuyến/QR và ánh xạ đầu đọc; khóa phiên riêng từng bàn. Có chuyển sang một bàn khi bàn kia hỏng, sau khi tạm dừng và xác nhận cấu hình lại. | S02 tr.17,21; U-13; P | CORE |
| R-012 | Ghi nhận vào/ra kho hoặc điểm kiểm tra bằng QR; một chuyến có nhiều lần ghé kho. Chỉ ghi trạng thái và số thực tế, không điều khiển bốc dỡ. | S03 tr.8,14–15; U-08 | CORE |
| R-013 | Giữ cân tạp chất và/hoặc tích hợp kết quả tương đương: gắn mẫu với chuyến, hai số cân mẫu, tính tỷ lệ theo tài liệu; cảnh báo mẫu dưới ngưỡng 10 kg cấu hình; lịch sử lấy lại mẫu và nhập tay có quyền. | S02 tr.18–19; U-07 | CORE |
| R-014 | Tiếp nhận/ghi nhận chữ đường theo nguồn được duyệt. Cho phép kết quả đến sau cân lần 2; không mặc định thiếu chữ đường là lỗi chặn cân. Kết quả không hợp lệ giữ cờ không đồng bộ. | S02 tr.20–22 | CORE |
| R-015 | Gọi FRM lấy/kiểm tra chính sách tại lúc quét QR theo S02; phân biệt API lỗi, chính sách thiếu và chữ đường đang chờ. Trạng thái cho hoàn tất do hợp đồng nghiệp vụ FRM xác nhận. | S02 tr.22; S04 tr.20; D-09 | CORE |
| R-016 | Xe ghép nhiều chủ mía: giữ cân tổng, phân bổ theo tỷ lệ đã duyệt hoặc số lượng xác định và phần còn lại; giữ chứng từ thỏa thuận, chưa gửi kết quả phân bổ khi chưa được xử lý. | S02 tr.18 | CORE |
| R-017 | Tạo phiên nhận kg IoT gắn chuyến, kho, thiết bị và dòng hàng; hiểu rõ số đo cộng dồn hay phát sinh; xử lý lặp, reset bộ đếm và sự kiện đến muộn. | U-05; P | CORE |
| R-018 | Nhận đếm bao AI theo phiên, làn/băng chuyền, mã hàng và chiều đi; không đếm theo số khung hình, không đếm trùng do gửi lại; không tự nhận diện SKU nếu nhà cung cấp chưa có khả năng đó. | U-05; P | CORE |
| R-019 | Đọc BOM/quy cách có phiên bản và hiệu lực từ FRM/ERP. Giữ riêng số lượng kế hoạch, số xác nhận kho, số đếm AI và hệ số quy đổi. Thiếu quy đổi cần thiết thì chưa kết luận đối soát. | U-09; S04 tr.29; P | CORE |
| R-020 | Đối soát cân xe, IoT và số bao/quy cách trên cùng cơ sở. Lưu số đo độc lập, chênh lệch, dung sai, nguồn dung sai và kết luận PASS/MISMATCH/MISSING_DATA/NOT_APPLICABLE. | U-09; S03 tr.16; P | CORE |
| R-021 | Một TRIP nhiều Shipment: cân tổng và tách phiếu/phân bổ theo quy tắc đã duyệt; tổng phân bổ bảo toàn tổng thực tế. Phân bổ không được ghi nhãn là từng dòng đã đo riêng. | S03 tr.16–18; P | CORE |
| R-022 | Tích hợp ERP qua luồng hiện hành được xác nhận: nhận dữ liệu, gửi số cân/nhận hàng/Ship Confirm theo đúng hợp đồng. Receipt và xác nhận ERP vẫn do ERP xử lý. | S05 mục 1–6; U-12 | CORE |
| R-023 | Tích hợp FRM: tra kế hoạch/chính sách, cập nhật lượt cân/kết quả theo khả năng API hiện hành. Không tự tạo endpoint từ tên nghiệp vụ trong PDF. | S02 tr.17,18,22; S04 tr.92; U-12 | CORE |
| R-024 | Chống xử lý lặp ở QR, cân, sự kiện IoT/AI, đồng bộ và barie. Phân biệt gửi chưa thành công với đã thành công nhưng mất phản hồi. Lưu khóa tham chiếu và trạng thái từng thông điệp. | P, phục vụ U-04/U-13 | CONTROL |
| R-025 | Khi lỗi, tạm ngưng thao tác nghiệp vụ liên quan, giữ dữ liệu đã ghi an toàn, không chốt mới/mở cổng mới dựa trên dữ liệu không tin cậy. Chỉ phục hồi sau kiểm tra, xác nhận và xử lý giao dịch chưa rõ kết quả. | U-13; P; D-07 | CORE |
| R-026 | Quyền theo chức năng và nhà máy; mọi sửa/duyệt/nhập tay/cân lại/in lại/chuyển bàn/khôi phục có audit trước–sau, người, thời gian, lý do. Không xóa dấu vết số đo. | S04 tr.78–80,87; P | CORE |
| R-027 | In QR, phiếu cân tổng và phiếu theo Shipment/chủ mía; đánh dấu bản in lại, tạm hoặc chờ xử lý. Cấu hình mẫu và số bản in; giữ bố cục nghiệp vụ cần thiết từ tài liệu. | S01 tr.6,12; S02 tr.13,23; S03 tr.17–19 | CORE |
| R-028 | Dashboard xe trong nhà máy, chờ cân, cân lần 1 chưa lần 2; báo cáo lượt cân, tổng hợp, chênh lệch, chỉnh sửa, lỗi thiết bị và lỗi tích hợp. Lọc theo ngày/ca/nhà máy/loại hàng/đối tác/xe. | S04 tr.88–92; P | CORE |
| R-029 | Xe bị từ chối lấy hàng vẫn qua bước cân kiểm tra ra; xử lý net bằng 0 theo quy trình được duyệt; không tự hủy toàn bộ SO chỉ vì net bằng 0. Phải xác minh phạm vi hủy với ERP. | S03 tr.15; D-10 | PROFILE |
| R-030 | Hỗ trợ mô hình nhiều lượt cân/phân đoạn cho phế liệu, rác thải nếu thuộc phạm vi; không cố định cơ sở dữ liệu chỉ có hai cột cân. | S04 tr.37 | PROFILE |
| R-031 | Chuyển nội bộ, giữa nhà máy, xuất trả: giữ profile riêng trên lõi chung. Trả hàng phải đối chiếu lượng đã nhập và các lần trả trước theo dữ liệu nguồn đã duyệt. | S04 mục 6–8; P về lũy kế | PROFILE |
| R-032 | Cân không qua ERP là profile nghiệp vụ được cho phép, không phải chế độ chữa cháy khi ERP mất kết nối. Quyền sử dụng và số liệu báo cáo phải tách biệt. | S04 mục 9; U-13; P | PROFILE |
| R-033 | Có phương án thay thế và tra cứu dữ liệu cũ, xử lý chuyến đang dở, đối soát số phiếu và khóa gửi trùng. Không vận hành hai hệ thống cùng ghi chứng từ cho cùng một chuyến. | U-01; P | CORE |
| R-034 | Giữ test tín hiệu cân và import trình tự đăng tài–cân ra có quyền. Dữ liệu test không gửi ERP/FRM hoặc mở barie. Import phải xem trước và kiểm tra hợp lệ trước ghi. | S02 tr.17,22; P | CORE |
| R-035 | Bảo vệ tài khoản, bí mật tích hợp, dữ liệu ảnh và dữ liệu cá nhân; lưu trữ/sao lưu/khôi phục theo chính sách nội bộ được duyệt; phân tách môi trường test và vận hành. | S04 tr.75,78–80; P | CONTROL |
| R-036 | Định nghĩa và nghiệm thu năng lực tại hiện trường: đồng thời nhiều bàn, số chuyến, độ trễ, khả năng khôi phục, AI trong điều kiện thực tế. Chưa có thông số thì không cam kết ngưỡng không căn cứ. | U-04, U-15; D-01, D-04, D-05, D-12 | CONTROL |
| R-037 | Nếu cân lại hoặc điều chỉnh sau khi đã gửi FRM/ERP, không gửi lặp như giao dịch mới. Tạo yêu cầu điều chỉnh liên kết bản trước, theo API đã xác nhận; thiếu API thì giữ chờ xử lý. | S02 tr.19; P | CONTROL |
| R-038 | Phân biệt số lượng đơn đặt hàng với lượng giao/nhận của chuyến này. Xử lý một phần, nhiều Shipment và nhiều kho theo hợp đồng hiện hành; không mặc định Quantity của cả PO/SO là lượng trên xe. | S03 tr.8,16; S05 các luồng Receiving/Confirm; P | CORE |
| R-039 | QR và AI vị trí không đủ để chứng minh xe vật lý đúng danh tính. Khi biển số vẫn nhập tay, phải có xác nhận xe/chuyến tại làn hoặc nguồn nhận diện đã nghiệm thu trước auto-open. | U-05, U-06, U-10; S03 tr.13; P | CORE |
| R-040 | Sau khi kết thúc lượt xe, vẫn nhận được kết quả chữ đường hợp lệ/điều chỉnh có quyền mà không mở lại QR hoặc barie. Lịch sử nghiệp vụ và trạng thái xe tách biệt. | S02 tr.20–22; P | CONTROL |

## 6. Những việc không thuộc phạm vi

Không tạo hệ thống ERP/FRM mới; không lập trình lại công thức định mức, chính sách giá/thanh toán; không điều khiển lượng bốc/dỡ hoặc khóa băng chuyền; không tự xây phòng thí nghiệm/thuật toán phân tích chữ đường từ các thông số chưa được cung cấp; không mặc định mở rộng sang nhận diện khuôn mặt, tải trọng trục hoặc AI phân loại mọi hàng hóa.

Không mặc định triển khai ANPR ngay trong tháng đầu chỉ dựa trên chữ Đúng ở câu trả lời số 6. Module này có thể tích hợp khi có đầu ra đã nghiệm thu, nhưng chưa phải điều kiện được xác nhận đã sẵn sàng. D-03 quyết định cách xác thực đúng xe cho chế độ mở barie tự động.

## 7. Định nghĩa hoàn thành ở cấp sản phẩm

Hoàn thành không chỉ là có giao diện và API giả lập. Mọi profile thuộc danh sách D-01 phải đi được luồng bình thường và ngoại lệ đã duyệt trên thiết bị, API và dữ liệu kiểm thử thật tại địa điểm nghiệm thu; không mất/trùng cân, không nhầm chuyến, không cộng hai nguồn đo thành một lượng hàng, không mở cổng sai điều kiện. Phải có tài liệu vận hành, phân quyền, backup/restore và diễn tập chuyển đổi trước khi thay thế hoàn toàn.

Các ngưỡng sai số, AI, tốc độ và quy mô là đầu vào cần ký duyệt; không thể suy ra chỉ từ mục tiêu cân chính xác và thiết bị đã đủ. Những D chưa giải quyết phải xuất hiện trong báo cáo nghiệm thu, không được AI tự hợp thức hóa.


---

# 02. Quy tắc xử lý, đối soát và trạng thái

**Quy ước:** các mã B và tên trạng thái là thiết kế P. Quy tắc nghiệp vụ kế thừa được ghi nguồn S. Không coi các tham số chưa có giá trị là 0, không coi dữ liệu chưa có là đã đạt.

## 1. Luồng chung và các điểm kiểm tra

Luồng phổ biến cho xe qua cổng:

Đối chiếu kế hoạch → đăng tài/QR → xác thực xe và cho vào → cân lần 1 → ghi nhận các điểm kiểm tra/kho → cân lần 2 → đối soát → đồng bộ đủ điều kiện → cho ra và kết thúc chuyến.

Nguồn: S02 trang 9–12, S03 trang 8. Hệ thống mới bổ sung AI vị trí, IoT, AI đếm bao và barie theo U-05/U-10.

Mỗi chuyến có danh sách checkpoint theo profile, không dùng một cột bước hiện tại để thay mọi thông tin. Ví dụ kho có thể có nhiều lần ghé, cân có thể có nhiều phiên bản, chữ đường có thể có kết quả sau khi xe rời đi.

| Profile | Checkpoint cốt lõi theo thứ tự |
|---|---|
| Mía nhập | Đăng tài → vào cổng → cân có hàng → tạp chất → ghi nhận lấy mẫu chữ đường nếu có → xác nhận công đoạn nhập → cân bì → đối soát/chính sách → đồng bộ → ra cổng |
| Vật tư nhập | Đăng tài → vào cổng → cân có hàng → xác nhận nhập kho → cân bì → đối soát → đồng bộ → ra cổng |
| Vật tư xuất | Đăng tài → vào cổng → cân bì → phiên kho/IoT/đếm bao nếu áp dụng → cân có hàng → đối soát → đồng bộ → ra cổng |
| Nội bộ trong nhà máy | Gắn QR xe với chuyến → kho nguồn → cân có hàng → kho đích → cân bì → đối soát/đồng bộ → kết thúc; không tạo bước qua cổng giả |
| Trên hai lượt cân | Danh sách phân đoạn có hướng nhập/xuất và vai trò cân được phê duyệt; không mặc định lấy số lớn nhất trừ số nhỏ nhất |

Checkpoint lấy mẫu chữ đường và kết quả chữ đường là hai trạng thái khác nhau. Không ép phải có kết quả chữ đường trước cân lần 2. Profile chính thức cần phản ánh API FRM và quy trình hiện trường đã duyệt trong D-09.

## 2. Trạng thái được tách theo mục đích

### 2.1. Vòng đời nghiệp vụ của chuyến

| Trạng thái | Ý nghĩa | Chuyển tiếp bình thường |
|---|---|---|
| TEMP_REGISTERED | Có phiếu tạm, chưa đủ kế hoạch | REGISTERED sau kiểm tra nguồn; hoặc CANCELLED |
| REGISTERED | Có QR chính thức, profile và chứng từ đã xác nhận | IN_PROGRESS khi thực hiện checkpoint hợp lệ đầu tiên |
| IN_PROGRESS | Đang đi qua các checkpoint của profile | WEIGHING_COMPLETED khi đủ lượt cân có hiệu lực |
| WEIGHING_COMPLETED | Đã có bộ số cân hoàn chỉnh, chưa mặc định đạt đối soát | RECONCILED khi mọi so sánh bắt buộc đạt |
| RECONCILED | Đạt đối soát trên phiên bản đầu vào xác định | POSTED khi các thông điệp bắt buộc được xác nhận; hoặc CLOSED cho profile nội bộ không tích hợp được duyệt |
| POSTED | Hệ thống đích đã xác nhận các kết quả bắt buộc của phiên bản này | CLOSED khi đủ điều kiện kết thúc/ra cổng |
| CLOSED | Chuyến vật lý đã kết thúc | Không nhận cân/qua cổng mới từ QR chuyến này |
| CANCELLED | Chuyến bị hủy hợp lệ | Không thực hiện bước nghiệp vụ mới |

Ngoại lệ đã duyệt có thể tạo nhánh cân lại hoặc xử lý xe bị từ chối, nhưng không cập nhật trực tiếp trạng thái cuối bằng một nút bỏ qua. Việc tiếp nhận chữ đường sau CLOSED không mở lại vòng đời vật lý.

### 2.2. Trạng thái thực thi và tích hợp

`execution_status = ACTIVE | PAUSED` tồn tại độc lập với vòng đời chuyến. PAUSED giữ nguyên bước cuối, có issue_code, phạm vi ảnh hưởng, thời điểm, người xử lý. Không xóa lịch sử để quay lại bước đầu.

Từng phép đối soát có `PENDING | PASS | MISMATCH | MISSING_DATA | NOT_APPLICABLE`. NOT_APPLICABLE chỉ dùng khi profile nói không áp dụng, không dùng để che mất kết nối.

Từng thông điệp có `NEW | HELD | SENDING | ACKNOWLEDGED | FAILED | UNKNOWN | SUPERSEDED`. UNKNOWN là chưa biết hệ thống đích có ghi nhận hay chưa, không phải đồng nghĩa FAILED.

Các checkpoint có `PENDING | ACTIVE | DONE | REJECTED | WAIVED`. WAIVED chỉ dành cho quy tắc cho phép miễn có phê duyệt; không dùng để bỏ AI/đầu cân/API khi đang lỗi. Mọi chuyển trạng thái phải kiểm tra quyền, phiên bản bản ghi và điều kiện phía máy chủ.

## 3. Điều kiện chốt cân tự động

**B-001 — Một lần chốt cân là một quyết định có bằng chứng.** Với R-007, chỉ chốt khi đồng thời thỏa:

1. QR chính thức còn hiệu lực, đúng chuyến, đúng nhà máy, đúng lần/loại checkpoint; chuyến chưa CLOSED/CANCELLED và không PAUSED.
2. Bàn cân, đầu cân, camera và đầu đọc thuộc đúng làn đã cấu hình; phiên cân đang sở hữu độc quyền bàn đó.
3. Danh tính xe/chuyến đã được xác thực theo D-03; không dùng riêng AI vị trí để kết luận đúng biển số.
4. AI trả vị trí hợp lệ với bằng chứng còn mới và ngưỡng chất lượng được duyệt. INVALID hoặc UNKNOWN không đạt. Sai cấu hình vùng quan sát phải báo lỗi, không tự giả định vùng mới.
5. Tín hiệu đầu cân hợp lệ, không mất gói làm đứt cửa sổ đánh giá, không quá tải/báo lỗi thiết bị, nằm trong khả năng đo đã cấu hình; giá trị đủ mới theo D-04.
6. Trọng lượng ổn định liên tục 5 giây theo tiêu chí thiết bị/dao động đã duyệt. Nếu xe lệch vị trí hoặc tín hiệu mất ổn định, cửa sổ đánh giá phải bắt đầu lại.
7. Đủ các điều kiện nghiệp vụ trước lượt cân: đã có cân lần 1 khi cân lần 2; mía đã đo tạp chất; điều kiện chính sách FRM theo hợp đồng.
8. Lưu được sự kiện cân, dấu thời gian, bằng chứng, phiên bản quy tắc và bản ghi liên quan. Khi chưa chắc giao dịch lưu đã thành công, không phát sinh lượt mới bằng việc bấm lại.

Giá trị 5 giây lấy từ S02 trang 17 và S04 trang 26; tiêu chí dao động, tần suất mẫu, độ mới ảnh/tín hiệu và các ngưỡng AI chưa có trong tài liệu, cần D-04/D-05. Không tự đặt mọi đầu cân cùng ngưỡng ±1 kg.

**B-002 — Cân thực và số phần mềm:** phần mềm ghi đúng giá trị đầu cân được chốt. Kiểm tra phần mềm không chứng minh độ chính xác vật lý của bàn cân; nghiệm thu phải có kiểm tra thiết bị ở hiện trường. Không cộng/trừ bù để số cân khớp đơn hàng.

**B-003 — Cân hai bàn:** khóa phiên riêng từng bàn, ánh xạ QR/đầu đọc/bàn cân rõ ràng. Hai xe quét đồng thời phải sinh hai kết quả đúng hai bàn; không lấy trọng lượng của bàn đang hiển thị cuối cùng trên một màn hình. Bàn hỏng được tạm dừng và chuyển tuyến sau xác nhận, không tự gán số cũ của bàn hỏng sang bàn còn lại.

## 4. Trọng lượng và công thức

### 4.1. Trọng lượng xe

`first_weight_kg` và `second_weight_kg` luôn là thứ tự thời gian. `gross_vehicle_kg` và `tare_vehicle_kg` là vai trò đo, không suy ra cố định từ tên WeightIn/WeightOut của API cũ.

- Nhập mía/vật tư: `vehicle_net_kg = first_weight_kg - second_weight_kg`, lần 1 có hàng, lần 2 là bì.
- Xuất hàng: `vehicle_net_kg = second_weight_kg - first_weight_kg`, lần 1 là bì, lần 2 có hàng.
- Nội bộ hoặc nhiều phân đoạn: dùng vai trò/hướng đã khai báo cho từng phân đoạn; không dùng ABS để che trái chiều.

`vehicle_net_kg` là chênh lệch cân xe. Nếu bao, pallet hoặc vật dụng khác lên/xuống xe giữa hai lần cân thì số này có thể gồm chúng; không mặc định đó là khối lượng sản phẩm tinh. Hệ thống phải lưu cơ sở khối lượng trước khi so sánh.

Giá trị âm là ngoại lệ, không tự chuyển thành dương. Net bằng 0 chỉ hợp lệ trong các profile/trường hợp được duyệt, ví dụ xe bị từ chối lấy hàng; ngưỡng xét gần 0 phải được xác nhận, không lấy dung sai của hàng bao áp vào mọi xe.

Dùng số thập phân chính xác với độ phân giải theo thiết bị và đơn vị được duyệt. Lưu số gốc, đơn vị gốc và số quy đổi kg; không làm tròn trung gian để mất thông tin. Phân biệt **bì xe** với **bì bao bì/pallet**.

### 4.2. Cân tạp chất mía

Theo S02 trang 18:

`impurity_percent = (sample_before_kg - sample_after_kg) / sample_before_kg × 100`

Điều kiện số học đề xuất: sample_before_kg > 0; 0 ≤ sample_after_kg ≤ sample_before_kg. Mẫu đầu nhỏ hơn 10 kg là cảnh báo theo tài liệu, ngưỡng cấu hình, không tự chuyển thành lỗi cứng. Không khẳng định 10 kg là quy chuẩn pháp lý đang có hiệu lực.

S02 cho lưu mẫu 1 sau 3–5 giây ổn định, mẫu 2 sau khi lấy mẫu 1 ra và cân về 0; cân/lấy lại nhiều lần có quyền và lịch sử. Cửa sổ cho cân mẫu cần chốt riêng, không nhầm với 5 giây của cân xe. Công thức trọng lượng thanh toán, chữ đường hoặc khấu trừ thương mại không tự xây trong phần mềm cân nếu FRM chưa cung cấp hợp đồng.

### 4.3. Ghép chủ mía và tách Shipment

Với xe ghép, cân tổng giữ nguyên. Tổng các phần phân bổ phải bằng tổng lượng được phân bổ, mỗi phần liên kết một chủ mía/chứng từ/dòng hàng. Ghi `allocation_method`, tỷ lệ hoặc số lượng đã duyệt, phiên bản và người duyệt. Sai lệch làm tròn được xử lý bằng một dòng điều chỉnh minh bạch theo phương án được duyệt, không làm biến mất phần dư.

S02 trang 18 yêu cầu giữ chưa đồng bộ khi chưa xử lý phân bổ. S02 trang 17 cũng yêu cầu đồng bộ từng lượt cân: phải phân biệt sự kiện cân tổng với kết quả đã phân bổ và xác nhận D-09 xem API FRM cho phép gửi loại nào trước. Không âm thầm coi hai yêu cầu này giống nhau.

S03 trang 16 cho tách phiếu một TRIP nhiều Shipment theo tỷ lệ trọng lượng đơn. Đây là **phân bổ**, không phải đo riêng từng Shipment. Phiếu/DB phải thể hiện khác nhau; không cộng lại bì xe nhiều lần khi tổng hợp.

## 5. Nhận số từ IoT và AI đếm bao

**B-010 — Phiên đo bắt buộc:** trước nhận dữ liệu tính vào chuyến, tạo measurement_session liên kết site, warehouse, lane/conveyor, trip, document_line/item và thiết bị. Một nguồn đo vật lý không gán đồng thời cho hai chuyến. Các sự kiện chưa gán được phải vào vùng chờ xác minh; không tự gán theo biển số gần nhất hoặc theo giờ gần nhất.

**B-011 — Nhận kg IoT:** cấu hình rõ loại dữ liệu là DELTA hay CUMULATIVE. DELTA cộng các sự kiện duy nhất. CUMULATIVE lấy chênh giữa mốc cuối và mốc bắt đầu của cùng counter_epoch, không cộng mọi bản tin bộ đếm. Reset hoặc giảm bộ đếm không có nguyên nhân phải PAUSED/HOLD để xác minh, không tính `max(0, end-start)` rồi bỏ qua lượng thiếu.

IoT phải khai báo `mass_origin = MEASURED | DERIVED_FROM_COUNT` và `mass_basis`. Nếu IoT chỉ lấy số bao nhân trọng lượng quy cách thì đây không phải nguồn cân kg độc lập; không gọi nó là hai phép đo xác nhận lẫn nhau.

**B-012 — Đếm bao:** mỗi lần vượt vạch hợp lệ có sự kiện, định danh riêng, phiên và chiều. Đếm số lần chuyển hàng thực, không số khung hình. Với băng chuyền có chiều trả lại và đã nghiệm thu: net_bag_count = forward - reverse. Không tự suy ra bao rách là hai bao hay một kiện là một bao khi chưa có quy tắc.

Không tự giả định AI nhận diện mã hàng. Mã hàng được khóa bởi phiên kho hoặc do nguồn đã nghiệm thu cung cấp. Nếu đổi SKU, đổi chuyến, còn bao đang trên băng hoặc sự kiện đến muộn, phải khóa ranh giới phiên và kiểm tra đủ dữ liệu trước khi kết thúc; không đẩy phần chưa rõ sang chuyến sau.

**B-013 — Không đếm trùng:** khóa duy nhất tối thiểu là source_device_id + boot_session_id + sequence hoặc định danh tương đương được nguồn bảo đảm. Cùng ID, cùng nội dung: trả kết quả đã xử lý; cùng ID nhưng nội dung khác: cách ly và báo lỗi. Sự kiện đến sai thứ tự được lưu để đối chiếu, không dùng như trạng thái hiện tại. Khi đóng phiên, kiểm tra dải sequence/watermark hoặc xác nhận tổng cuối để phát hiện thiếu dữ liệu.

Mất camera hoặc IoT cần thiết dẫn tới tạm ngưng xử lý chuyến/phiên liên quan. Phần mềm này không gửi lệnh dừng băng chuyền; quy trình dừng việc bốc hàng thuộc vận hành. Hàng vẫn di chuyển trong thời gian mất tín hiệu phải được kiểm đếm/khôi phục số liệu có xác nhận trước khi tiếp tục.

## 6. Đối soát không trộn nguồn

Lưu độc lập tối thiểu:

| Số liệu | Ý nghĩa |
|---|---|
| vehicle_net_kg | Chênh lệch cân xe thực |
| iot_mass_kg | Khối lượng phiên IoT, kèm nguồn đo/quy đổi và cơ sở khối lượng |
| ai_bag_count | Số bao thực đếm qua camera trong phiên |
| warehouse_confirmed_quantity | Số thực xuất/nhập kho đã xác nhận trên ERP/FRM |
| planned_quantity | Số kế hoạch của chứng từ/chuyến; không thay số xác nhận thực tế |
| nominal_kg_per_unit | Khối lượng danh nghĩa theo BOM/quy cách có phiên bản |
| expected_kg | Số xác nhận liên quan × quy cách, là lượng quy đổi để đối soát |

Có thể tính `ai_nominal_kg = ai_bag_count × nominal_kg_per_unit`, nhưng phải hiển thị **kg quy đổi từ đếm bao**, không ghi thành kg đã cân. Không lấy trung bình ba nguồn; không cộng vehicle_net_kg + iot_mass_kg vì có thể là cùng một lô hàng được đo hai nơi.

Các phép so sánh đề xuất theo profile:

- Cân xe ↔ kg IoT, chỉ khi cùng phạm vi hàng và cơ sở khối lượng, IoT là nguồn đo phù hợp.
- Số bao AI ↔ số bao kho xác nhận, theo đúng SKU và quy cách.
- Cân xe ↔ khoảng khối lượng tương ứng số kho xác nhận và quy cách.
- Số thực tế chuyến ↔ kế hoạch/lượng còn lại trên nguồn, không mặc định phải bằng toàn bộ đơn hàng.

Nếu một bên là khối lượng sản phẩm tinh và bên kia gồm bao/pallet, chỉ được quy về cùng cơ sở bằng dữ liệu bao bì hợp lệ từ nguồn đã duyệt; thiếu dữ liệu thì MISSING_DATA, không tự cho qua hoặc trừ hằng số.

**Dung sai lịch sử cho hàng bao tại S03 trang 16:**

`lower_kg = min_kg_per_bag × reference_bag_count - scale_margin_kg`

`upper_kg = max_kg_per_bag × reference_bag_count + scale_margin_kg`

Trong tài liệu, scale_margin_kg = 20. Giữ đây là quy tắc tham khảo cho profile phù hợp, có phiên bản cấu hình. Không mặc định ±20 kg cho mọi vật tư, mía hoặc IoT. S04 trang 29 nói chưa có bảng dung sai vật tư khác. D-06 phải duyệt dung sai, cơ sở dùng số bao, và cách cộng nhiều SKU; BOM chỉ có khối lượng danh nghĩa chưa chắc có min/max.

**Ví dụ giả để kiểm tra thiết kế:** cân xe net 30.050 kg; IoT đo cùng cơ sở 30.040 kg; AI đếm 600 bao; ERP xác nhận 600 bao; quy cách danh nghĩa 50 kg/bao. Khi đó lệch cân–IoT = 10 kg; số bao lệch = 0; lượng danh nghĩa = 30.000 kg; lệch cân–danh nghĩa = 50 kg. Chưa có dung sai đã duyệt thì chưa kết luận PASS. Không đổi số cân xe thành 30.000 kg.

## 7. Lỗi, cảnh báo và quyền tiếp tục

| Tình huống | Hành vi yêu cầu/đề xuất |
|---|---|
| QR sai, tạm, đã hủy hoặc chuyến đã đóng | Không chốt cân/không mở barie nghiệp vụ; hiển thị lý do |
| Đúng QR nhưng xe vật lý khác | Không mở barie, không chốt cân; xác minh lại |
| AI vị trí INVALID/UNKNOWN, ảnh cũ hoặc mất nguồn | Không chốt mới; không dùng kết quả cũ |
| Tín hiệu cân mất ổn định, lỗi, quá tải hoặc cũ | Không chốt; reset cửa sổ ổn định khi cần |
| FRM/ERP lỗi kết nối | Tạm ngưng quy trình phụ thuộc; không tự chuyển sang profile không ERP |
| Đã gửi nhưng timeout | UNKNOWN; tra cứu/đối soát tham chiếu trước gửi lại; không tự tạo số phiếu khác |
| IoT reset, AI thiếu sự kiện hoặc gán sai chuyến | Tạm ngưng phiên liên quan và kiểm tra thực tế |
| Mía chưa đo tạp chất | Chặn cân lần 2 theo S02 trang 21 |
| Mía chưa có chữ đường, nguồn trả trạng thái chờ hợp lệ | Cảnh báo, vẫn cho cân lần 2 theo S02 trang 20–22; không coi là lỗi thiết bị/API |
| Chính sách FRM thiếu/không hợp lệ | Không hoàn tất theo trạng thái cho phép của FRM đã xác nhận trong D-09; không nhầm với thiếu chữ đường |
| Vượt dung sai | Giữ kết quả đã đo; chặn hoàn tất/gửi kết quả chính thức; kiểm đếm ngoài hệ thống rồi cân lại theo S03 trang 16 |
| Một bàn cân hỏng | Dừng bàn/luồng liên quan; giám sát có thể cấu hình chuyển sang bàn tốt sau xác nhận, không tự tiếp tục trên bàn lỗi |
| Nhập tay/cân lại | Bản ghi ngoại lệ có quyền và biên bản; không gỡ các điều kiện lỗi thiết bị/API đang hiệu lực |
| Máy in hoặc lưu ảnh lỗi | Phạm vi dừng theo mức bắt buộc của checkpoint được duyệt; không phát hành phiếu hoàn tất giả |

U-13 xác nhận dừng khi lỗi nhưng chưa xác định dừng toàn nhà máy hay chỉ làn/chuyến. D-07 phải chốt ma trận ảnh hưởng. Đề xuất: lỗi thiết bị cục bộ dừng luồng dùng thiết bị; lỗi dịch vụ dùng chung dừng mọi luồng phụ thuộc. Đây là P, không phải quyết định thay người vận hành.

## 8. Barie và khôi phục

Điều kiện auto-open: chuyến/QR còn hiệu lực + đúng danh tính xe tại làn + đúng hướng/cổng + đủ checkpoint theo giai đoạn + không PAUSED + bộ điều khiển sẵn sàng + điều kiện an toàn tại chỗ đạt.

Không coi QR là danh tính vật lý. Khi chưa có ANPR đã nghiệm thu, sử dụng xác nhận hiện thời của nhân viên tại làn, gắn phiên xe, thời gian và người xác nhận. Khi đó barie có thể tự mở sau xác nhận, nhưng không mô tả là vận hành hoàn toàn không người.

Lệnh mở có command_id duy nhất và thời hạn. Theo dõi REQUESTED/ACKNOWLEDGED/EXECUTED/FAILED/UNKNOWN/EXPIRED; mất phản hồi không tự phát lại sau khôi phục khi chưa kiểm tra trạng thái thiết bị và xe. Xác nhận xe đã qua phải dựa vào sự kiện qua cổng hoặc người có quyền, không dựa riêng vào ACK mở.

Khi lỗi, ngừng cấp quyền mở mới; không ra lệnh đóng cưỡng bức xuống xe đang ở dưới cần barie. Cảm biến an toàn, dừng khẩn cấp và mở khẩn cấp thuộc thiết kế thiết bị/quy trình nhà máy. Mở khẩn cấp không biến chuyến lỗi thành chuyến hoàn tất nghiệp vụ; phải có đối chiếu sau sự kiện.

Sau phục hồi: kiểm tra kết nối → xác minh vị trí xe/phiên và các số liệu → giải quyết message/command UNKNOWN → giám sát xác nhận tiếp tục → đánh giá lại đầy đủ điều kiện. Không tự chốt một số cân hoặc mở barie từ sự kiện cũ còn trong hàng đợi.


---

# 03. Mô hình dữ liệu và tích hợp

## 1. Kiến trúc đề xuất, không khóa công nghệ

P: chọn ứng dụng có các module rõ trách nhiệm, một lõi nghiệp vụ dùng chung, kèm lớp kết nối thiết bị tại nhà máy và adapter FRM/ERP. Team chọn ngôn ngữ, framework và cơ sở dữ liệu theo năng lực/hạ tầng đã có; gói này không ấn định phiên bản phần mềm hoặc tuyên bố tương thích khi chưa thử.

Các khối: giao diện vận hành → dịch vụ nghiệp vụ chuyến/cân → lưu trữ dữ liệu và bằng chứng; dịch vụ nghiệp vụ giao tiếp adapter FRM/ERP, gateway đầu cân/QR/IoT/barie và dịch vụ AI.

Không xây thêm bộ thiết kế workflow kéo-thả trong tháng này. Dùng các profile cấu hình có kiểm tra hợp lệ, phiên bản và người duyệt. Các chức năng đăng tài, cân và đối soát không viết lại thành hai nhánh ứng dụng độc lập cho mía/vật tư.

Gateway có thể giữ nhật ký sự kiện để phục hồi/đối chiếu, nhưng không được tự tiếp tục cân nghiệp vụ hoặc cho xe qua khi ứng dụng/nguồn bắt buộc đang lỗi. Giữ dữ liệu để tránh mất số không đồng nghĩa được chạy offline.

## 2. Thực thể dữ liệu cốt lõi

Tất cả là mô hình P, không phải bảng dữ liệu AMMS đang có. Mã nguồn ngoài lưu dạng chuỗi để giữ tiền tố và số 0 đầu; không suy ra PO/Shipment là số nguyên.

| Thực thể | Trường/quan hệ tối thiểu |
|---|---|
| Site, Gate, Lane, Weighbridge, Device | id, code, site_id, địa điểm/vùng, khả năng thiết bị, trạng thái, cấu hình và version; ánh xạ camera/QR/đầu cân/barie |
| WorkflowProfile | id, version, loại nghiệp vụ, hướng, checkpoint, vai trò cân, nguồn bắt buộc, quy tắc đối soát, hiệu lực, phê duyệt |
| Trip | trip_id, site_id, profile_id/version, source_system, reference_trip_no, vehicle_id, tractor_plate, trailer_plate nếu có, driver_ref, lifecycle, execution_status, version, created_at |
| ExternalDocument | id, source_system, organization_code, business_unit, type, reference_no, shipment_no, version/source_hash, status, raw_snapshot_ref |
| TripDocumentLine | trip_id, document_id, source_line_id, item_code, warehouse_id, planned_quantity/uom, confirmed_quantity/uom, remaining_quantity, movement_segment_id |
| QRToken | token_id, token_hash, loại TEMP/TRIP/INTERNAL_VEHICLE, trip_id hoặc vehicle_id, trạng thái, issued_at, revoked_at; không nhét BOM/số cân để sửa bằng QR |
| CheckpointEvent | id, trip_id, checkpoint_type, warehouse/lane, thời gian bắt đầu/kết thúc, người/nguồn xác nhận, phiên bản |
| WeighSession | id, trip_id, bridge_id, checkpoint/segment, vai trò GROSS/TARE, trạng thái, ownership/lock, bắt đầu/kết thúc |
| WeighEvent | id, session_id, revision, supersedes_id, raw_weight, raw_uom, normalized_kg, measured_at, source AUTO/MANUAL/IMPORT/TEST, device_id, stable_window, policy_version, evidence_refs, approval_ref |
| QualitySample | sample_id, trip_id, qr_ref, loại IMPURITY/CCS, số đo mẫu, kết quả, valid_flag, source, version, người/giờ nhận, sync_eligibility |
| MeasurementSession | id, trip_id, warehouse_id, conveyor/lane_id, item/line_refs, device_refs, mass_basis, counter_start/epoch, trạng thái, watermark và đóng phiên |
| DeviceEvent | event_id, device_id, boot_session_id, sequence, type, occurred_at, received_at, measurement/session_ref, payload, payload_hash, processing_status |
| BomSnapshot | source_system, item_code, source_version/effective_at, uom, nominal/min/max kg mỗi đơn vị nếu nguồn có, bì bao/pallet nếu có, mass_basis, snapshot_hash |
| Reconciliation | trip_id, revision, input_versions, từng phép so sánh, observed/expected, tolerance_policy_id/version, difference, result, resolved_by |
| Allocation | trip_id, measured_total_ref, document_line hoặc owner, allocated_kg, method, ratio, rounding_adjustment, approval_ref |
| IntegrationMessage | message_id, destination, operation, business_key, trip/revision, payload_hash/ref, InterfaceReference nếu có, status, attempts, response, external_doc_ref |
| GateDecision, GateCommand | trip_id, lane, direction, identity_evidence, conditions, decision; command_id, expiry, controller_feedback và pass_event_ref |
| Approval, AuditLog, Incident | loại hành động, actor, phạm vi, before/after, lý do, biên bản, timestamps; sự cố và xác nhận khôi phục |

Mã chuyến, sự kiện, lệnh barie và tham chiếu thông điệp phải duy nhất theo phạm vi được định nghĩa. Số phiếu hiển thị có thể là chuỗi tăng cấu hình tại server; không dùng nó thay khóa bất biến của dữ liệu.

## 3. Ràng buộc quan trọng

- Một bàn cân chỉ có một phiên chốt cân đang sở hữu tại một thời điểm. Một chuyến không được chốt đồng thời cùng checkpoint ở hai bàn.
- Một vai trò trong một phân đoạn chỉ có một phiên bản cân hiệu lực; bản cũ bất biến và được liên kết supersedes, không bị xóa.
- Không hoàn tất một lần cân thứ hai khi chưa có lần đầu hợp lệ; không ghép lần đầu của chuyến A với lần sau của chuyến B có cùng biển số.
- Cùng document_line có thể đi nhiều chuyến; phải lưu số lượng của chuyến này và liên kết với dữ liệu nguồn, không tự cộng trùng các bản đồng bộ kế hoạch.
- Tổng allocated_kg bằng tổng đủ điều kiện phân bổ sau quy tắc làm tròn đã duyệt. Trọng lượng xe tổng và trọng lượng phân bổ từng Shipment phải phân biệt trong dữ liệu/phiếu.
- Kết quả đối soát gắn phiên bản số cân, BOM, xác nhận kho và dữ liệu phiên. Thay đầu vào làm kết luận cũ hết hiệu lực, không giữ PASS cũ.
- Phiếu, sự kiện, audit và dữ liệu nguồn cũ có thể được tra cứu theo quyền; không dùng xóa cứng để sửa lỗi chứng từ.
- Giữ dấu thời gian nguồn và nhận; lưu thời gian chuẩn thống nhất, hiển thị theo múi giờ nhà máy được cấu hình. Không giả định đồng hồ mọi thiết bị luôn đồng bộ.

## 4. Hợp đồng sự kiện thiết bị đề xuất

Schema ở `contracts/device-events.schema.json` là **giao thức chuẩn hóa nội bộ đề xuất**, không khẳng định đầu cân/camera hiện có phát đúng JSON này. Adapter chịu trách nhiệm chuyển định dạng thật sang cấu trúc chuẩn sau khi test mapping.

Envelope gồm event_id, event_type, site_id, lane_id, device_id, boot_session_id, sequence, occurred_at, received_at, correlation_id và payload. Với IoT/đếm bao, measurement_session_id bắt buộc ở dữ liệu được áp dụng; dữ liệu chưa gán phải lưu vùng chưa xác minh. Khối lượng trong schema dùng chuỗi thập phân để không làm mất độ chính xác khi trao đổi; adapter ERP sẽ chuyển sang kiểu mà API hiện hành yêu cầu.

Các loại: SCALE_READING, POSITION_EVIDENCE, BAG_PASSAGE, IOT_MASS_READING, BARRIER_FEEDBACK. Các ngưỡng/chất lượng cần thiết do profile quyết định; schema kiểm tra kiểu dữ liệu không chứng minh kết quả AI đúng hoặc số cân hợp lệ về nghiệp vụ.

Đối với POSITION_EVIDENCE, lưu verdict VALID/INVALID/UNKNOWN, confidence, model_version, vùng quan sát version và evidence_ref. Không tự chuyển confidence null thành 1. Đối với đếm bao, verdict/chất lượng không đạt làm dữ liệu chưa đủ để chốt đối soát; không gán bằng 0 bao.

Bên nhận kiểm tra quyền gửi của từng thiết bị, mapping nhà máy/làn, trùng ID, payload hash, dải sequence và thời điểm. Không để client giao diện tự gửi trọng lượng AUTO hoặc tự gắn quyền ADMIN.

## 5. Tích hợp ERP theo tài liệu tham khảo

S05 là tài liệu thiết kế tháng 09/2021. Dưới đây là tên luồng và mã mẫu trong tài liệu, **không chứng minh endpoint/mã đang dùng hiện nay**. Không nhầm số thứ tự IF.SCA với TransactionTypeCode.

| Nhóm trong S05 | Tác dụng theo tài liệu | Mã/action trong payload mẫu | Trang PDF |
|---|---|---|---|
| IF.SCA.01 & 02 — Purchasing Order for Receiving | Nhận đơn mua, gửi thông tin cân để ERP tạo Receipt | 01 / RECEIVE | 7–15 |
| IF.SCA.03 — InterOrg Intransit Shipped | Lấy giao dịch vận chuyển nội bộ từ ERP cho hệ thống cân | Không suy ra mã từ số IF; phải kiểm tra hợp đồng xuất dữ liệu | 16–21 |
| IF.SCA.04 — InterOrg Intransit for Receiving | Gửi cân nhận giao dịch intransit | 03 / RECEIVE | 22–30 |
| IF.SCA.05 & 06 — Transfer Order for Ship Confirm | Gửi kết quả phục vụ xác nhận xuất chuyển | 04 / CONFIRM | 31–39 |
| IF.SCA.07 & 08 — Transfer Order for Receiving | Gửi kết quả nhận chuyển | 05 / RECEIVE | 40–47 |
| IF.SCA.09 & 10 — Sale Order for Ship Confirm | Gửi số cân phục vụ xác nhận xuất bán | 06 / CONFIRM | 48–56 |

S05 dùng mẫu REST gửi dữ liệu và SOAP `runReport` để lấy báo cáo trong phần mô tả tương ứng. Không biến câu API đã có thành giả định mọi luồng đều REST. Endpoint có `<host>` trong PDF là mẫu cấu hình, không phải địa chỉ đang hoạt động để DEV gọi ngay.

### 5.1. Các nhóm trường cần mapping

Giữ khả năng ánh xạ tên trường tham khảo: TransactionTypeName, TransactionTypeCode, ActionCodeRequest, TripNumber, ShipmentNumber, WeightIn, WeightOut, NetWeight, TotalWeightOut, TotalNetWeight, WeightNumber, OrganizationCode, BusinessUnit, InterfaceReference, Transporter, PlateNumber, DriverName, TruckOwner và các thông tin Vendor theo từng loại giao dịch.

Luồng Receiving có `lines` với các trường tham khảo ReferenceNumber, ReferenceLineNumber, ShipmentLineNumber, ItemNumber, Quantity, UnitOfMeasure và OrganizationCode. Các luồng có thể khác nhau; không tự áp một request duy nhất cho mọi operation.

### 5.2. Các điểm không được tự suy diễn

S05 trang 9–10 mô tả WeightIn là cân vào/cân bì trong phần Receiving; mẫu Receiving trang 13 có WeightIn lớn hơn WeightOut. S05 trang 56 mô tả TotalWeightOut là tổng cân vào. Các cách đặt tên/diễn giải chưa đủ để chốt mapping vai trò bì/cả xe cho mọi luồng. Phải chạy các ca nhận hàng và xuất hàng với số biết trước, so với dữ liệu ERP thực nhận, rồi khóa mapping riêng từng transaction type.

Payload mẫu có TransactionTypeCode dạng chuỗi 06 nhưng bảng mô tả ghi NUMBER; tên Sale/Sales và cách viết Status cũng có chỗ khác nhau. Mẫu response Ship Confirm trang 56 thiếu dấu phẩy sau ErrorMessage. Giữ đây là điểm cần kiểm chứng, không copy nguyên văn thành hợp đồng chạy thật, không âm thầm sửa rồi tuyên bố đó là API hiện hành.

HTTP 200 không tự đồng nghĩa nghiệp vụ thành công: parser phải xử lý payload thành công/lỗi thật đã thu thập. Lưu phản hồi gốc theo chính sách che dữ liệu nhạy cảm. Tài liệu không chứng minh API hỗ trợ idempotency bằng InterfaceReference, truy vấn trạng thái hay điều chỉnh; các khả năng này phải xác nhận D-02.

### 5.3. Quy tắc trao đổi

Đọc kế hoạch và BOM/khẳng định kho theo phiên bản; tạo snapshot. Chỉ gửi kết quả chính thức khi đủ quy tắc profile. Không dùng lượng BOM để thay số cân thực tế. Không tự lập thêm Receipt/Ship Confirm bên ngoài hợp đồng cũ vì có thể trùng nghiệp vụ ERP đã thực hiện.

Gửi nhiều Shipment phải có trạng thái theo từng Shipment. Một Shipment thất bại không được làm hệ thống cho rằng cả TRIP đã POSTED; cũng không gửi lại những Shipment đã thành công một cách mù quáng. Tổng mức hoàn thành chuyến dựa trên danh sách thông điệp bắt buộc và phiên bản đầu vào.

## 6. Tích hợp FRM

Theo S02: kế hoạch/lệnh đốn; số đăng tài theo loại xe; hủy trước cân vào; thông tin ưu tiên; đồng bộ lượt cân; kết quả tạp chất/chữ đường; xử lý chủ mía ghép; lấy chính sách tại quét QR. S04 trang 92 chỉ dẫn tài liệu FRM qua một liên kết bên ngoài bộ PDF; nội dung hợp đồng chi tiết đó chưa được cung cấp/đọc trong gói này.

Do đó không tạo endpoint, tên field, chữ ký xác thực hoặc cấu trúc phản hồi FRM giả. D-02/D-09 phải cung cấp request/response đã khử thông tin nhạy cảm cho từng thao tác, cách mapping trip/phiếu/lệnh đốn/chủ mía và kết quả điều kiện chính sách.

Phân biệt các dạng kết quả: lỗi kết nối; nghiệp vụ không hợp lệ; chính sách chưa có; chữ đường đang chờ; đã đo tạp chất; kết quả không hợp lệ không được gửi; đã nhận thông điệp trùng. Không ép mọi tình huống thành true/false.

## 7. Tính nhất quán và gửi lại

P: dùng sổ thông điệp bền vững/outbox cùng giao dịch lưu kết quả. Mỗi business operation có business_key gắn hệ thống đích, trip_id, loại kết quả, revision và chứng từ/dòng tương ứng. Đặc tả exact key phải tương thích với API hiện hành; không đổi khóa chỉ để gửi lại cho qua lỗi.

Khi timeout, giữ UNKNOWN. Phục hồi bằng tra cứu nếu API có, hoặc đối chiếu có người phụ trách xác nhận; chỉ gửi lại tự động khi hợp đồng đã chứng minh cơ chế chống trùng. Chính sách retry có kiểm soát không cho phép tiếp tục luồng xe trong lúc đang lỗi. Trạng thái ACTIVE chỉ được phục hồi sau kiểm tra.

Bản cân được thay sau khi đã POSTED cần operation điều chỉnh/hủy tương ứng được ERP/FRM duyệt. Nếu không có API đó, giữ bản điều chỉnh chờ xử lý, không ghi đè kết quả cũ và không gửi như đơn mới. In lại phiếu không tạo lại thông điệp gửi ERP.

## 8. Các API nghiệp vụ nội bộ cần định nghĩa

Đây là danh mục operation P, không phải URL hiện hữu. DEV có thể thể hiện thành REST/OpenAPI hoặc hợp đồng tương đương sau khi chọn stack.

| Operation | Đầu vào cốt lõi | Kết quả/lưu ý |
|---|---|---|
| LookupPlan | source, org/site, trip/document ref, vehicle | snapshot + trạng thái hợp lệ + lỗi phân biệt |
| RegisterTrip | profile, source refs, vehicle, driver | trip_id, QR, version hoặc phiếu tạm |
| ResolveQR | token, reader, checkpoint, current session | trip + allowed action; không tự chốt cân chỉ vì lookup thành công |
| OpenWeighSession | trip, bridge, checkpoint, expected trip version | session độc quyền |
| EvaluateAndCommitWeight | session, referenced device/evidence IDs, idempotency key | weight event hoặc lý do không đủ điều kiện; server quyết định |
| RequestReweigh / ApproveManualWeight | trip/checkpoint, reason, attachment, actor | revision và approval; không bỏ qua lỗi đang tồn tại |
| Open/CloseMeasurementSession | trip, kho, SKU/line, devices, counter baseline/final | phiên và kiểm tra đầy đủ dữ liệu |
| ReconcileTrip | trip, expected revision | so sánh + phiên bản đầu vào + blockers |
| SubmitResult / ReconcileDeliveryStatus | trip/revision, destination/operation | trạng thái từng message và chứng từ ngoài |
| EvaluateGate / IssueGateCommand | trip, lane/direction, identity proof, expected version | quyết định + command_id có hạn; không tự xác nhận xe đã qua |
| Pause / Resume | scope, reason, incident, actor | trạng thái + audit, đánh giá lại trước resume |

Mọi operation thay đổi dữ liệu phải chỉ rõ xác thực, quyền, kiểm tra version, chống lặp, lỗi hợp lệ, tác dụng phụ và tiêu chí nghiệm thu. Không để AI tạo endpoint xóa lịch sử cân hoặc patch trực tiếp POSTED/CLOSED mà không có workflow.


---

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


---

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


---

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


---

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
