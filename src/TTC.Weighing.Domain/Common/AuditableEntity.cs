namespace TTC.Weighing.Domain.Common;

/// <summary>
/// Base cho mọi thực thể nghiệp vụ. Có version cho optimistic concurrency (03_Du_lieu mục 3).
/// Không xóa cứng chứng từ nghiệp vụ — dùng DeletedAt.
/// </summary>
public abstract class AuditableEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Optimistic concurrency token. Mọi operation ghi phải kiểm tra version mong đợi.</summary>
    public int Version { get; set; }
}
