# Đây là đặc tả kiểm thử cần DEV hiện thực step definitions, chưa phải kết quả đã chạy.
# Mọi số liệu trong kịch bản là dữ liệu giả. Ngưỡng minh họa không phải cấu hình production.
Feature: Hệ thống cân hợp nhất mía và vật tư

  Background:
    Given môi trường kiểm thử tách biệt với thiết bị điều khiển và chứng từ sản xuất
    And các điều kiện không được nêu là lỗi trong kịch bản đã đạt theo profile thử nghiệm

  @A-001 @R-001 @R-002
  Scenario: Hai nhóm hàng dùng chung lõi nhưng khác checkpoint
    Given một profile nhập mía từ FRM và một profile xuất vật tư từ ERP
    When tạo một chuyến cho mỗi profile
    Then cả hai dùng chung mô hình Trip và WeighEvent
    And chỉ profile mía yêu cầu checkpoint tạp chất

  @A-002 @R-003
  Scenario: Một biển số có hai chuyến tách biệt
    Given xe TEST-01 đã hoàn tất chuyến T1 và đăng ký chuyến T2
    When quét QR của T1 để cân cho T2
    Then hệ thống từ chối QR chuyến đã đóng
    And không dùng số cân của T1 cho T2

  @A-003 @R-004
  Scenario: Phiếu tạm không cho cân chính thức
    Given chuyến có QR tạm vì chưa có kế hoạch hợp lệ
    When quét QR tại bàn cân
    Then không phát sinh WeighEvent chính thức
    And giao diện chỉ rõ cần hoàn tất đăng tài từ nguồn

  @A-004 @R-005
  Scenario: Hủy đăng tài có kiểm soát trước cân vào
    Given một đăng tài mía chưa cân vào và người dùng có quyền hủy
    When xác nhận hủy với lý do
    Then QR bị thu hồi và hủy được ghi audit
    And thông điệp hủy FRM được tạo theo hợp đồng đã duyệt
    And không tái sử dụng số đăng tài đã hủy cho chuyến khác

  @A-005 @R-006 @R-039
  Scenario: Tự mở sau xác thực xe tại làn khi chưa có ANPR
    Given biển số được nhân viên tại làn xác nhận đúng chuyến trong phiên hiện tại
    And QR chính thức cùng điều kiện barie và nghiệp vụ đều hợp lệ
    When dịch vụ đánh giá quyền vào cổng
    Then phát đúng một command_id mở đúng làn
    And không ghi xe đã qua cổng chỉ từ phản hồi đã nhận lệnh

  @A-006 @R-006 @R-039
  Scenario: Đúng QR không bù được sai xe vật lý
    Given QR của T1 hợp lệ nhưng xe tại làn không phải xe được xác nhận cho T1
    When yêu cầu mở barie
    Then không phát lệnh mở
    And ghi lý do danh tính xe không khớp

  @A-007 @R-007
  Scenario: Chưa đủ thời gian ổn định
    Given trọng lượng ổn định liên tục mới được 4.9 giây
    When đánh giá chốt cân với yêu cầu 5 giây
    Then không lưu lượt cân chính thức

  @A-008 @R-007 @R-024
  Scenario: Đủ điều kiện và quét QR lặp chỉ chốt một lần
    Given trọng lượng và vị trí hợp lệ liên tục đủ 5 giây
    And QR đúng chuyến và đúng phiên cân
    When đầu đọc gửi cùng hành động quét ba lần
    Then chỉ có một WeighEvent chính thức cho quyết định đó
    And lần gọi lặp trả kết quả đã ghi mà không tạo phiếu mới

  @A-009 @R-007
  Scenario: Xe sai vị trí dù số cân đã ổn định
    Given đầu cân ổn định nhưng AI trả INVALID hoặc UNKNOWN
    When đánh giá chốt cân
    Then không chốt
    And khi xe vào lại đúng vị trí cửa sổ đánh giá được khởi tạo lại

  @A-010 @R-007 @R-025
  Scenario: Không dùng tín hiệu cuối đã cũ
    Given giá trị cân hoặc ảnh vượt ngưỡng tuổi dữ liệu của cấu hình thử đã duyệt
    When đánh giá điều kiện cân
    Then hiển thị nguồn dữ liệu cũ và không chốt
    And không lấy trạng thái VALID cũ để thay ảnh mới

  @A-011 @R-011 @R-024
  Scenario: Hai bàn cân quét cùng lúc
    Given T1 có 35000 kg trên bàn A và T2 có 12000 kg trên bàn B
    When hai đầu đọc quét QR gần như đồng thời
    Then T1 nhận 35000 kg từ A và T2 nhận 12000 kg từ B
    And không chia sẻ khóa phiên hoặc số cân giữa hai bàn

  @A-012 @R-008
  Scenario: Cân nhập có hàng trước và bì sau
    Given cân lần 1 của chuyến nhập là 35000 kg và lần 2 là 12000 kg
    When tính trọng lượng hàng
    Then vehicle_net_kg bằng 23000 kg
    And first_weight giữ vai trò GROSS và second_weight giữ vai trò TARE

  @A-013 @R-008
  Scenario: Cân xuất có bì trước và có hàng sau
    Given cân lần 1 của chuyến xuất là 12000 kg và lần 2 là 35000 kg
    When tính trọng lượng hàng
    Then vehicle_net_kg bằng 23000 kg
    And first_weight giữ vai trò TARE và second_weight giữ vai trò GROSS

  @A-014 @R-008
  Scenario: Không dùng trị tuyệt đối để che lỗi hướng cân
    Given chuyến nhập có lần 1 là 12000 kg và lần 2 là 35000 kg
    When áp công thức của profile nhập
    Then kết quả âm được báo ngoại lệ
    And không tự đổi thành 23000 kg để hoàn tất

  @A-015 @R-009 @R-026
  Scenario: Cân lại không ghi đè
    Given lượt cân cũ 35000 kg cần cân lại và có phê duyệt hợp lệ
    When chốt lượt mới 34980 kg
    Then số cũ vẫn tồn tại với liên kết superseded
    And chỉ phiên bản mới có hiệu lực cho đối soát mới
    And người duyệt và biên bản được truy vết

  @A-016 @R-010 @R-026
  Scenario: Nhập tay là ngoại lệ có nguồn rõ
    Given người có quyền nhập tay và biên bản hợp lệ trong quy trình không còn lỗi chặn
    When ghi số cân thủ công
    Then source bằng MANUAL và lưu người cùng lý do
    And dữ liệu không được gắn nhãn AUTO

  @A-017 @R-010 @R-025
  Scenario: Nhập tay không bỏ qua trạng thái lỗi
    Given đầu cân hoặc API bắt buộc đang lỗi và chuyến PAUSED
    When quản trị nhập một số kg bằng tay
    Then hệ thống không cho chuyển tiếp nghiệp vụ hoặc mở barie dựa vào số này
    And vẫn cần giải quyết sự cố và xác nhận phục hồi

  @A-018 @R-013
  Scenario: Mía chưa đo tạp chất bị chặn cân lần 2
    Given chuyến mía chưa có kết quả tạp chất hợp lệ
    When quét QR tại cân lần 2
    Then hệ thống nêu rõ thiếu tạp chất và không chốt cân lần 2

  @A-019 @R-014 @R-015
  Scenario: Chữ đường đang chờ không đồng nghĩa lỗi API
    Given tạp chất hợp lệ và FRM trả chính sách cho phép cùng trạng thái chữ đường đang chờ
    When cân mía lần 2 với đầy đủ điều kiện khác
    Then hệ thống cảnh báo chữ đường chưa có nhưng vẫn chốt số cân
    And không gán chữ đường bằng 0 hoặc tự coi API lỗi

  @A-020 @R-013
  Scenario: Công thức tạp chất và ngưỡng mẫu là hai quy tắc khác nhau
    Given mẫu trước 10 kg và sau loại tạp chất 9.5 kg
    When tính kết quả
    Then tỷ lệ tạp chất bằng 5 phần trăm
    And mẫu trước 9 kg trong một phép thử riêng chỉ cảnh báo dưới ngưỡng 10 kg theo chính sách thử

  @A-021 @R-013 @R-026
  Scenario: Lấy lại mẫu giữ lịch sử và điều kiện về 0
    Given mẫu 1 đã được lưu và cân chưa về 0
    When hệ thống nhận giá trị tiếp theo để lưu mẫu 2
    Then không tự nhận đó là mẫu 2
    And việc cân mẫu thêm theo ngoại lệ yêu cầu quyền và lưu lịch sử

  @A-022 @R-016 @R-023
  Scenario: Chủ mía ghép chưa phân bổ không gửi kết quả phân bổ
    Given xe có hai chủ mía và tổng cân hợp lệ nhưng chưa có thỏa thuận phân chia được duyệt
    When hoàn thành phần cân
    Then kết quả phân bổ giữ HELD
    And không tự chia đều hoặc gửi toàn bộ khối lượng cho cả hai chủ

  @A-023 @R-015
  Scenario: Chính sách FRM và chữ đường được kiểm tra riêng
    Given FRM trả chính sách chưa đủ theo hợp đồng thử và chữ đường đã có
    When yêu cầu hoàn tất chuyến
    Then chính sách thiếu vẫn chặn hoàn tất theo profile
    And không suy ra có chữ đường thì mọi điều kiện FRM đã đạt

  @A-024 @R-017
  Scenario: Bộ đếm kg cộng dồn dùng chênh lệch không cộng các bản tin
    Given phiên IoT có counter đầu 1000 kg và các bản tin 11000 kg rồi 31000 kg cùng epoch
    When đóng phiên với dữ liệu đầy đủ
    Then iot_mass_kg bằng 30000 kg
    And không bằng tổng các giá trị counter đã nhận

  @A-025 @R-017 @R-025
  Scenario: Bộ đếm IoT reset giữa chuyến
    Given counter giảm từ 31000 kg xuống 0 và đổi epoch chưa được xác minh
    When nhận bản tin mới
    Then phiên được giữ chờ kiểm tra
    And không bỏ qua lượng thiếu bằng cách ép hiệu số âm thành 0

  @A-026 @R-018 @R-024
  Scenario: Sự kiện bao gửi lặp
    Given một BAG_PASSAGE đã được nhận với cùng device boot_session sequence và nội dung
    When sự kiện được gửi lại năm lần
    Then số bao chỉ tăng một lần
    And bên gửi nhận được kết quả đã xử lý

  @A-027 @R-024 @R-025
  Scenario: Cùng định danh sự kiện nhưng khác nội dung
    Given một sự kiện đã lưu với payload_hash H1
    When cùng khóa sự kiện đến với payload_hash H2 khác H1
    Then hệ thống cách ly bản tin và tạo sự cố
    And không ghi đè bản cũ hoặc áp thêm số lượng

  @A-028 @R-018
  Scenario: Bao được trả ngược chiều
    Given profile đếm có chiều trả lại đã duyệt
    And trong phiên có 10 lượt bao đi ra và 1 lượt bao quay lại
    When tính số bao ròng
    Then ai_bag_count bằng 9
    And không đếm bao theo tổng số khung hình

  @A-029 @R-017 @R-018
  Scenario: Chưa gán chuyến hoặc mã hàng không được tự đoán
    Given sự kiện IoT hoặc đếm bao chưa có measurement_session hợp lệ
    When tiếp nhận bản tin
    Then lưu vào vùng chờ xác minh
    And không tự gán cho xe gần nhất hoặc suy ra SKU bằng một AI chưa được nghiệm thu

  @A-030 @R-017 @R-018 @R-025
  Scenario: Mất tín hiệu khi hàng vẫn đi qua
    Given phiên bị mất camera hoặc IoT bắt buộc giữa quá trình hàng di chuyển
    When thiết bị kết nối trở lại
    Then phiên chưa được kết luận đủ dữ liệu chỉ vì kết nối đã tốt
    And cần đối chiếu sequence watermark hoặc kiểm đếm có xác nhận phần mất

  @A-031 @R-017 @R-019 @R-020
  Scenario: IoT quy đổi từ bao không phải phép cân độc lập
    Given IoT báo kg bằng số bao nhân quy cách và mass_origin là DERIVED_FROM_COUNT
    When tạo báo cáo đối soát với AI đếm cùng nguồn bao
    Then báo cáo ghi rõ dữ liệu quy đổi
    And không tuyên bố có hai phép cân độc lập xác nhận nhau

  @A-032 @R-020
  Scenario: Hai nguồn khác cơ sở khối lượng
    Given cân xe gồm bao pallet nhưng IoT chỉ có khối lượng sản phẩm tinh
    And chưa có dữ liệu bì bao pallet được duyệt
    When đối soát khối lượng
    Then phép so sánh là MISSING_DATA
    And không tự trừ một hằng số để cho PASS

  @A-033 @R-019 @R-020
  Scenario: Thiếu BOM không biến số thực tế thành 0
    Given cân xe đã lưu hợp lệ nhưng thiếu quy cách cần thiết cho phép đối soát hàng bao
    When hoàn tất đối soát
    Then giữ số cân thực tế và đánh dấu MISSING_DATA
    And không kết luận PASS hoặc đặt expected_kg bằng 0

  @A-034 @R-002 @R-019 @R-020
  Scenario: Thay đầu vào làm kết luận cũ hết hiệu lực
    Given đối soát đã PASS theo BOM phiên bản B1 và lượng kho Q1
    When có điều chỉnh hợp lệ được chọn áp dụng sang B2 hoặc Q2
    Then phải tạo phiên bản đối soát mới
    And lịch sử PASS cũ vẫn lưu nhưng không đại diện cho đầu vào mới

  @A-035 @R-012 @R-038
  Scenario: Một chuyến lấy một phần ở nhiều kho
    Given SO có 1000 bao nhưng chuyến hiện tại được xác nhận lấy 400 bao qua hai kho
    When cả hai checkpoint kho hoàn tất với tổng 400 bao
    Then đối soát chuyến dùng 400 bao chứ không phải 1000 bao
    And giữ liên kết xác nhận riêng của từng kho

  @A-036 @R-021
  Scenario: Phân bổ nhiều Shipment bảo toàn tổng
    Given net của xe là 30000 kg và phương án chia đã duyệt 40 phần trăm cùng 60 phần trăm
    When phân bổ và in hai phiếu Shipment
    Then hai lượng phân bổ là 12000 kg và 18000 kg
    And cả hai được ghi là phân bổ từ cân tổng
    And báo cáo tổng không cộng bì xe hai lần

  @A-037 @R-020
  Scenario: Kiểm tra đúng biên dung sai profile lịch sử đã duyệt
    Given profile thử có 100 bao với min 49.9 kg max 50.1 kg và biên cân 20 kg
    When tính khoảng khối lượng hợp lệ
    Then cận dưới là 4970 kg và cận trên là 5030 kg
    And không tự áp khoảng này cho profile vật tư khác chưa có dung sai

  @A-038 @R-020 @R-009
  Scenario: Vượt dung sai giữ số đo và chờ kiểm đếm
    Given số cân hợp lệ nằm ngoài khoảng dung sai đã duyệt
    When đánh giá hoàn tất
    Then kết quả là MISMATCH và chưa gửi kết quả chính thức
    And giữ nguyên số đo để kiểm tra ngoài hệ thống rồi cân lại có quyền

  @A-039 @R-022 @R-024
  Scenario: HTTP thành công nhưng nghiệp vụ ERP lỗi
    Given API trả HTTP 200 với payload lỗi nghiệp vụ theo hợp đồng thử
    When adapter xử lý phản hồi
    Then message không có trạng thái ACKNOWLEDGED
    And chuyến không thành POSTED chỉ từ mã HTTP

  @A-040 @R-024 @R-025
  Scenario: Timeout sau khi ERP đã ghi nhận
    Given ERP đã tạo chứng từ nhưng phản hồi bị mất
    When hệ thống cân nhận timeout
    Then message là UNKNOWN và chuyến phụ thuộc tạm ngưng
    And phải xác minh tham chiếu trước gửi lại
    And không tự tạo business_key hoặc số phiếu mới

  @A-041 @R-021 @R-022 @R-024
  Scenario: Một TRIP đồng bộ một phần
    Given TRIP có Shipment A đã thành công và Shipment B bị lỗi
    When tổng hợp trạng thái tích hợp
    Then TRIP chưa POSTED hoàn toàn
    And lần xử lý tiếp không gửi lại A một cách mù quáng

  @A-042 @R-027 @R-024
  Scenario: In lại phiếu không gửi lại giao dịch
    Given một phiếu cân đã đồng bộ thành công
    When người được quyền in lại hai bản
    Then chỉ tạo lịch sử in lại và đánh dấu bản in
    And không phát sinh cân Receipt hoặc Ship Confirm mới

  @A-043 @R-029
  Scenario: Xe bị từ chối lấy hàng không tự hủy toàn bộ SO
    Given xe bị kho từ chối và đã cân kiểm tra phù hợp quy tắc net bằng 0 được duyệt
    When xử lý đóng lượt giao
    Then hành động ERP chỉ nằm trong phạm vi hủy đã được D-10 xác nhận
    And thiếu hợp đồng hủy thì giữ chờ xử lý chứ không gọi lệnh hủy SO tự đoán

  @A-044 @R-037 @R-009
  Scenario: Cân lại sau khi đã gửi
    Given cân phiên bản W1 đã POSTED và phát sinh cân lại W2 được duyệt
    When xử lý đồng bộ bản mới
    Then giữ liên kết điều chỉnh W2 đến W1
    And không gửi W2 như giao dịch độc lập mới khi chưa có hợp đồng điều chỉnh

  @A-045 @R-004 @R-031
  Scenario: QR xe nội bộ dùng nhiều chu kỳ nhưng không trộn chuyến
    Given xe nội bộ có QR cố định và chuyến I1 đã kết thúc
    When bắt đầu chuyến I2 với kế hoạch mới được xác nhận
    Then I2 có trip_id riêng và liên kết đúng QR xe
    And không mang số cân hoặc trạng thái DONE của I1 sang I2
    And không yêu cầu bước qua cổng giả trong profile nội bộ

  @A-046 @R-032 @R-025
  Scenario: Không dùng profile không ERP để vượt sự cố ERP
    Given ERP đang mất kết nối cho chuyến bắt buộc ERP
    When người vận hành tìm cách chuyển chuyến sang không ERP để tiếp tục
    Then hệ thống từ chối cách chuyển nhằm bỏ qua lỗi
    And profile không ERP chỉ dùng cho nghiệp vụ riêng đã được duyệt

  @A-047 @R-030 @R-008
  Scenario: Nghiệp vụ trên hai lượt cân
    Given profile phế liệu đã duyệt với nhiều phân đoạn cân có vai trò rõ
    When ghi lượt thứ ba và tính từng phân đoạn
    Then dữ liệu lưu thêm WeighEvent theo phân đoạn
    And không ghi đè hai cột cân cũ hoặc lấy max trừ min làm mọi kết quả

  @A-048 @R-031
  Scenario: Chuyển nhà máy và xuất trả không lẫn nhau
    Given một chuyển giữa nhà máy có lượt xuất riêng và lượt nhận riêng
    And một nghiệp vụ xuất trả có lượng nhập gốc cùng các lần trả trước
    When hoàn tất từng nghiệp vụ
    Then lượt xuất không làm lượt nhận tự thành đã cân
    And lượng trả mới được kiểm tra với lượng còn được trả theo nguồn đã duyệt

  @A-049 @R-025 @R-011
  Scenario: Sự cố và phục hồi không chạy lại tự động
    Given bàn A hỏng và các luồng phụ thuộc đã PAUSED theo ma trận được duyệt
    When thiết bị hoạt động trở lại hoặc người có quyền chuyển sang bàn B
    Then phải xác minh lại xe phiên checkpoint và điều kiện cân trước tiếp tục
    And không tự phát cân hoặc mở cổng từ các sự kiện cũ

  @A-050 @R-006 @R-025
  Scenario: Lỗi không gây đóng barie cưỡng bức
    Given xe đang dưới cần barie và ứng dụng phát hiện lỗi
    When chuyển quy trình sang PAUSED
    Then ứng dụng ngừng cấp quyền mở nghiệp vụ mới
    And không ra lệnh đóng xuống xe để thực hiện chính sách dừng khi lỗi

  @A-051 @R-026 @R-035
  Scenario: Quyền được kiểm tra tại server và theo nhà máy
    Given người dùng chỉ có quyền đọc tại nhà máy A
    When gọi trực tiếp API nhập tay hoặc xem bằng chứng thuộc nhà máy B
    Then server từ chối theo quyền và phạm vi
    And không chỉ dựa vào việc nút bị ẩn trên giao diện

  @A-052 @R-028 @R-026
  Scenario: Báo cáo và audit truy ra dữ liệu gốc
    Given chuyến có cân lại nhập tay và đối soát chênh lệch
    When người có quyền mở báo cáo và chi tiết chuyến
    Then thấy phiên bản có hiệu lực cùng lịch sử số cũ người lý do và bằng chứng
    And tổng báo cáo không đếm lại các phiên bản bị thay thế

  @A-053 @R-034 @R-035
  Scenario: Chế độ test và import không tác động vật lý
    Given dữ liệu test hoặc file import đang ở bước xem trước
    When chạy kiểm tra hoặc in phiếu test
    Then không gửi chứng từ FRM ERP và không mở barie thật
    And lỗi định dạng thứ tự checkpoint hoặc trùng mã được báo trước khi import chính thức

  @A-054 @R-033
  Scenario: Chuyển chuyến đang dở có đầy đủ tham chiếu
    Given chuyến cũ đã cân lần 1 và một số thông điệp đã được FRM ERP xác nhận
    When chuyển đổi chuyến theo phương án đã duyệt
    Then giữ vai trò số cân thời gian nguồn ID cũ QR và trạng thái tích hợp
    And chỉ một hệ thống được quyền gửi kết quả tiếp theo

  @A-055 @R-033 @R-035
  Scenario: Rollback sau giao dịch đã POSTED
    Given hệ thống mới đã gửi chứng từ thành công trước quyết định quay lui
    When thực hiện runbook rollback thử nghiệm
    Then khóa bên gửi trước và đối soát sổ ACKNOWLEDGED cùng UNKNOWN
    And không khôi phục DB cũ rồi gửi lại cùng giao dịch như chưa từng tồn tại

  @A-056 @R-014 @R-040
  Scenario: Chữ đường hợp lệ đến sau khi xe đã ra
    Given chuyến mía CLOSED nhưng kết quả chữ đường đang chờ
    When nhận kết quả hợp lệ từ nguồn có thẩm quyền
    Then lưu phiên bản kết quả và đồng bộ theo hợp đồng
    And QR chuyến vẫn đóng cùng barie không mở lại

  @A-057 @R-036 @R-019
  Scenario: Cấu hình chưa duyệt không được bật production
    Given profile mẫu còn null ở tham số ổn định AI dung sai hoặc mapping bắt buộc
    When yêu cầu kích hoạt profile để vận hành thật
    Then validation chỉ rõ các giá trị và quyết định D còn thiếu
    And không thay null thành 0 hoặc bỏ phép kiểm tra để cho chạy

  @A-058 @R-036 @R-024
  Scenario: Tải đồng thời và restart không làm trùng số
    Given tập thử có số bàn chuyến và tần suất sự kiện được D-12 phê duyệt
    When chạy tải đồng thời và khởi động lại dịch vụ trong lúc xử lý
    Then đo độ trễ số lượng trùng mất và kết quả theo tiêu chí đã ký
    And không báo đạt nếu chỉ thử tuần tự một chuyến

  @A-059 @R-007 @R-018 @R-036
  Scenario: Nghiệm thu AI bằng điều kiện hiện trường
    Given bộ dữ liệu kiểm thử có nhãn độc lập bao gồm ngày đêm chói che khuất bao sát nhau và tình huống sai vị trí phù hợp hiện trường
    When đánh giá phiên bản model cùng cấu hình camera đưa vào vận hành
    Then báo lỗi đếm và cho phép sai theo từng nhóm tình huống với mẫu số rõ
    And không dùng confidence của model thay kết quả đánh giá có nhãn
    And các ca sai vị trí được quy định phải chặn không được tự mở điều kiện cân

  @A-060 @R-006 @R-024 @R-025
  Scenario: Mất phản hồi barie không phát lại lệnh cũ sau resume
    Given lệnh mở đã gửi nhưng trạng thái thực thi UNKNOWN và xe có thể đã đi qua
    When kết nối phục hồi sau khi lệnh hết hạn
    Then phải xác minh thiết bị cùng trạng thái xe
    And không tự phát lại lệnh mở hết hạn hoặc ghi thêm một lần qua cổng
