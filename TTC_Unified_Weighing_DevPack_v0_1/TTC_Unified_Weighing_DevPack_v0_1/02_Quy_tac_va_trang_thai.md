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
