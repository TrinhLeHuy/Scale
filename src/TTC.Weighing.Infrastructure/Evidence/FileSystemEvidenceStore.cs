using TTC.Weighing.Application.Abstractions;

namespace TTC.Weighing.Infrastructure.Evidence;

/// <summary>
/// Lưu bằng chứng vào thư mục theo chuyến. Không lưu được thì lượt cân không được phát hành (B-001.8).
/// Thời hạn giữ ảnh và dữ liệu thô cần D-08; lớp này không tự xóa bất cứ thứ gì.
/// </summary>
public sealed class FileSystemEvidenceStore(string rootPath) : IEvidenceStore
{
    public async Task<IReadOnlyList<string>> PersistAsync(
        Guid tripId, IEnumerable<string> sourceRefs, CancellationToken ct = default)
    {
        var refs = sourceRefs.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();

        if (refs.Count == 0)
            return [];

        var tripFolder = Path.Combine(rootPath, tripId.ToString("N"));
        Directory.CreateDirectory(tripFolder);

        var stored = new List<string>(refs.Count);

        foreach (var sourceRef in refs)
        {
            var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.evidence";
            var path = Path.Combine(tripFolder, fileName);

            // Bản ghi trỏ tới nguồn bằng chứng. Adapter thật sẽ chép ảnh hoặc video vào đây.
            await File.WriteAllTextAsync(path,
                $"trip={tripId}{Environment.NewLine}source={sourceRef}{Environment.NewLine}" +
                $"stored_at={DateTimeOffset.UtcNow:O}{Environment.NewLine}", ct);

            stored.Add(path);
        }

        return stored;
    }
}
