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
