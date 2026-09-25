using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TTC.Weighing.Infrastructure.Persistence;

/// <summary>
/// Chỉ dùng cho `dotnet ef` lúc thiết kế. Connection string lấy từ biến môi trường,
/// không hard-code thông tin kết nối vào repository (R-035).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<WeighingDbContext>
{
    public WeighingDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("TTC_WEIGHING_ConnectionStrings__WeighingDb")
            ?? "Host=localhost;Port=5432;Database=ttc_weighing_design;Username=postgres";

        var options = new DbContextOptionsBuilder<WeighingDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new WeighingDbContext(options);
    }
}
