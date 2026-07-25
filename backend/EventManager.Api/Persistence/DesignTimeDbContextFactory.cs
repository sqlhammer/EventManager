using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EventManager.Api.Persistence;

/// <summary>Lets EF Core tooling (migrations) construct the context without booting the web host.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=eventmanager;Username=postgres;Password=postgres")
            .Options;
        return new AppDbContext(options);
    }
}
