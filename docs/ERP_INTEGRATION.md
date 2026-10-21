# Tích hợp ERP — Scale ↔ Oracle Fusion (qua DigiScale)

## Tổng quan

Hệ thống Scale tích hợp với Oracle Fusion ERP **thông qua DigiScale** (middleware).
- **NHẬN** data: `GET /api/v1/integration/order-lines` (Basic Auth)
- **GỬI** data: `POST /api/v1/integration/oracle-tickets` (Basic Auth)
- Base URL: `https://digiscale-uat.agris.com.vn`

## 6 loại giao dịch (TransactionTypeCode)

| Code | TransactionTypeName | ActionCodeRequest | Hướng | Mô tả |
|------|---|---|---|---|
| 01 | Purchasing Order for Receiving | RECEIVE | Nhập hàng | Mua vôi/hóa chất, NCC giao hàng vào nhà máy |
| 02 | InterOrg Intransit Shipped | *(outbound)* | Oracle→Scale | Chuyển kho nội bộ - xuất |
| 03 | InterOrg Intransit for Receiving | RECEIVE | Nhập hàng | Chuyển kho nội bộ - nhận |
| 04 | Transfer Order for Ship Confirm | CONFIRM | Xuất hàng | Xuất kho/chuyển kho/mua bán nội bộ |
| 05 | Transfer Order for Receiving | RECEIVE | Nhập hàng | Nhận hàng chuyển kho |
| 06 | Sales Order for Ship Confirm | CONFIRM | Xuất hàng | Bán hàng, xuất giao khách |

### Quy tắc ActionCodeRequest
- **RECEIVE** = nhập hàng vào nhà máy (01, 03, 05)
- **CONFIRM** = xuất hàng ra khỏi nhà máy (04, 06)
- **02** = outbound từ Oracle (Scale chỉ nhận data, không push)

## API GET — Kéo order-lines từ DigiScale

```
GET /api/v1/integration/order-lines
Auth: Basic (integration_frm / password)

Query params (all optional):
  businessUnit    "KSK" hoặc "KSK BU" (normalized)
  fromDate        yyyy-MM-dd
  toDate          yyyy-MM-dd
  transactionTypeCode  01|02|03|04|05|06
  shipmentNumber, tripNumber, plateNumber, search
  page (from 1), pageSize (default 50, max 200)
```

## API POST — Đẩy phiếu cân vật tư lên DigiScale → Oracle

```
POST /api/v1/integration/oracle-tickets
Auth: Basic

Payload mẫu (type 01 - PO nhập hàng):
{
  "TransactionTypeName": "Purchasing Order for Receiving",
  "TransactionTypeCode": "01",
  "ActionCodeRequest": "RECEIVE",
  "TripNumber": "PO10100347",
  "ShipmentNumber": "PO10100347",
  "Transporter": "Nhà vận chuyển",
  "PlateNumber": "Biển số",
  "DriverName": "Tên tài xế",
  "WeightIn": 755,
  "WeightOut": 250,
  "NetWeight": 505,
  "WeightNumber": "Số phiếu cân",
  "OrganizationCode": "101505",
  "BusinessUnit": "TTC BH BU",
  "InterfaceReference": "Số tham chiếu",
  "lines": [{
    "OrganizationCode": "101505",
    "ReferenceNumber": "PO10100347",
    "ReferenceLineNumber": "1.1",
    "ShipmentLineNumber": "1.1",
    "ItemNumber": "10100007",
    "Quantity": 500,
    "UnitOfMeasure": "Kg"
  }]
}
```

Response:
- Success: `{"Status": "SUCCESS", "ReceiptNumber": "100021"}`
- Error: `{"Status": "ERROR", "ErrorMessage": "..."}`

## BU filter

- Mỗi nhà máy (BU) chỉ thấy data của mình
- BU lấy từ `Program.ActiveOrganizationUnitCode` (set khi login)
- API trả BU dạng "KSK BU" → normalize bỏ suffix " BU" khi compare
