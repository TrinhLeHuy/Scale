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
