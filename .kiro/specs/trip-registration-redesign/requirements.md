# Requirements Document

## Introduction

Thiết kế lại module đăng tài (UI-02) của hệ thống cân hợp nhất TTC để phản ánh đúng quy trình thực tế tại nhà máy. Implementation hiện tại đã đúng kiến trúc kỹ thuật (Clean Architecture, EF Core, WinForms MVP) nhưng đơn giản hóa quá mức luồng nghiệp vụ: thiếu kiểm tra kế hoạch FRM/ERP, thiếu phân loại xe để xếp hàng, thiếu số thứ tự theo loại xe, thiếu luồng hủy đăng tài gửi FRM, thiếu lưới danh sách chuyến có bộ lọc, và phiếu đăng tài chưa đúng bố cục (1 form + 3 QR).

Phạm vi redesign tập trung vào Trip entity, RegisterTripHandler, ITripNumberGenerator, QrToken, TripRegistrationForm, và DashboardQueries liên quan đến đăng tài. Không thay đổi kiến trúc tổng thể hoặc các module cân, đối soát, tích hợp.

## Glossary

- **Hệ_thống**: Hệ thống cân hợp nhất TTC (phần mềm đăng tài tại cổng nhà máy)
- **FRM**: Hệ thống quản lý nông trường, nguồn kế hoạch mía, lệnh đốn, thông tin chủ mía
- **ERP**: Hệ thống hoạch định tài nguyên, nguồn PO/SO/TO/TRIP/Shipment cho vật tư
- **Nhân_viên_cổng**: Nhân viên điều phối tại cổng nhà máy, có quyền trip.register
- **Nhân_viên_kho**: Nhân viên kho có quyền đăng ký kế hoạch trên FRM/ERP
- **Tài_xế**: Người lái xe vào nhà máy
- **Biển_số**: Biển số xe đầu kéo hoặc xe tải, nhập tay tại giai đoạn hiện tại, camera sau theo U-06
- **Loại_xe**: Phân loại xe gồm Container, Xe_tải, Xe_kéo, dùng để xếp hàng riêng biệt
- **Số_thứ_tự**: Số đăng tài tự tăng tại server, tách riêng theo Loại_xe trong cùng nhà máy và ngày
- **Phiếu_đăng_tài**: Bản in chứa 1 form thông tin đăng tài cộng 3 mã QR cho 3 điểm quét khác nhau
- **QR_tạm**: QR phát khi xe chưa có trong kế hoạch, chỉ dùng tra cứu, không đủ điều kiện cân chính thức theo R-004
- **QR_chuyến**: QR chính thức phát khi xe đã được xác nhận trong kế hoạch FRM/ERP
- **Kế_hoạch**: Lệnh đốn (FRM) hoặc Shipment (ERP) đã được đăng ký cho biển số xe
- **Bàn_cân**: Trạm cân được gán cho chuyến khi xếp hàng
- **Kho**: Kho hàng được gán cho chuyến dựa trên kế hoạch nguồn
- **Lưới_chuyến**: Bảng danh sách chuyến đã đăng tài ở cuối form, có bộ lọc theo trạng thái

## Requirements

### Requirement 1: Kiểm tra biển số trong kế hoạch FRM/ERP

**User Story:** Là Nhân_viên_cổng, tôi muốn kiểm tra biển số xe trong kế hoạch FRM/ERP trước khi đăng tài, để xác định xe đã được đăng ký hay chưa và quyết định luồng xử lý phù hợp.

#### Acceptance Criteria

1. WHEN Nhân_viên_cổng nhập Biển_số và nhấn nút Kiểm tra, THE Hệ_thống SHALL gọi API tra cứu kế hoạch từ FRM và ERP để tìm lệnh đốn hoặc Shipment khớp với Biển_số đó trong ngày hiện tại
2. WHEN FRM hoặc ERP trả về kết quả khớp, THE Hệ_thống SHALL hiển thị danh sách kế hoạch tìm được gồm mã lệnh đốn hoặc Shipment, loại hàng, chủ mía hoặc khách hàng, kho đích, và nguồn dữ liệu FRM hoặc ERP
3. WHEN không tìm thấy kế hoạch nào khớp Biển_số, THE Hệ_thống SHALL hiển thị thông báo xe chưa đăng ký trong kế hoạch và cho phép Nhân_viên_cổng chọn phát QR_tạm
4. IF API FRM hoặc ERP trả lỗi hoặc không phản hồi trong thời gian cấu hình, THEN THE Hệ_thống SHALL hiển thị trạng thái lỗi kết nối cụ thể và không cho phép đăng tài chính thức cho đến khi tra cứu thành công
5. WHEN có nhiều kế hoạch khớp cùng Biển_số, THE Hệ_thống SHALL hiển thị danh sách để Nhân_viên_cổng chọn đúng kế hoạch cần gắn với chuyến

### Requirement 2: Phân loại xe và số thứ tự theo loại

**User Story:** Là Nhân_viên_cổng, tôi muốn phân loại xe theo loại (Container, Xe tải, Xe kéo) khi đăng tài, để hệ thống xếp hàng riêng biệt và cấp số thứ tự đúng theo từng loại xe.

#### Acceptance Criteria

1. THE Hệ_thống SHALL yêu cầu Nhân_viên_cổng chọn Loại_xe (Container, Xe_tải, Xe_kéo) trước khi hoàn tất đăng tài cho hồ sơ mía nhập
2. WHEN đăng tài thành công, THE Hệ_thống SHALL cấp Số_thứ_tự tự tăng tại server, tách riêng theo Loại_xe trong cùng nhà máy và cùng ngày vận hành
3. THE Hệ_thống SHALL đảm bảo Số_thứ_tự không trùng lặp ngay cả khi nhiều trạm đăng tài đồng thời, sử dụng cơ chế sequence tại database
4. WHEN hồ sơ nghiệp vụ không phải mía nhập, THE Hệ_thống SHALL không bắt buộc chọn Loại_xe nhưng vẫn cho phép ghi nhận nếu có
5. THE Hệ_thống SHALL hiển thị Số_thứ_tự trên Phiếu_đăng_tài và trong mã QR_chuyến

### Requirement 3: Đăng tài xe có trong kế hoạch

**User Story:** Là Nhân_viên_cổng, tôi muốn đăng tài nhanh cho xe đã có trong kế hoạch, để xe được xếp hàng tự động và đi thẳng đến bàn cân hoặc khu chờ.

#### Acceptance Criteria

1. WHEN Nhân_viên_cổng chọn kế hoạch đã tìm được và nhấn Lưu phiếu (F4), THE Hệ_thống SHALL tạo Trip với Lifecycle bằng Registered, gắn thông tin kế hoạch từ nguồn FRM/ERP gồm mã lệnh đốn hoặc Shipment, loại hàng, chủ mía, giống mía, vụ mùa, trạm, hình thức thu hoạch
2. WHEN đăng tài thành công, THE Hệ_thống SHALL tự động xếp hàng cho chuyến dựa trên Loại_xe và quy tắc ưu tiên từ kế hoạch nguồn, gán Bàn_cân và Kho đích
3. WHEN đăng tài thành công, THE Hệ_thống SHALL phát QR_chuyến chính thức chứa Số_thứ_tự, mã Bàn_cân được gán, mã Kho đích
4. WHEN đăng tài thành công, THE Hệ_thống SHALL in Phiếu_đăng_tài gồm 1 form thông tin đăng tài và 3 mã QR
5. THE Hệ_thống SHALL lưu toàn bộ thông tin đăng tài với audit log gồm người thực hiện, thời gian, correlation_id, và phiên bản hồ sơ nghiệp vụ

### Requirement 4: Đăng tài xe chưa có trong kế hoạch (QR tạm)

**User Story:** Là Nhân_viên_cổng, tôi muốn phát QR tạm cho xe chưa đăng ký trong kế hoạch, để xe có thể vào khu chờ trong khi nhân viên kho đăng ký bổ sung trên FRM/ERP.

#### Acceptance Criteria

1. WHEN Biển_số không có trong kế hoạch và Nhân_viên_cổng chọn phát QR tạm, THE Hệ_thống SHALL tạo Trip với Lifecycle bằng TempRegistered và phát QR_tạm không chứa Số_thứ_tự
2. THE Hệ_thống SHALL in phiếu vào cổng tạm với QR_tạm, ghi rõ trạng thái chưa đăng ký kế hoạch
3. WHEN Nhân_viên_kho đăng ký kế hoạch cho Biển_số trên FRM/ERP và Hệ_thống nhận được xác nhận, THE Hệ_thống SHALL tự động chuyển Trip sang Lifecycle bằng Registered, thu hồi QR_tạm, phát QR_chuyến mới chứa Số_thứ_tự và thông tin xếp hàng
4. WHEN QR_chuyến mới được phát, THE Hệ_thống SHALL in Phiếu_đăng_tài chính thức mới gồm 1 form và 3 QR để Tài_xế đến trạm in lấy phiếu mới
5. THE Hệ_thống SHALL giữ QR_tạm ở trạng thái Revoked sau khi phát QR_chuyến, đảm bảo QR_tạm không thể dùng cho thao tác cân chính thức theo R-004

### Requirement 5: Hủy đăng tài

**User Story:** Là Nhân_viên_cổng, tôi muốn hủy đăng tài cho chuyến chưa bắt đầu cân, để xử lý các trường hợp xe rời đi hoặc đăng tài sai thông tin.

#### Acceptance Criteria

1. WHILE Trip có Lifecycle là TempRegistered hoặc Registered và chưa bắt đầu cân lần 1, THE Hệ_thống SHALL cho phép Nhân_viên_cổng hủy đăng tài
2. WHEN Nhân_viên_cổng yêu cầu hủy, THE Hệ_thống SHALL yêu cầu đăng nhập xác nhận và lý do hủy trước khi thực hiện
3. WHEN hủy đăng tài được xác nhận, THE Hệ_thống SHALL chuyển Trip sang Lifecycle bằng Cancelled, thu hồi tất cả QR liên quan bằng cách đặt Status thành Revoked, và giải phóng Số_thứ_tự khỏi hàng chờ
4. WHEN Trip có nguồn từ FRM, THE Hệ_thống SHALL gửi thông báo hủy đến FRM theo API đã xác nhận, ghi trạng thái gửi và xử lý trường hợp FRM không phản hồi theo quy tắc UNKNOWN của R-024
5. WHILE Trip đã có WeighSession ở trạng thái Committed, THE Hệ_thống SHALL từ chối hủy đăng tài và hiển thị lý do chuyến đã bắt đầu cân
6. THE Hệ_thống SHALL ghi audit log đầy đủ cho thao tác hủy gồm người hủy, thời gian, lý do, trạng thái gửi FRM, và correlation_id

### Requirement 6: Form đăng tài với đầy đủ trường thông tin

**User Story:** Là Nhân_viên_cổng, tôi muốn form đăng tài hiển thị đầy đủ thông tin cần thiết theo quy trình thực tế, để tôi có thể kiểm tra và xác nhận thông tin chuyến trước khi lưu.

#### Acceptance Criteria

1. THE Hệ_thống SHALL hiển thị form đăng tài với các trường: Cổng, Biển_số kèm nút Kiểm tra, Số đăng tài tự sinh, Lệnh đốn hoặc Shipment, Ngày giờ, Loại_xe, Loại mía, Giống mía, Chủ mía, Hình thức thu hoạch, Trạm, Vụ mùa, và Ghi chú
2. WHEN kế hoạch FRM/ERP được chọn, THE Hệ_thống SHALL tự động điền các trường Lệnh đốn hoặc Shipment, Loại mía, Giống mía, Chủ mía, Hình thức thu hoạch, Trạm, Vụ mùa từ dữ liệu nguồn mà Nhân_viên_cổng không phải nhập tay
3. THE Hệ_thống SHALL hiển thị các nút thao tác Lưu phiếu (F4), In phiếu (F5), Xác nhận (F6), Làm mới (Esc) với phím tắt tương ứng
4. THE Hệ_thống SHALL hiển thị panel thông báo ghi nhận các sự kiện trong phiên làm việc gồm kết quả kiểm tra biển số, trạng thái đăng tài, lỗi kết nối
5. THE Hệ_thống SHALL hiển thị thanh trạng thái ở cuối form với trạng thái kết nối Server, trạng thái barcode reader, trạng thái LPR Camera, trạng thái Camera chung

### Requirement 7: Lưới danh sách chuyến với bộ lọc

**User Story:** Là Nhân_viên_cổng, tôi muốn xem danh sách tất cả chuyến đã đăng tài trong ngày với các bộ lọc, để theo dõi tình trạng xe trong nhà máy và quản lý phiếu đã hủy.

#### Acceptance Criteria

1. THE Hệ_thống SHALL hiển thị lưới danh sách chuyến ở phần dưới form đăng tài, gồm các cột Số đăng tài, Trip number, Shipment number, Biển số, Số thứ tự, Tài xế, Thời gian vào, Thời gian ra, Nhà vận tải, Khách hàng hoặc NCC, Hàng hóa
2. THE Hệ_thống SHALL cung cấp các tab lọc nhanh: Trong nhà máy cho xe đã vào chưa ra, Đã ra cho xe đã ra cổng, Phiếu đã hủy cho chuyến Cancelled
3. THE Hệ_thống SHALL cung cấp bộ lọc theo khoảng ngày để xem chuyến ngoài ngày hiện tại
4. WHEN Nhân_viên_cổng chọn một dòng trong lưới, THE Hệ_thống SHALL điền thông tin chuyến đó lên form phía trên để xem chi tiết hoặc thao tác tiếp như in lại phiếu hoặc hủy
5. THE Hệ_thống SHALL tự động làm mới lưới khi có chuyến mới được đăng tài, hủy, hoặc thay đổi trạng thái mà không yêu cầu Nhân_viên_cổng bấm refresh thủ công

### Requirement 8: Nội dung mã QR và phiếu đăng tài

**User Story:** Là Nhân_viên_cổng, tôi muốn phiếu đăng tài có đúng bố cục 1 form cộng 3 QR và mã QR chứa đủ thông tin cần thiết, để Tài_xế có thể quét QR tại các điểm khác nhau trong luồng.

#### Acceptance Criteria

1. THE Hệ_thống SHALL tạo Phiếu_đăng_tài gồm 1 phần form thông tin chứa Số đăng tài, Biển_số, Loại_xe, Loại hàng, Chủ mía hoặc Khách hàng, Ngày giờ, Bàn_cân, Kho và 3 mã QR riêng biệt
2. THE Hệ_thống SHALL mã hóa trong QR_chuyến các thông tin token bảo mật dạng hash, Số_thứ_tự, mã Bàn_cân được gán, mã Kho đích; QR_tạm chỉ chứa token bảo mật và không chứa Số_thứ_tự
3. WHEN in lại phiếu bằng F5, THE Hệ_thống SHALL đánh dấu bản in lại với nhãn BẢN IN LẠI và ghi audit log cho thao tác in lại theo R-027
4. THE Hệ_thống SHALL hỗ trợ cấu hình mẫu in phiếu và số bản in theo từng nhà máy
5. IF máy in không sẵn sàng hoặc gặp lỗi in, THEN THE Hệ_thống SHALL ghi nhận đăng tài thành công vì dữ liệu đã lưu, thông báo lỗi in, và cho phép in lại sau mà không cần đăng tài lại

### Requirement 9: Xếp hàng tự động theo loại xe

**User Story:** Là Nhân_viên_cổng, tôi muốn hệ thống tự động xếp hàng cho chuyến theo loại xe sau khi đăng tài, để xe được điều phối đúng thứ tự đến bàn cân.

#### Acceptance Criteria

1. WHEN đăng tài thành công với Lifecycle bằng Registered, THE Hệ_thống SHALL tự động thêm chuyến vào hàng chờ cân, phân nhóm theo Loại_xe gồm Container, Xe_tải, Xe_kéo
2. THE Hệ_thống SHALL gán Bàn_cân cho chuyến dựa trên cấu hình bàn cân khả dụng và quy tắc phân bổ theo nhà máy
3. WHEN chuyến bị hủy hoặc chuyển sang Cancelled, THE Hệ_thống SHALL xóa chuyến khỏi hàng chờ và cập nhật thứ tự các chuyến còn lại
4. THE Hệ_thống SHALL cho phép Nhân_viên_cổng xem hàng chờ hiện tại theo từng Loại_xe, hiển thị Số_thứ_tự, Biển_số, thời gian chờ, Bàn_cân được gán
5. IF không có Bàn_cân khả dụng, THEN THE Hệ_thống SHALL vẫn đăng tài thành công nhưng đặt chuyến vào hàng chờ chưa gán bàn, và thông báo cho Nhân_viên_cổng

### Requirement 10: Tích hợp camera tại cổng

**User Story:** Là Nhân_viên_cổng, tôi muốn xem hình ảnh camera vào/ra tại cổng trực tiếp trên form đăng tài, để đối chiếu xe thực tế với thông tin đăng tài.

#### Acceptance Criteria

1. THE Hệ_thống SHALL hiển thị hai khung hình camera trên form đăng tài gồm Camera Vào và Camera Ra, lấy luồng từ camera đã cấu hình cho cổng hiện tại
2. WHEN đăng tài thành công, THE Hệ_thống SHALL chụp và lưu ảnh từ Camera Vào tại thời điểm đăng tài, gắn ảnh với Trip để làm bằng chứng
3. IF camera không khả dụng hoặc mất kết nối, THEN THE Hệ_thống SHALL hiển thị trạng thái mất kết nối camera trên khung hình tương ứng và vẫn cho phép đăng tài mà không chặn vì thiếu camera
4. THE Hệ_thống SHALL hiển thị trạng thái camera Online hoặc Offline trên thanh trạng thái của form

### Requirement 11: Cập nhật Trip entity cho quy trình thực tế

**User Story:** Là nhà phát triển, tôi muốn Trip entity lưu đầy đủ thông tin theo quy trình thực tế, để hỗ trợ toàn bộ luồng đăng tài mới bao gồm loại xe, xếp hàng, và thông tin kế hoạch nguồn.

#### Acceptance Criteria

1. THE Hệ_thống SHALL lưu Loại_xe dưới tên VehicleType trên Trip entity với các giá trị Container, Truck, Trailer; cho phép null khi hồ sơ không bắt buộc phân loại
2. THE Hệ_thống SHALL lưu Số_thứ_tự dưới tên QueueSequenceNumber trên Trip entity, riêng biệt với TripNo; Số_thứ_tự phản ánh vị trí trong hàng chờ theo Loại_xe
3. THE Hệ_thống SHALL lưu thông tin gán bàn cân AssignedWeighbridgeId và kho đích AssignedWarehouseId trên Trip entity khi xếp hàng
4. THE Hệ_thống SHALL lưu các trường thông tin mía mở rộng gồm SugarcaneType cho loại mía, SugarcaneVariety cho giống mía, SugarcaneOwner cho chủ mía, HarvestMethod cho hình thức thu hoạch, Station cho trạm, CropSeason cho vụ mùa; tất cả lấy từ FRM khi có kế hoạch
5. THE Hệ_thống SHALL lưu GateId cho cổng đăng tài trên Trip entity để xác định nơi đăng tài
6. THE Hệ_thống SHALL duy trì khả năng tương thích ngược với các trường mới cho phép null, các Trip đã tạo trước redesign vẫn hoạt động bình thường
