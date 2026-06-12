using AlexWoW.Database;
using AlexWoW.Database.Abstractions;
using AlexWoW.Web.Services;

namespace AlexWoW.Web.Tests;

/// <summary>Чтение стоимостей смены расы/пола из настроек с фолбэком на дефолты (M8.6).</summary>
public sealed class ServerSettingsServiceTests
{
    [Fact]
    public async Task Returns_stored_value_when_present()
    {
        var svc = new ServerSettingsService(new FakeSettings((ServerSettingKeys.RaceChangeCostGold, "1500")));
        Assert.Equal(1500u, await svc.RaceChangeCostGoldAsync());
    }

    [Fact]
    public async Task Falls_back_to_default_when_key_missing()
    {
        var svc = new ServerSettingsService(new FakeSettings());
        Assert.Equal(1000u, await svc.RaceChangeCostGoldAsync());   // дефолт из ServerSettingKeys
        Assert.Equal(2000u, await svc.GenderChangeCostGoldAsync());
    }

    [Fact]
    public async Task Falls_back_to_default_when_value_unparseable()
    {
        var svc = new ServerSettingsService(new FakeSettings((ServerSettingKeys.GenderChangeCostGold, "не-число")));
        Assert.Equal(2000u, await svc.GenderChangeCostGoldAsync());
    }

    private sealed class FakeSettings(params (string Key, string Value)[] rows) : ISettingRepository
    {
        private readonly Dictionary<string, string> _map = rows.ToDictionary(r => r.Key, r => r.Value);

        public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_map.TryGetValue(key, out var v) ? v : null);

        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(_map);
    }
}
