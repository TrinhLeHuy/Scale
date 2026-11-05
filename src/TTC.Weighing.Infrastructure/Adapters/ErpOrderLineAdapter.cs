using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TTC.Weighing.Infrastructure.Adapters;

/// <summary>
/// Kéo dữ liệu đơn hàng ERP từ DigiScale (order-lines).
/// GET /api/v1/integration/order-lines?businessUnit=...&fromDate=...&toDate=...
/// Auth: Bearer token (từ login) hoặc Basic Auth fallback.
/// </summary>
public sealed class ErpOrderLineAdapter
{
    private const string API_PATH = "api/v1/integration/order-lines";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ErpOrderLineAdapter> _logger;

    /// <summary>Bearer token từ login DigiScale. Set sau login.</summary>
    public string? ActiveAccessToken { get; set; }

    public ErpOrderLineAdapter(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<ErpOrderLineAdapter> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Kéo danh sách order-lines từ ERP theo BU + khung ngày.
    /// </summary>
    public async Task<ErpOrderLineResponse?> GetOrderLinesAsync(
        string businessUnit,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? shipmentNumber = null,
        string? tripNumber = null,
        string? plateNumber = null,
        string? transactionTypeCode = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        try
        {
            var query = $"{API_PATH}?businessUnit={Uri.EscapeDataString(businessUnit)}" +
                $"&page={page}" +
                $"&pageSize={pageSize}";

            if (fromDate.HasValue)
                query += $"&fromDate={fromDate.Value:yyyy-MM-dd}";
            if (toDate.HasValue)
                query += $"&toDate={toDate.Value:yyyy-MM-dd}";
            if (!string.IsNullOrWhiteSpace(shipmentNumber))
                query += $"&shipmentNumber={Uri.EscapeDataString(shipmentNumber)}";
            if (!string.IsNullOrWhiteSpace(tripNumber))
                query += $"&tripNumber={Uri.EscapeDataString(tripNumber)}";
            if (!string.IsNullOrWhiteSpace(plateNumber))
                query += $"&plateNumber={Uri.EscapeDataString(plateNumber)}";
            if (!string.IsNullOrWhiteSpace(transactionTypeCode))
                query += $"&transactionTypeCode={Uri.EscapeDataString(transactionTypeCode)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, query);
            SetAuth(request);

            var fullUrl = _httpClient.BaseAddress is not null
                ? new Uri(_httpClient.BaseAddress, query).ToString()
                : query;
            _logger.LogInformation("ERP: GET {Url}", fullUrl);

            var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            // Debug: ghi response ra file
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "last-erp-response.txt"), $"GET {query}\nHTTP {(int)response.StatusCode}\n{body}"); } catch { }

            _logger.LogInformation("ERP: HTTP {Status} ({Length} bytes) Body={Body}",
                (int)response.StatusCode, body.Length, body.Length > 500 ? body[..500] : body);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ERP: GET order-lines failed HTTP {Status}: {Body}",
                    (int)response.StatusCode, body.Length > 500 ? body[..500] : body);
                return null;
            }

            return JsonSerializer.Deserialize<ErpOrderLineResponse>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERP: error fetching order-lines");
            return null;
        }
    }

    /// <summary>Kéo tất cả pages.</summary>
    public async Task<List<ErpOrderLine>> GetAllOrderLinesAsync(
        string businessUnit, DateTime? fromDate = null, DateTime? toDate = null,
        string? transactionTypeCode = null,
        CancellationToken ct = default)
    {
        var allItems = new List<ErpOrderLine>();

        // Kéo hết theo hasNext của API; giới hạn an toàn chỉ để tránh lặp vô hạn nếu API trả sai hasNext.
        const int SAFETY_MAX_PAGES = 10_000;
        for (var page = 1; page <= SAFETY_MAX_PAGES; page++)
        {
            var response = await GetOrderLinesAsync(businessUnit, fromDate, toDate,
                transactionTypeCode: transactionTypeCode,
                page: page, pageSize: 200, ct: ct);

            if (response?.Items is null || response.Items.Count == 0)
                break;

            allItems.AddRange(response.Items);

            _logger.LogDebug("ERP: order-lines page {Page} — {Count} items, total: {Total}",
                page, response.Items.Count, allItems.Count);

            if (!response.HasNext)
                break;
        }

        _logger.LogInformation("ERP: fetched {Count} order-lines for {BU} [{From} → {To}]",
            allItems.Count, businessUnit,
            fromDate?.ToString("yyyy-MM-dd") ?? "(all)",
            toDate?.ToString("yyyy-MM-dd") ?? "(all)");

        // Gán BU cho items nếu API không trả (ERP API trả businessUnitName thay vì businessUnit)
        foreach (var item in allItems)
        {
            if (string.IsNullOrWhiteSpace(item.BusinessUnit))
                item.BusinessUnit = item.BusinessUnitName ?? businessUnit;
        }

        return allItems;
    }

    /// <summary>Load order-lines từ DB local.</summary>
    public async Task<List<ErpOrderLine>> LoadFromDbAsync()
    {
        var connStr = _configuration.GetConnectionString("WeighingDb");
        if (string.IsNullOrWhiteSpace(connStr)) return [];

        try
        {
            await using var conn = new Npgsql.NpgsqlConnection(connStr);
            await conn.OpenAsync();
            var items = new List<ErpOrderLine>();

            await using var cmd = new Npgsql.NpgsqlCommand(
                "SELECT * FROM erp_order_lines ORDER BY synced_at DESC, shipment_number", conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
                items.Add(ReadOrderLine(reader));

            _logger.LogInformation("ERP: loaded {Count} order-lines from DB", items.Count);
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ERP: failed to load from DB");
            return [];
        }
    }

    /// <summary>Tìm order-line trong DB local theo shipment/trip/plate. Ưu tiên shipment → trip → plate.</summary>
    public async Task<ErpOrderLine?> FindInDbAsync(string? shipmentOrTrip, string? plateNumber = null)
    {
        var connStr = _configuration.GetConnectionString("WeighingDb");
        if (string.IsNullOrWhiteSpace(connStr)) return null;

        try
        {
            await using var conn = new Npgsql.NpgsqlConnection(connStr);
            await conn.OpenAsync();

            // Tìm theo shipment_number hoặc trip_number
            if (!string.IsNullOrWhiteSpace(shipmentOrTrip))
            {
                await using var cmd = new Npgsql.NpgsqlCommand(
                    "SELECT * FROM erp_order_lines WHERE shipment_number = $1 OR trip_number = $1 ORDER BY synced_at DESC", conn);
                cmd.Parameters.AddWithValue(shipmentOrTrip);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) { var line = ReadOrderLine(reader); if (TTC.Weighing.Infrastructure.Queries.OrganizationUnitFilter.MatchesCurrent(line.BusinessUnit)) return line; }
            }

            // Fallback: tìm theo plate_number
            if (!string.IsNullOrWhiteSpace(plateNumber))
            {
                await using var cmd = new Npgsql.NpgsqlCommand(
                    "SELECT * FROM erp_order_lines WHERE plate_number = $1 ORDER BY synced_at DESC", conn);
                cmd.Parameters.AddWithValue(plateNumber);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) { var line = ReadOrderLine(reader); if (TTC.Weighing.Infrastructure.Queries.OrganizationUnitFilter.MatchesCurrent(line.BusinessUnit)) return line; }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ERP: FindInDbAsync failed");
            return null;
        }
    }

    private static readonly JsonSerializerOptions RawJsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Có JSON gốc → đọc y như lúc kéo API; dòng cũ chưa có → đọc theo cột.</summary>
    private static ErpOrderLine ReadOrderLine(Npgsql.NpgsqlDataReader reader)
    {
        var raw = ReadOptional(reader, "raw_json");
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                var line = JsonSerializer.Deserialize<ErpOrderLine>(raw, RawJsonOptions);
                if (line is not null)
                {
                    line.BusinessUnit ??= reader["business_unit"] as string;
                    return line;
                }
            }
            catch (JsonException) { /* JSON hỏng → đọc theo cột */ }
        }
        return ReadOrderLineColumns(reader);
    }

    private static ErpOrderLine ReadOrderLineColumns(Npgsql.NpgsqlDataReader reader) => new()
    {
        Id = ReadOptional(reader, "api_id"),
        Type = ReadOptional(reader, "record_type"),
        ShipmentLineNumber = long.TryParse(ReadOptional(reader, "shipment_line_number"), out var lineNo) ? lineNo : null,
        ShipmentNumber = reader["shipment_number"] as string,
        TripNumber = reader["trip_number"] as string,
        PlateNumber = reader["plate_number"] as string,
        TransactionTypeName = reader["transaction_type_name"] as string,
        TransactionTypeCode = reader["transaction_type_code"] as string,
        ItemNumber = reader["item_number"] as string,
        ItemDescription = reader["item_description"] as string,
        OrganizationCode = reader["organization_code"] as string,
        VendorName = reader["vendor_name"] as string,
        CustomerName = reader["customer_name"] as string,
        Transporter = reader["transporter"] as string,
        DriverName = reader["driver_name"] as string,
        TotalQuantity = reader["total_quantity"] is DBNull ? null : (decimal?)reader["total_quantity"],
        RemainingQuantity = reader["remaining_quantity"] is DBNull ? null : (decimal?)reader["remaining_quantity"],
        GrossWeight = reader["gross_weight"] is DBNull ? null : (decimal?)reader["gross_weight"],
        TransactionDate = reader["transaction_date"] as string,
        BusinessUnit = reader["business_unit"] as string,
        ReferenceNumber = reader["reference_number"] as string,
        ActionCodeRequest = reader["action_code"] as string,
        Source = reader["source_system"] as string,
        CreatedBy = reader["created_by"] as string,
        TruckOwner = ReadOptional(reader, "truck_owner"),
        VendorNumber = ReadOptional(reader, "vendor_number"),
        VendorSiteCode = ReadOptional(reader, "vendor_site_code"),
        OrganizationName = ReadOptional(reader, "organization_name"),
        SubInventoryCode = ReadOptional(reader, "sub_inventory_code")
    };

    private static string? ReadOptional(System.Data.Common.DbDataReader reader, string column)
    {
        for (var i = 0; i < reader.FieldCount; i++)
            if (string.Equals(reader.GetName(i), column, StringComparison.OrdinalIgnoreCase))
                return reader.IsDBNull(i) ? null : reader.GetValue(i) as string;
        return null;
    }

    /// <summary>Ghi integration_message cho trip sau khi push oracle-tickets thành công.</summary>
    public Task RecordErpSyncAsync(Guid tripId, string operation, string? responseBody)
        => RecordSyncAsync(tripId, "ERP", operation, success: true);

    /// <summary>
    /// Ghi trạng thái đồng bộ của chuyến vào integration_messages (cột "Đồng bộ" trên lưới đọc bản mới nhất).
    /// <paramref name="destination"/>: "ERP" (vật tư, oracle-tickets) hoặc "FRM" (mía, phiếu cân DigiScale).
    /// Thành công → Acknowledged; lỗi → Failed. Lỗi ghi chỉ log, không chặn luồng cân.
    /// </summary>
    public async Task RecordSyncAsync(Guid tripId, string destination, string operation, bool success)
    {
        var connStr = _configuration.GetConnectionString("WeighingDb");
        if (string.IsNullOrWhiteSpace(connStr)) return;

        try
        {
            await using var conn = new Npgsql.NpgsqlConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand(@"
INSERT INTO integration_messages (id, trip_id, destination, operation, status, business_key, created_at, attempts)
VALUES (gen_random_uuid(), $1, $2, $3, $4, $5, now(), 1)
", conn);
            cmd.Parameters.AddWithValue(tripId);
            cmd.Parameters.AddWithValue(destination);
            cmd.Parameters.AddWithValue(operation);
            cmd.Parameters.AddWithValue(success ? "Acknowledged" : "Failed");
            cmd.Parameters.AddWithValue($"{destination}-{tripId:N}-{operation}");
            await cmd.ExecuteNonQueryAsync();

            _logger.LogInformation("{Dest}: recorded sync status for trip {TripId} op={Op} ok={Ok}",
                destination, tripId, operation, success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Dest}: failed to record sync status", destination);
        }
    }

    /// <summary>
    /// Lưu order-lines xuống DB. UPSERT theo storage_key (ID bản ghi DigiScale): mọi nguồn (Oracle, WMS...)
    /// và mọi dòng của cùng shipment đều được giữ riêng.
    /// Không xóa dữ liệu cũ — dữ liệu mới cập nhật synced_at, cũ giữ nguyên.
    /// </summary>
    public async Task SaveToDbAsync(List<ErpOrderLine> items)
    {
        var connStr = _configuration.GetConnectionString("WeighingDb");
        if (string.IsNullOrWhiteSpace(connStr) || items.Count == 0) return;

        try
        {
            await using var conn = new Npgsql.NpgsqlConnection(connStr);
            await conn.OpenAsync();

            // Đảm bảo cột mới tồn tại (idempotent)
            foreach (var col in new[] { "reference_number", "action_code", "source_system", "created_by", "truck_owner", "vendor_number", "vendor_site_code", "organization_name", "sub_inventory_code",
                         "api_id", "record_type", "shipment_line_number", "storage_key", "raw_json" })
            {
                await using var alter = new Npgsql.NpgsqlCommand(
                    $"ALTER TABLE erp_order_lines ADD COLUMN IF NOT EXISTS {col} text", conn);
                await alter.ExecuteNonQueryAsync();
            }

            // Khóa cũ (shipment_number, business_unit) gộp mất các dòng khác nhau của cùng shipment
            // và đè dữ liệu giữa các nguồn (Oracle / WMS). Khóa mới: storage_key (ID bản ghi DigiScale).
            // Index cũ dựng lại được bằng reset-db.sql nếu cần quay lui.
            await using (var dropOld = new Npgsql.NpgsqlCommand(
                "DROP INDEX IF EXISTS uq_erp_order_lines_shipment_bu", conn))
                await dropOld.ExecuteNonQueryAsync();
            await using (var idx = new Npgsql.NpgsqlCommand(
                "CREATE UNIQUE INDEX IF NOT EXISTS uq_erp_order_lines_storage_key ON erp_order_lines (storage_key) WHERE storage_key IS NOT NULL", conn))
                await idx.ExecuteNonQueryAsync();
            await using (var idxShipment = new Npgsql.NpgsqlCommand(
                "CREATE INDEX IF NOT EXISTS idx_erp_order_lines_shipment_number ON erp_order_lines (shipment_number)", conn))
                await idxShipment.ExecuteNonQueryAsync();

            // Dòng cache kiểu cũ (chưa có storage_key) của các shipment vừa kéo về → thay bằng dữ liệu mới,
            // tránh hiện trùng. Đây là bảng cache từ API, không phải chứng từ nghiệp vụ.
            var refreshedShipments = items
                .Where(i => !string.IsNullOrWhiteSpace(i.ShipmentNumber))
                .Select(i => i.ShipmentNumber!)
                .Distinct()
                .ToArray();
            if (refreshedShipments.Length > 0)
            {
                await using var cleanup = new Npgsql.NpgsqlCommand(
                    "DELETE FROM erp_order_lines WHERE storage_key IS NULL AND shipment_number = ANY($1)", conn);
                cleanup.Parameters.AddWithValue(refreshedShipments);
                await cleanup.ExecuteNonQueryAsync();
            }

            foreach (var item in items)
            {
                await using var cmd = new Npgsql.NpgsqlCommand(@"
INSERT INTO erp_order_lines (shipment_number,trip_number,plate_number,transaction_type_name,transaction_type_code,
  item_number,item_description,organization_code,vendor_name,customer_name,transporter,driver_name,
  total_quantity,remaining_quantity,gross_weight,transaction_date,business_unit,
  reference_number,action_code,source_system,created_by,truck_owner,vendor_number,vendor_site_code,organization_name,sub_inventory_code,
  api_id,record_type,shipment_line_number,storage_key,raw_json,synced_at)
VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20,$21,$22,$23,$24,$25,$26,$27,$28,$29,$30,$31,now())
ON CONFLICT (storage_key) WHERE storage_key IS NOT NULL
DO UPDATE SET
  shipment_number=$1, trip_number=$2, plate_number=$3, transaction_type_name=$4, transaction_type_code=$5,
  item_number=$6, item_description=$7, organization_code=$8, vendor_name=$9, customer_name=$10,
  transporter=$11, driver_name=$12, total_quantity=$13, remaining_quantity=$14, gross_weight=$15,
  transaction_date=$16, business_unit=$17, reference_number=$18, action_code=$19, source_system=$20, created_by=$21,
  truck_owner=$22, vendor_number=$23, vendor_site_code=$24,
  organization_name=$25, sub_inventory_code=$26,
  api_id=$27, record_type=$28, shipment_line_number=$29, raw_json=$31, synced_at=now()
", conn);
                cmd.Parameters.AddWithValue((object?)item.ShipmentNumber ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.TripNumber ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.PlateNumber ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.TransactionTypeName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.TransactionTypeCode ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.ItemNumber ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.ItemDescription ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.OrganizationCode ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.VendorName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.CustomerName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.EffectiveTransporter ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.DriverName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.TotalQuantity ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.RemainingQuantity ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.GrossWeight ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.TransactionDate ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.BusinessUnit ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.ReferenceNumber ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.ActionCodeRequest ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.Source ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.CreatedBy ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.TruckOwner ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.VendorNumber ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.VendorSiteCode ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.OrganizationName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.SubInventoryCode ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.Id ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.Type ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)item.ShipmentLineNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? DBNull.Value);
                cmd.Parameters.AddWithValue(item.StorageKey);
                // JSON gốc: đọc lại đủ mọi trường (Uom, ItemName, kho, số lượng...) — các cột riêng không có hết.
                cmd.Parameters.AddWithValue(JsonSerializer.Serialize(item));
                await cmd.ExecuteNonQueryAsync();
            }

            _logger.LogInformation("ERP: upserted {Count} order-lines to DB", items.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ERP: failed to save to DB");
        }
    }

    private void SetAuth(HttpRequestMessage request)
    {
        // API GET order-lines dùng Basic Auth (theo tài liệu DigiScale)
        var username = _configuration.GetValue<string>("FrmPlanLookup:Username");
        var password = _configuration.GetValue<string>("FrmPlanLookup:Password");
        if (!string.IsNullOrWhiteSpace(username))
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{username}:{password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            return;
        }

        // Fallback: Bearer token nếu không có Basic Auth config
        var token = ActiveAccessToken
            ?? _configuration.GetValue<string>("ErpApi:AccessToken");
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }
}

// ══════════════════════════════════════════════════════════════
//  DTOs cho ERP order-lines response
// ══════════════════════════════════════════════════════════════

public sealed class ErpOrderLineResponse
{
    [JsonPropertyName("items")]
    public List<ErpOrderLine>? Items { get; set; }

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("hasNext")]
    public bool HasNext { get; set; }
}

public sealed class ErpOrderLine
{
    /// <summary>ID bản ghi phía DigiScale — duy nhất cho từng dòng, dùng làm khóa lưu local.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Nguồn dữ liệu phía DigiScale (ví dụ Oracle, WMS).</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("shipmentLineNumber")]
    public long? ShipmentLineNumber { get; set; }

    /// <summary>
    /// Khóa lưu local: ID DigiScale; API không trả ID thì ghép nguồn + shipment + dòng + BU
    /// (không gộp các dòng / các nguồn khác nhau của cùng một shipment).
    /// </summary>
    [JsonIgnore]
    public string StorageKey =>
        !string.IsNullOrWhiteSpace(Id) ? Id.Trim()
        : $"{Type}|{ShipmentNumber}|{ShipmentLineNumber}|{TripNumber}|{BusinessUnit}";

    /// <summary>Nguồn WMS — nghiệp vụ là luồng SO (Sales Order for Ship Confirm).</summary>
    public const string WMS_TYPE = "WMS";
    public const string SO_TRANSACTION_TYPE_CODE = "06";
    public const string SO_TRANSACTION_TYPE_NAME = "Sale Order for Ship Confirm";

    [JsonIgnore]
    public bool IsWms => string.Equals(Type?.Trim(), WMS_TYPE, StringComparison.OrdinalIgnoreCase);

    /// <summary>Mã loại GD Oracle dạng số mà DigiScale oracle-tickets chấp nhận (01–06).</summary>
    private static bool IsOracleNumericCode(string? code) => code?.Trim() is "01" or "02" or "03" or "04" or "05" or "06";

    /// <summary>
    /// Dòng thuộc luồng SO: mã 06, hoặc WMS (WMS trả mã "SO" / "Sales Order", không phải mã số Oracle).
    /// </summary>
    [JsonIgnore]
    public bool IsSalesOrderFlow =>
        TransactionTypeCode?.Trim() == SO_TRANSACTION_TYPE_CODE
        || string.Equals(TransactionTypeCode?.Trim(), "SO", StringComparison.OrdinalIgnoreCase)
        || (IsWms && !IsOracleNumericCode(TransactionTypeCode));

    /// <summary>Luồng xuất/ship confirm: TO ship confirm (04) hoặc luồng SO (06 / WMS).</summary>
    [JsonIgnore]
    public bool IsShipConfirmFlow => IsSalesOrderFlow || TransactionTypeCode?.Trim() == "04";

    /// <summary>
    /// Mã loại GD đẩy oracle-tickets (DigiScale chỉ nhận mã số Oracle): mã số ERP giữ nguyên;
    /// luồng SO không có mã số (WMS "SO") → 06.
    /// </summary>
    [JsonIgnore]
    public string EffectiveTransactionTypeCode =>
        IsOracleNumericCode(TransactionTypeCode) ? TransactionTypeCode!.Trim()
        : IsSalesOrderFlow ? SO_TRANSACTION_TYPE_CODE
        : TransactionTypeCode?.Trim() ?? "";

    /// <summary>Tên loại GD đi kèm mã: mã số ERP giữ tên ERP; luồng SO quy về 06 thì dùng tên SO của Oracle.</summary>
    [JsonIgnore]
    public string EffectiveTransactionTypeName =>
        IsOracleNumericCode(TransactionTypeCode) ? TransactionTypeName?.Trim() ?? ""
        : IsSalesOrderFlow ? SO_TRANSACTION_TYPE_NAME
        : TransactionTypeName?.Trim() ?? "";

    /// <summary>
    /// Action đẩy oracle-tickets: ưu tiên actionCodeRequest ERP trả về; không có thì suy theo luồng
    /// (ship confirm / WMS → CONFIRM, còn lại → RECEIVE).
    /// </summary>
    [JsonIgnore]
    public string EffectiveActionCode =>
        !string.IsNullOrWhiteSpace(ActionCodeRequest) ? ActionCodeRequest.Trim().ToUpperInvariant()
        : IsShipConfirmFlow ? "CONFIRM" : "RECEIVE";

    /// <summary>Nguồn hiển thị: type (Oracle/WMS) kèm source hệ thống gốc nếu có.</summary>
    [JsonIgnore]
    public string? SourceLabel =>
        string.IsNullOrWhiteSpace(Type) ? Source
        : string.IsNullOrWhiteSpace(Source) ? Type
        : $"{Type} / {Source}";

    [JsonPropertyName("shipmentNumber")]
    public string? ShipmentNumber { get; set; }

    [JsonPropertyName("tripNumber")]
    public string? TripNumber { get; set; }

    [JsonPropertyName("plateNumber")]
    public string? PlateNumber { get; set; }

    [JsonPropertyName("transactionTypeName")]
    public string? TransactionTypeName { get; set; }

    [JsonPropertyName("transactionTypeCode")]
    public string? TransactionTypeCode { get; set; }

    [JsonPropertyName("itemCode")]
    public string? ItemCode { get; set; }

    [JsonPropertyName("itemNumber")]
    public string? ItemNumber { get; set; }

    [JsonPropertyName("itemName")]
    public string? ItemName { get; set; }

    [JsonPropertyName("itemDescription")]
    public string? ItemDescription { get; set; }

    [JsonPropertyName("warehouseCode")]
    public string? WarehouseCode { get; set; }

    [JsonPropertyName("warehouseName")]
    public string? WarehouseName { get; set; }

    [JsonPropertyName("customerCode")]
    public string? CustomerCode { get; set; }

    [JsonPropertyName("customerName")]
    public string? CustomerName { get; set; }

    [JsonPropertyName("vendorName")]
    public string? VendorName { get; set; }

    [JsonPropertyName("transporterName")]
    public string? TransporterName { get; set; }

    [JsonPropertyName("transporter")]
    public string? Transporter { get; set; }

    [JsonPropertyName("driverName")]
    public string? DriverName { get; set; }

    [JsonPropertyName("organizationCode")]
    public string? OrganizationCode { get; set; }

    /// <summary>Tên tổ chức kho, ví dụ "126501 - Thanh pham - PROC".</summary>
    [JsonPropertyName("organizationName")]
    public string? OrganizationName { get; set; }

    [JsonPropertyName("subInventoryCode")]
    public string? SubInventoryCode { get; set; }

    [JsonPropertyName("subInventoryName")]
    public string? SubInventoryName { get; set; }

    /// <summary>Kho (tên tổ chức kho ERP): organizationName → warehouseCode/Name; trống → null.</summary>
    [JsonIgnore]
    public string? EffectiveWarehouse =>
        !string.IsNullOrWhiteSpace(OrganizationName) ? OrganizationName.Trim()
        : !string.IsNullOrWhiteSpace(WarehouseCode) ? $"{WarehouseCode} - {WarehouseName}".TrimEnd(' ', '-')
        : !string.IsNullOrWhiteSpace(WarehouseName) ? WarehouseName.Trim() : null;

    [JsonPropertyName("quantity")]
    public decimal? Quantity { get; set; }

    [JsonPropertyName("totalQuantity")]
    public decimal? TotalQuantity { get; set; }

    [JsonPropertyName("remainingQuantity")]
    public decimal? RemainingQuantity { get; set; }

    [JsonPropertyName("uom")]
    public string? Uom { get; set; }

    [JsonPropertyName("grossWeight")]
    public decimal? GrossWeight { get; set; }

    [JsonPropertyName("transactionDate")]
    public string? TransactionDate { get; set; }

    [JsonPropertyName("businessUnit")]
    public string? BusinessUnit { get; set; }

    [JsonPropertyName("businessUnitName")]
    public string? BusinessUnitName { get; set; }

    [JsonPropertyName("referenceNumber")]
    public string? ReferenceNumber { get; set; }

    [JsonPropertyName("actionCodeRequest")]
    public string? ActionCodeRequest { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; set; }

    [JsonPropertyName("truckOwner")]
    public string? TruckOwner { get; set; }

    [JsonPropertyName("vendorNumber")]
    public string? VendorNumber { get; set; }

    [JsonPropertyName("vendorSiteCode")]
    public string? VendorSiteCode { get; set; }

    /// <summary>Nhà vận chuyển: `transporter`, không có thì `transporterName`; trống → null.</summary>
    [JsonIgnore]
    public string? EffectiveTransporter =>
        !string.IsNullOrWhiteSpace(Transporter) ? Transporter.Trim()
        : !string.IsNullOrWhiteSpace(TransporterName) ? TransporterName.Trim() : null;
}
