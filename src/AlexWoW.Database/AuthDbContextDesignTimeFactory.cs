using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace AlexWoW.Database;

/// <summary>
/// Design-time фабрика для <c>dotnet ef migrations</c> (Срез 2 рефактора DAL #23). Использует
/// ФИКСИРОВАННУЮ <see cref="ServerVersion"/> (MySQL 8.4) — генерация миграций не коннектится к живой БД.
/// На рантайме НЕ используется (контекст там собирается через DI с реальной строкой подключения).
/// </summary>
public sealed class AuthDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        var serverVersion = ServerVersion.Create(new Version(8, 4, 0), ServerType.MySql);
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseMySql("server=localhost;database=alexwow_auth", serverVersion)
            .Options;
        return new AuthDbContext(options);
    }
}
