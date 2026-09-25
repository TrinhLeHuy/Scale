# Requirements Document

## Introduction

Tích hợp luồng cân end-to-end cho hệ thống cân hợp nhất TTC, bao gồm các module chưa được xử lý bởi spec trip-registration-redesign. Phạm vi bao gồm: luồng vào/ra kho (MeasurementSessionForm redesign), cải tiến luồng cân (QR scan → auto-load trip → save → sync ERP), luồng cổng ra (xác minh hoàn thành trước khi cho xe ra), luồng đo tạp chất tự động, luồng chữ đường, đồng bộ ERP (manual sync, auto-retry), in phiếu cân, xử lý ghép xe (split shipment), và cân lại sau sự cố.

Hệ thống hiện có đã implement các handler nghiệp vụ cốt lõi (EvaluateAndCommitWeightHandler, GateHandlers, QualityHandlers, MeasurementSessionHandlers, ReconcileTripHandler, AllocateTripHandler, SubmitTripResultHandler, ExceptionHandlers) và các entity domain (Trip, WeighSession, WeighEvent, QualitySample, MeasurementSession, Reconciliation, Allocation). Spec này tập trung vào tích hợp các module này thành luồng liền mạch trên UI và bổ sung các nghiệp vụ chưa có.

## Glossary

- **Hệ_thống**: Hệ thống cân hợp nhất TTC (phần mềm cân, kho, cổng tại nhà máy mía)
- **FRM**: Hệ thống quản lý nông trường, nguồn kế hoạch mía, lệnh đốn, thông tin chủ mía
- **ERP**: Hệ thống hoạch định tài nguyên, nguồn PO/SO/TO/TRIP/Shipment cho vật tư
- **Nhân_viên_cân**: Nhân viên vận hành bàn cân, có quyền weighing.weight.commit
- **Nhân_viên_kho**: Nhân viên kho, có quyền warehouse.session.open và warehouse.session.close
- **Nhân_viên_cổng**: Nhân viên điều phối cổng, có quyền gate.evaluate và gate.passage.confirm
- **Nhân_viên_chất_lượng**: Nhân viên đo tạp chất/chữ đường, có quyền quality.impurity.record
- **Quản_lý_ca**: Người có quyền phê duyệt thao tác ngoại lệ (nhập tay, cân lại, sync thủ công)
- **Tài_xế**: Người lái xe, mang phiếu đăng tài có QR Code qua các điểm quét
- **QR_chuyến**: Mã QR chính thức trên phiếu đăng tài, chứa token bảo mật + số thứ tự + bàn cân + kho
- **QR_tạm**: Mã QR tạm thời phát khi chưa có kế hoạch, không đủ điều kiện cân chính thức
- **Bàn_cân**: Trạm cân vật lý, kết nối đầu cân qua IScaleSignalGateway, một bàn chỉ có một phiên đang sở hữu tại một thời điểm
- **Phiên_cân**: WeighSession — phiên sở hữu độc quyền bàn cân, ghi nhận lượt cân
- **Lượt_cân**: WeighEvent — bản ghi bất biến của một lần chốt cân, cân lại tạo bản mới liên kết supersedes_id
- **Phiên_kho**: MeasurementSession — phiên đo tại kho, nhận dữ liệu IoT và đếm bao
- **Phiếu_cân**: Phiếu in sau khi hoàn thành cân lần 2, chứa thông tin cân vào/ra, trọng lượng hàng, tạp chất
- **Tạp_chất**: Tỷ lệ tạp chất mía tính theo công thức (mẫu trước - mẫu sau) / mẫu trước × 100
- **Chữ_đường**: Chỉ số CCS (Commercial Cane Sugar), có thể đến sau khi xe đã ra cổng
- **Ghép_xe**: Trường hợp một chuyến vận chuyển mía thuộc nhiều chủ mía, cần phân bổ trọng lượng theo tỷ lệ
- **Dung_sai**: Ngưỡng chênh lệch cho phép giữa các nguồn đo, phải có chính sách đã duyệt (D-06)
- **UNKNOWN**: Trạng thái thông điệp tích hợp chưa rõ kết quả, phải tra cứu trước khi gửi lại
- **Checkpoint**: Điểm kiểm soát trong luồng nghiệp vụ (Registration, GateIn, FirstWeight, ImpuritySample, SecondWeight, WarehouseIn, WarehouseOut, GateOut)

## Requirements

### Requirement 1: Luồng cân lần 1 — Quét QR tự động load chuyến

**User Story:** Là Nhân_viên_cân, tôi muốn quét QR trên phiếu đăng tài để hệ thống tự động nạp thông tin chuyến và mở phiên cân, để tôi chỉ cần giám sát số cân và nhấn lưu khi đủ điều kiện.

#### Acceptance Criteria

1. WHEN Nhân_viên_cân quét QR_chuyến tại bàn cân, THE Hệ_thống SHALL giải mã QR, tra cứu Trip theo token hash, hiển thị thông tin chuyến gồm Số đăng tài, Biển số, Loại xe, Khách hàng, Hàng hóa, Kho đích, Shipment, và tự động mở Phiên_cân trên bàn cân tương ứng
2. WHEN Phiên_cân được mở thành công, THE Hệ_thống SHALL bắt đầu hiển thị số cân realtime từ đầu cân, cập nhật trạng thái ổn định theo thời gian thực, và hiển thị checklist các điều kiện chốt cân gồm QR hợp lệ, Vị trí AI, Cân ổn định, Điều kiện trước đã đủ
3. IF QR không hợp lệ hoặc đã hết hạn hoặc đã bị thu hồi, THEN THE Hệ_thống SHALL hiển thị thông báo lỗi cụ thể gồm mã lỗi và lý do, không mở Phiên_cân, và giữ form ở trạng thái chờ quét QR mới
4. IF QR thuộc loại QR_tạm, THEN THE Hệ_thống SHALL từ chối mở phiên cân chính thức và hiển thị thông báo xe chưa đăng ký kế hoạch, cần quay lại cổng để lấy QR_chuyến
5. WHEN đã quét QR và mở phiên, THE Hệ_thống SHALL kết nối đúng bàn cân được gán trong QR_chuyến, tự động chuyển đổi giữa 2 bàn cân kết nối cùng PC dựa trên thông tin bàn cân trong QR
6. WHEN Nhân_viên_cân nhấn Lưu phiếu (F5), THE Hệ_thống SHALL gọi EvaluateAndCommitWeightHandler tại server để đánh giá toàn bộ điều kiện chốt cân và ghi Lượt_cân nếu đạt; UI không tự tính toán điều kiện
7. IF chốt cân thất bại do chưa đủ điều kiện, THEN THE Hệ_thống SHALL hiển thị từng điều kiện chưa đạt với mã lỗi cụ thể để Nhân_viên_cân biết cần xử lý gì
8. WHEN chốt cân thành công cho cân lần 1, THE Hệ_thống SHALL cập nhật Trip.Lifecycle thành InProgress, đóng Phiên_cân, hiển thị trọng lượng đã chốt, và hướng dẫn Tài_xế đi đo tạp chất

### Requirement 2: Luồng đo tạp chất tự động tại trạm lấy mẫu

**User Story:** Là Nhân_viên_chất_lượng, tôi muốn quét QR mẫu và hệ thống tự động cân mẫu tạp chất, để giảm thao tác thủ công và đảm bảo chính xác số liệu.

#### Acceptance Criteria

1. WHEN Nhân_viên_chất_lượng quét QR tại trạm lấy mẫu, THE Hệ_thống SHALL tra cứu Trip và hiển thị thông tin chuyến mía gồm Biển số, Chủ mía, Loại mía, Giống mía
2. WHEN mẫu 1 (mẫu trước) được đặt lên cân mẫu, THE Hệ_thống SHALL tự động chờ tín hiệu ổn định trong khoảng 3 đến 5 giây rồi ghi nhận khối lượng mẫu trước (SampleBeforeKg)
3. WHEN mẫu 1 được lấy ra và mẫu 2 (mẫu sau khi loại tạp chất) được đặt lên cân, THE Hệ_thống SHALL tự động ghi nhận khối lượng mẫu sau (SampleAfterKg) sau khi ổn định
4. WHEN có đủ cả mẫu trước và mẫu sau, THE Hệ_thống SHALL tính tỷ lệ tạp chất theo công thức (SampleBeforeKg - SampleAfterKg) / SampleBeforeKg × 100 và ghi QualitySample với Type là Impurity
5. IF khối lượng mẫu trước dưới ngưỡng cấu hình theo profile, THEN THE Hệ_thống SHALL hiển thị cảnh báo mẫu dưới ngưỡng nhưng vẫn cho phép ghi kết quả vì đây là cảnh báo không phải lỗi cứng
6. IF khối lượng mẫu sau lớn hơn mẫu trước, THEN THE Hệ_thống SHALL từ chối ghi kết quả và hiển thị thông báo số đo không hợp lệ, cần lấy lại mẫu
7. THE Hệ_thống SHALL lưu lịch sử mọi lần đo, lấy lại mẫu tạo bản QualitySample mới liên kết SupersedesId đến bản trước, bản cũ giữ nguyên và hết hiệu lực
8. WHEN Nhân_viên_chất_lượng có quyền quality.impurity.manual, THE Hệ_thống SHALL cho phép nhập tay kết quả tạp chất với bắt buộc có lý do và phê duyệt

### Requirement 3: Luồng chữ đường (CCS)

**User Story:** Là Nhân_viên_chất_lượng, tôi muốn tiếp nhận kết quả chữ đường từ FRM hoặc nhập tay, để bổ sung thông tin chất lượng mía cho chuyến dù kết quả có thể đến sau khi xe đã ra cổng.

#### Acceptance Criteria

1. THE Hệ_thống SHALL tiếp nhận kết quả chữ đường cho chuyến ở mọi trạng thái Lifecycle trừ Cancelled, bao gồm cả chuyến đã Closed vì chữ đường có thể đến sau khi xe ra cổng
2. WHEN kết quả chữ đường được nhập, THE Hệ_thống SHALL ghi QualitySample với Type là Ccs, xác định Status dựa trên giá trị: Valid nếu CCS có giá trị dương, Pending nếu nguồn báo đang chờ, Invalid nếu giá trị thiếu hoặc âm
3. WHEN Nhân_viên_chất_lượng có quyền quality.ccs.receive, THE Hệ_thống SHALL cho phép nhập tay giá trị chữ đường với yêu cầu nguồn và phê duyệt tương ứng
4. THE Hệ_thống SHALL hiển thị cảnh báo trên màn hình cân lần 2 nếu chưa có kết quả chữ đường, nhưng không chặn thao tác cân vì thiếu chữ đường chỉ là cảnh báo theo quy tắc nghiệp vụ
5. THE Hệ_thống SHALL lưu lịch sử chữ đường với cơ chế revision, kết quả mới tạo bản mới và bản cũ hết hiệu lực

### Requirement 4: Luồng cân lần 2 — Kiểm tra điều kiện trước và đồng bộ FRM

**User Story:** Là Nhân_viên_cân, tôi muốn hệ thống kiểm tra đầy đủ điều kiện trước khi cho cân lần 2 và tự động gọi FRM khi quét QR, để đảm bảo quy trình đúng thứ tự và dữ liệu đồng bộ.

#### Acceptance Criteria

1. WHEN Nhân_viên_cân quét QR_chuyến tại bàn cân cho cân lần 2, THE Hệ_thống SHALL gọi API FRM để đồng bộ thông tin chuyến tại thời điểm quét QR, không phải tại thời điểm lưu
2. WHILE chuyến chưa có kết quả đo tạp chất hợp lệ (QualitySample loại Impurity với Status là Valid), THE Hệ_thống SHALL chặn cân lần 2 và hiển thị thông báo phải đo tạp chất trước
3. WHILE chuyến chưa có kết quả chữ đường, THE Hệ_thống SHALL hiển thị cảnh báo thiếu chữ đường nhưng vẫn cho phép cân lần 2
4. WHEN chốt cân lần 2 thành công, THE Hệ_thống SHALL tính trọng lượng hàng bằng chênh lệch giữa Gross và Tare theo công thức do hướng profile quyết định, hiển thị kết quả trên form
5. IF trọng lượng hàng vượt dung sai cấu hình, THEN THE Hệ_thống SHALL hiển thị cảnh báo yêu cầu kiểm tra và đếm hàng theo quy định nhà máy trước khi xác nhận
6. WHEN cân lần 2 thành công và đủ lượt cân theo chuỗi vai trò của profile, THE Hệ_thống SHALL tự động chuyển Trip.Lifecycle sang WeighingCompleted
7. WHEN chốt cân lần 2 thành công, THE Hệ_thống SHALL cho phép in Phiếu_cân và cho phép đồng bộ thủ công kết quả đến ERP bằng nút Sync trên form

### Requirement 5: In phiếu cân

**User Story:** Là Nhân_viên_cân, tôi muốn in phiếu cân sau khi hoàn thành cân lần 2 với đầy đủ thông tin theo quy định nhà máy, để Tài_xế có chứng từ trước khi ra cổng.

#### Acceptance Criteria

1. WHEN Nhân_viên_cân nhấn In phiếu (F6) sau khi cân lần 2 hoàn thành, THE Hệ_thống SHALL in phiếu cân chứa thông tin: Số phiếu cân, Số đăng tài, Biển số, Loại xe, Khách hàng hoặc chủ mía, Hàng hóa, Kho, trọng lượng cân lần 1, thời gian cân lần 1, trọng lượng cân lần 2, thời gian cân lần 2, trọng lượng hàng, tỷ lệ tạp chất nếu có
2. THE Hệ_thống SHALL cho phép chọn máy in và số bản in trước khi in, với cấu hình mặc định theo nhà máy
3. WHEN in lại phiếu cân, THE Hệ_thống SHALL đánh dấu phiếu là BẢN IN LẠI và ghi audit log cho thao tác in lại
4. IF máy in không sẵn sàng, THEN THE Hệ_thống SHALL thông báo lỗi in nhưng không ảnh hưởng đến dữ liệu cân đã lưu, và cho phép in lại sau
5. THE Hệ_thống SHALL hỗ trợ cấu hình mẫu phiếu cân riêng theo từng nhà máy, phân biệt phiếu mía nhập và phiếu vật tư

### Requirement 6: Luồng vào kho — MeasurementSessionForm redesign

**User Story:** Là Nhân_viên_kho, tôi muốn quét QR để xác nhận xe vào kho và theo dõi quá trình giao nhận hàng, để quản lý luồng xe trong kho và tạo dữ liệu đối soát.

#### Acceptance Criteria

1. WHEN Nhân_viên_kho quét QR_chuyến tại cổng kho, THE Hệ_thống SHALL tra cứu Trip, hiển thị thông tin chuyến, và ghi nhận CheckpointEvent loại WarehouseIn với thời gian bắt đầu
2. WHEN xe vào kho thành công, THE Hệ_thống SHALL cho phép Nhân_viên_kho mở Phiên_kho (MeasurementSession) để nhận dữ liệu IoT và đếm bao nếu quy trình yêu cầu
3. WHEN quá trình giao nhận hàng hoàn tất, THE Hệ_thống SHALL cho phép Nhân_viên_kho xác nhận hoàn thành và ghi nhận CheckpointEvent loại WarehouseOut với thời gian kết thúc
4. WHEN xe ra khỏi kho, THE Hệ_thống SHALL trả phiếu đăng tài cho Tài_xế và hướng dẫn đến bàn cân lần 2
5. THE Hệ_thống SHALL hiển thị danh sách xe đang trong kho và xe đã ra kho, cho phép lọc theo ngày và trạng thái
6. IF xe chưa hoàn thành giao nhận mà cần ra kho, THEN THE Hệ_thống SHALL yêu cầu Nhân_viên_kho xác nhận lý do và ghi CheckpointEvent với WaiverReason nếu được phê duyệt

### Requirement 7: Luồng cổng ra — Xác minh hoàn thành trước khi cho xe ra

**User Story:** Là Nhân_viên_cổng, tôi muốn hệ thống kiểm tra xe đã hoàn thành tất cả bước bắt buộc trước khi cho ra cổng, để đảm bảo không có xe rời nhà máy khi chưa cân xong.

#### Acceptance Criteria

1. WHEN Nhân_viên_cổng quét QR_chuyến tại cổng ra, THE Hệ_thống SHALL tra cứu Trip và hiển thị thông tin chuyến kèm mốc thời gian: thời gian vào cổng, thời gian cân lần 1, thời gian cân lần 2, thời gian ra cổng
2. WHEN Trip.Lifecycle đã đạt trạng thái Reconciled hoặc Posted hoặc Closed, THE Hệ_thống SHALL hiển thị thông báo xe đã hoàn thành cân và cho phép Nhân_viên_cổng xác nhận cho xe qua cổng
3. IF Trip chưa hoàn thành các checkpoint bắt buộc theo profile, THEN THE Hệ_thống SHALL từ chối cho xe ra cổng và hiển thị danh sách bước chưa hoàn thành
4. WHEN Nhân_viên_cổng xác nhận cho xe qua (F6), THE Hệ_thống SHALL gọi EvaluateGateHandler kiểm tra điều kiện, phát lệnh mở barie có thời hạn 30 giây, rồi gọi ConfirmVehiclePassageHandler ghi nhận xe đã qua
5. WHEN xe ra cổng thành công, THE Hệ_thống SHALL thu hồi phiếu đăng tài QR từ Tài_xế, chuyển Trip.Lifecycle sang Closed nếu chưa, và ghi audit log
6. THE Hệ_thống SHALL hiển thị danh sách xe trong nhà máy và xe đã ra cổng trên lưới bên dưới form, với tab lọc và bộ lọc ngày
7. IF có lệnh mở barie đang ở trạng thái Unknown hoặc Requested chưa hết hạn cho chuyến, THEN THE Hệ_thống SHALL từ chối phát lệnh mới và yêu cầu giải quyết lệnh treo trước

### Requirement 8: Đồng bộ ERP — Manual sync và auto-retry

**User Story:** Là Nhân_viên_cân, tôi muốn đồng bộ kết quả cân đến ERP/FRM sau khi cân xong, với khả năng gửi lại khi gặp lỗi, để đảm bảo dữ liệu nhất quán giữa hai hệ thống.

#### Acceptance Criteria

1. WHEN cân lần 2 hoàn thành và đối soát đạt, THE Hệ_thống SHALL cho phép Nhân_viên_cân bấm nút Sync trên form cân để tạo thông điệp đồng bộ đến ERP/FRM thông qua SubmitTripResultHandler
2. THE Hệ_thống SHALL tạo thông điệp với business_key duy nhất gồm đích, trip number, operation, revision, và shipment number; cùng business_key không tạo bản trùng
3. WHILE hợp đồng API với hệ thống đích chưa được xác minh (D-02), THE Hệ_thống SHALL giữ thông điệp ở trạng thái Held và hiển thị lý do chưa gửi
4. WHEN thông điệp ở trạng thái Failed hoặc Unknown, THE Hệ_thống SHALL cho phép Quản_lý_ca tra cứu trạng thái trên hệ thống đích và kết luận Acknowledged hoặc Failed thông qua ResolveUnknownMessageHandler
5. IF thông điệp ở trạng thái Unknown, THEN THE Hệ_thống SHALL không tự động gửi lại mà yêu cầu tra cứu đối soát trước, chỉ cho retry tự động khi hợp đồng đã chứng minh cơ chế chống trùng (idempotency)
6. THE Hệ_thống SHALL hiển thị trạng thái đồng bộ trên form cân và trên IntegrationMonitorForm (UI-08) với đầy đủ thông tin: business_key, trạng thái, thời gian gửi, lỗi nếu có
7. WHEN phân bổ cho chuyến chưa được duyệt, THE Hệ_thống SHALL giữ thông điệp chưa gửi và hiển thị lý do còn phân bổ chưa duyệt

### Requirement 9: Ghép xe — Phân bổ trọng lượng cho nhiều chủ mía

**User Story:** Là Quản_lý_ca, tôi muốn phân bổ trọng lượng cân cho nhiều chủ mía trên cùng một chuyến xe, để xử lý trường hợp ghép xe phổ biến tại nhà máy mía.

#### Acceptance Criteria

1. THE Hệ_thống SHALL hỗ trợ một chuyến có nhiều dòng phân bổ (Allocation), mỗi dòng gắn với một chủ mía hoặc Shipment, tổng phân bổ bảo toàn tổng trọng lượng hàng thực tế
2. WHEN Quản_lý_ca chọn phương pháp phân bổ ApprovedRatio, THE Hệ_thống SHALL phân bổ trọng lượng theo tỷ lệ đã duyệt cho từng chủ mía, phần dư làm tròn được ghi nhận minh bạch trong RoundingAdjustmentKg
3. WHEN Quản_lý_ca chọn phương pháp FixedQuantityWithRemainder, THE Hệ_thống SHALL gán số lượng cố định cho từng dòng và phần còn lại về dòng chỉ định
4. THE Hệ_thống SHALL bắt buộc phê duyệt (ApprovalRef) cho mọi thao tác phân bổ, phân bổ chưa duyệt không được gửi kết quả đến FRM/ERP
5. IF trọng lượng hàng (vehicle_net_kg) âm hoặc bằng 0, THEN THE Hệ_thống SHALL từ chối phân bổ và yêu cầu xử lý ngoại lệ theo quy trình trước
6. THE Hệ_thống SHALL hiển thị bảng phân bổ trên TripDetailForm (UI-06) với từng dòng chủ mía, trọng lượng phân bổ, tỷ lệ, và phần dư làm tròn

### Requirement 10: Cân lại sau sự cố

**User Story:** Là Quản_lý_ca, tôi muốn cho phép cân lại một lượt cân khi phát hiện sự cố (cân nhầm, lỗi thiết bị, lỗi biển số), để sửa số liệu mà vẫn giữ nguyên lịch sử cân cũ.

#### Acceptance Criteria

1. WHEN Quản_lý_ca yêu cầu cân lại cho một vai trò cân (Gross hoặc Tare) đã có lượt cân hiệu lực, THE Hệ_thống SHALL tạo yêu cầu phê duyệt (Approval) với lý do bắt buộc và tài liệu sự cố nếu có
2. IF kết quả cân đã được gửi đến FRM/ERP (có IntegrationMessage ở trạng thái Acknowledged), THEN THE Hệ_thống SHALL đánh dấu yêu cầu cân lại là REWEIGH_AFTER_POST, yêu cầu tạo yêu cầu điều chỉnh liên kết bản trước trên hệ thống đích
3. WHEN cân lại được phê duyệt, THE Hệ_thống SHALL cho phép mở phiên cân mới cho cùng vai trò, tạo WeighEvent mới liên kết SupersedesId đến lượt cân trước, đặt lượt cũ thành IsEffective bằng false
4. THE Hệ_thống SHALL giữ nguyên toàn bộ lịch sử lượt cân cũ để tra cứu, không UPDATE hoặc DELETE bản ghi WeighEvent đã commit
5. WHEN cân lại hoàn thành, THE Hệ_thống SHALL yêu cầu chạy lại đối soát vì số đầu vào đã thay đổi, đối soát cũ hết hiệu lực
6. IF chuyến đang ở trạng thái Paused do sự cố, THEN THE Hệ_thống SHALL yêu cầu Resume chuyến trước khi cân lại, và việc Resume đòi hỏi xác nhận đã kiểm tra kết nối, vị trí xe, và giải quyết giao dịch Unknown

### Requirement 11: Nhập tay số cân với quyền quản trị

**User Story:** Là Quản_lý_ca, tôi muốn nhập tay số cân khi thiết bị cân gặp sự cố nhưng vẫn cần ghi nhận trọng lượng, để không làm gián đoạn quy trình vận chuyển.

#### Acceptance Criteria

1. WHEN Quản_lý_ca có quyền weighing.weight.manual, THE Hệ_thống SHALL hiển thị tùy chọn nhập tay số cân trên form cân (WeighOperationForm)
2. THE Hệ_thống SHALL bắt buộc lý do, phê duyệt hợp lệ (ApprovalRef), và ghi WeighEvent với Source là Manual khi nhập tay
3. WHILE chuyến đang ở trạng thái Paused, THE Hệ_thống SHALL từ chối nhập tay vì nhập tay không được dùng để bỏ qua trạng thái lỗi đang hiệu lực
4. WHILE chuyến có giao dịch tích hợp chưa rõ kết quả (Unknown), THE Hệ_thống SHALL từ chối nhập tay và yêu cầu đối soát giao dịch trước
5. THE Hệ_thống SHALL ghi audit log đầy đủ cho thao tác nhập tay gồm người thực hiện, thời gian, lý do, phê duyệt, số cân gốc và quy đổi

### Requirement 12: Cập nhật loại mía sai trên FRM

**User Story:** Là Nhân_viên_kho, tôi muốn hệ thống đồng bộ lại thông tin loại mía (tươi/cháy) từ FRM khi phát hiện sai, để phiếu cân phản ánh đúng loại hàng thực tế.

#### Acceptance Criteria

1. WHEN FRM cập nhật loại mía cho chuyến (ví dụ từ mía tươi sang mía cháy hoặc ngược lại), THE Hệ_thống SHALL nhận thông tin cập nhật và đồng bộ trường SugarcaneType trên Trip
2. WHEN loại mía được cập nhật, THE Hệ_thống SHALL ghi audit log ghi nhận giá trị cũ và giá trị mới, người cập nhật, và correlation_id
3. THE Hệ_thống SHALL hiển thị loại mía cập nhật trên form cân và form chi tiết chuyến ngay sau khi đồng bộ
4. IF chuyến đã gửi kết quả đến ERP/FRM (đã Posted), THEN THE Hệ_thống SHALL tạo yêu cầu điều chỉnh liên kết với chuyến để cập nhật thông tin trên hệ thống đích

### Requirement 13: Hiển thị và giám sát trạng thái thiết bị trên form cân

**User Story:** Là Nhân_viên_cân, tôi muốn thấy rõ trạng thái kết nối của tất cả thiết bị liên quan trên form cân, để biết ngay khi có sự cố thiết bị cần xử lý.

#### Acceptance Criteria

1. THE Hệ_thống SHALL hiển thị thanh trạng thái trên form cân (WeighOperationForm) với trạng thái kết nối của: Đầu đọc barcode, Đồng hồ cân bàn 1, Đồng hồ cân bàn 2, Camera LPR 1, Camera LPR 2, Camera toàn cảnh
2. WHEN đầu cân mất tín hiệu hoặc gửi dữ liệu cũ, THE Hệ_thống SHALL hiển thị dấu hiệu mất tín hiệu trên WeighDisplayControl bằng ký hiệu dạng ba gạch ngang và thời gian mất tín hiệu, không giữ số cũ như đang trực tuyến
3. THE Hệ_thống SHALL hiển thị thời gian ổn định dạng đếm ngược trên WeighDisplayControl, ví dụ Ổn định 3.2 giây trên 5.0 giây, để Nhân_viên_cân biết khi nào đủ điều kiện cân ổn định
4. WHEN đồng hồ cân báo lỗi (Fault, Overload, Underload), THE Hệ_thống SHALL hiển thị trạng thái lỗi cụ thể trên thanh trạng thái và trên WeighDisplayControl, đồng thời chặn chốt cân cho đến khi thiết bị hoạt động bình thường

