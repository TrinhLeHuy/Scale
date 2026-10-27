# Bản dịch Lào / Khmer cần duyệt

Các key dưới đây đang dùng **bản dịch nháp** trong `strings.lo.json` và `strings.km.json`.
Giao diện hiển thị bản nháp, không còn tiền tố `[TODO-lo]` / `[TODO-km]`.

Khi có bản dịch chính thức: sửa giá trị trong
`src/TTC.Weighing.WinForms/Localization/Resources/strings.{lo,km}.json`, rồi xóa key khỏi danh sách này.

Cả hai file `lo` và `km` cần duyệt cùng 44 key:

| Nhóm | Key |
|---|---|
| Bảng dữ liệu (ERP/FRM) | `explorer.search_placeholder`, `explorer.clear_filters`, `explorer.count`, `explorer.count_filtered`, `explorer.empty`, `frm.empty`, `erp.empty` |
| Cột bảng Lệnh đốn | `frm.col_ma_ld`, `frm.col_status`, `frm.col_ngay_cap`, `frm.col_tram`, `frm.col_vu_dau_tu`, `frm.col_loai_xe`, `frm.col_uu_tien` |
| Phiếu cân vật tư | `print.mat_form_title`, `print.mat_title`, `print.mat_trip`, `print.mat_direction`, `print.mat_customer`, `print.mat_item`, `print.mat_weight_in`, `print.mat_time_in`, `print.mat_weight_out`, `print.mat_time_out`, `print.mat_net`, `print.mat_net_negative` |
| Màn Cân (vật tư) | `field.direction`, `weigh.more_sec_erp`, `weigh.more_erp_status`, `weigh.more_erp_not_found` |
| Đăng tài vật tư | `reg.erp_already_registered_title`, `reg.erp_already_registered_msg`, `reg.vehicle_not_weighed_out_title`, `reg.vehicle_not_weighed_out_msg` |
| Đo chữ đường (CCS) | `rule.ccs_material_trip`, `ccs.ux_material_trip`, `ccs.ux_ccs_fail_title`, `ccs.ux_retry_blocked_title`, `ccs.ux_retry_no_values`, `ccs.ux_note_ccs_not_positive`, `ccs.ux_ccs_not_positive` |
| Tổng quan (cột số cân) | `col.first_weight_unit`, `col.second_weight_unit`, `col.net_weight_unit` |
