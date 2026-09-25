using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TTC.Weighing.Infrastructure.Persistence.Converters;

/// <summary>
/// Chuyển danh sách sang JSON tường minh khi lưu vào cột jsonb.
/// Npgsql 8 không tự serialize kiểu tập hợp nên phải khai báo rõ, đồng thời tránh
/// phụ thuộc vào cơ chế reflection lúc chạy.
/// </summary>
public sealed class JsonListConverter<T> : ValueConverter<List<T>, string>
{
    public JsonListConverter()
        : base(value => Serialize(value), json => Deserialize(json))
    {
    }

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    // Biểu thức của value converter không nhận collection expression nên tách ra hàm tĩnh.
    private static string Serialize(List<T> value)
        => JsonSerializer.Serialize(value ?? new List<T>(), Options);

    private static List<T> Deserialize(string json)
        => string.IsNullOrWhiteSpace(json)
            ? new List<T>()
            : JsonSerializer.Deserialize<List<T>>(json, Options) ?? new List<T>();
}

/// <summary>
/// So sánh theo nội dung để EF phát hiện thay đổi khi danh sách bị sửa tại chỗ.
/// Không có comparer thì thêm phần tử vào danh sách sẽ không được lưu.
/// </summary>
public sealed class JsonListComparer<T> : ValueComparer<List<T>>
{
    public JsonListComparer()
        : base(
            (left, right) => AreEqual(left, right),
            list => ComputeHash(list),
            list => Snapshot(list))
    {
    }

    private static bool AreEqual(List<T>? left, List<T>? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;

        return left.Count == right.Count && left.SequenceEqual(right);
    }

    private static int ComputeHash(List<T>? list)
    {
        if (list is null) return 0;

        var hash = new HashCode();

        foreach (var item in list)
            hash.Add(item);

        return hash.ToHashCode();
    }

    private static List<T> Snapshot(List<T>? list)
        => list is null ? new List<T>() : new List<T>(list);
}
