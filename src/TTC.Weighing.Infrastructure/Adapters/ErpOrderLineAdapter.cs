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

        for (var page = 1; page <= 100; page++)
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
                "SELECT shipment_number,trip_number,plate_number,transaction_type_name,transaction_type_code,item_number,item_description,organization_code,vendor_name,customer_name,transporter,driver_name,total_quantity,remaining_quantity,gross_weight,transaction_date,business_unit,reference_number,action_code,source_system,created_by FROM erp_order_lines ORDER BY synced_at DESC, shipment_number", conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                items.Add(new ErpOrderLine
                {
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
                    CreatedBy = reader["created_by"] as string
                });
            }

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
                    "SELECT shipment_number,trip_number,plate_number,transaction_type_name,transaction_type_code,item_number,item_description,organization_code,vendor_name,customer_name,transporter,driver_name,total_quantity,remaining_quantity,gross_weight,transaction_date,business_unit,reference_number,action_code,source_system,created_by FROM erp_order_lines WHERE shipment_number = $1 OR trip_number = $1 LIMIT 1", conn);
                cmd.Parameters.AddWithValue(shipmentOrTrip);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync()) return ReadOrderLine(reader);
            }

            // Fallback: tìm theo plate_number
            if (!string.IsNullOrWhiteSpace(plateNumber))
            {
                await using var cmd = new Npgsql.NpgsqlCommand(
                    "SELECT shipment_number,trip_number,plate_number,transaction_type_name,transaction_type_code,item_number,item_description,organization_code,vendor_name,customer_name,transporter,driver_name,total_quantity,remaining_quantity,gross_weight,transaction_date,business_unit,reference_number,action_code,source_system,created_by FROM erp_order_lines WHERE plate_number = $1 LIMIT 1", conn);
                cmd.Parameters.AddWithValue(plateNumber);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync()) return ReadOrderLine(reader);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ERP: FindInDbAsync failed");
            return null;
        }
    }

    private static ErpOrderLine ReadOrderLine(Npgsql.NpgsqlDataReader reader) => new()
    {
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
        CreatedBy = reader["created_by"] as string
    };

    /// <summary>Ghi integration_message cho trip sau khi push oracle-tickets thành công.</summary>
    public async Task RecordErpSyncAsync(Guid tripId, string operation, string? responseBody)
    {
        var connStr = _configuration.GetConnectionString("WeighingDb");
        if (string.IsNullOrWhiteSpace(connStr)) return;

        try
        {
            await using var conn = new Npgsql.NpgsqlConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand(@"
INSERT INTO integration_messages (id, trip_id, destination, operation, status, business_key, created_at, attempts)
VALUES (gen_random_uuid(), $1, 'ERP', $2, 'Acknowledged', $3, now(), 1)
", conn);
            cmd.Parameters.AddWithValue(tripId);
            cmd.Parameters.AddWithValue(operation);
            cmd.Parameters.AddWithValue($"ERP-{tripId:N}-{operation}");
            await cmd.ExecuteNonQueryAsync();

            _logger.LogInformation("ERP: recorded sync status for trip {TripId} op={Op}", tripId, operation);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ERP: failed to record sync status");
        }
    }

    /// <summary>
    /// Lưu order-lines xuống DB. UPSERT: trùng (shipment_number, business_unit) thì cập nhật, không trùng thì thêm mới.
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

            // Đảm bảo unique index tồn tại (idempotent)
            await using (var idx = new Npgsql.NpgsqlCommand(
                "CREATE UNIQUE INDEX IF NOT EXISTS uq_erp_order_lines_shipment_bu ON erp_order_lines (shipment_number, business_unit) WHERE shipment_number IS NOT NULL", conn))
                await idx.ExecuteNonQueryAsync();

            // Đảm bảo cột mới tồn tại (idempotent)
            foreach (var col in new[] { "reference_number", "action_code", "source_system", "created_by" })
            {
                await using var alter = new Npgsql.NpgsqlCommand(
                    $"ALTER TABLE erp_order_lines ADD COLUMN IF NOT EXISTS {col} text", conn);
                await alter.ExecuteNonQueryAsync();
            }

            foreach (var item in items)
            {
                await using var cmd = new Npgsql.NpgsqlCommand(@"
INSERT INTO erp_order_lines (shipment_number,trip_number,plate_number,transaction_type_name,transaction_type_code,
  item_number,item_description,organization_code,vendor_name,customer_name,transporter,driver_name,
  total_quantity,remaining_quantity,gross_weight,transaction_date,business_unit,
  reference_number,action_code,source_system,created_by,synced_at)
VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20,$21,now())
ON CONFLICT (shipment_number, business_unit) WHERE shipment_number IS NOT NULL
DO UPDATE SET
  trip_number=$2, plate_number=$3, transaction_type_name=$4, transaction_type_code=$5,
  item_number=$6, item_description=$7, organization_code=$8, vendor_name=$9, customer_name=$10,
  transporter=$11, driver_name=$12, total_quantity=$13, remaining_quantity=$14, gross_weight=$15,
  transaction_date=$16, reference_number=$18, action_code=$19, source_system=$20, created_by=$21, synced_at=now()
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
                cmd.Parameters.AddWithValue((object?)item.Transporter ?? DBNull.Value);
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
}
