using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Tests;

/// <summary>
/// Data-driven автотест маппинга по фикстуре реальных <c>spell_template</c> (M12.7, тир-1 Spell QA):
/// прогоняет <see cref="SpellCatalog.FromTemplate"/> по всем известным классовым спеллам (фикстура из mangos),
/// проверяет структурные инварианты и golden-дайджест (любой дрейф разбора → падение теста). Фикстуру
/// перегенерировать одноразовым дампом (tools/throwaway) при обновлении дампа мира.
/// </summary>
public class SpellCatalogFixtureTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Golden-дайджест парсинга всей фикстуры. Обновлять осознанно (изменение парсера/дампа):
    /// при падении взять «Actual» из сообщения и вписать сюда, предварительно проверив, что изменение ожидаемо.</summary>
    private const string ExpectedDigest = "5c29d011491e7b7e51b767bd139789b5bc84919e3104755f26e9e55897685d2f";

    private static List<SpellTemplateData> LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "spell_template_known.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<SpellTemplateData>>(json, JsonOpts) ?? [];
    }

    [Fact]
    public void Fixture_NotEmpty()
    {
        var rows = LoadFixture();
        Assert.True(rows.Count > 1000, $"ожидалось много спеллов, получено {rows.Count}");
    }

    [Fact]
    public void FromTemplate_StructuralInvariants_HoldForAllKnownSpells()
    {
        var rows = LoadFixture();
        var violations = new List<string>();

        foreach (var t in rows.OrderBy(r => r.Id))
        {
            SpellCatalog.SpellInfo info;
            try
            {
                info = SpellCatalog.FromTemplate(t);
            }
            catch (Exception ex)
            {
                violations.Add($"spell {t.Id}: исключение в FromTemplate — {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            if (info.MinAmount > info.MaxAmount)
                violations.Add($"spell {t.Id}: min {info.MinAmount} > max {info.MaxAmount}");
            if (info.DirectEffectIndex > 3)
                violations.Add($"spell {t.Id}: DirectEffectIndex {info.DirectEffectIndex} вне 0..3");
            if (info.PeriodicEffectIndex > 3)
                violations.Add($"spell {t.Id}: PeriodicEffectIndex {info.PeriodicEffectIndex} вне 0..3");
            if (info.WeaponPercent > 0 && !info.WeaponDamage)
                violations.Add($"spell {t.Id}: WeaponPercent>0, но WeaponDamage=false");
            if (info.CreateItemId != 0 && info.CreateItemCount < 1)
                violations.Add($"spell {t.Id}: CreateItemId задан, но count<1");
            if (info.Reagents is { } reg && reg.Any(r => r.Item == 0 || r.Count == 0))
                violations.Add($"spell {t.Id}: реагент с нулевым item/count");
        }

        Assert.True(violations.Count == 0,
            $"Нарушено инвариантов: {violations.Count}\n{string.Join('\n', violations.Take(40))}");
    }

    [Fact]
    public void FromTemplate_GoldenDigest_Stable()
    {
        var rows = LoadFixture();
        var sb = new StringBuilder();
        foreach (var t in rows.OrderBy(r => r.Id))
        {
            var i = SpellCatalog.FromTemplate(t);
            sb.Append(t.Id).Append('|')
              .Append(i.School).Append('|').Append(i.MinAmount).Append('|').Append(i.MaxAmount).Append('|')
              .Append(i.CastMs).Append('|').Append(i.ManaCost).Append('|').Append(i.ManaCostPct).Append('|')
              .Append(i.CooldownMs).Append('|').Append(i.GcdMs).Append('|').Append(i.PowerType).Append('|')
              .Append(i.IsHeal ? 1 : 0).Append('|').Append(i.WeaponDamage ? 1 : 0).Append('|').Append(i.WeaponPercent).Append('|')
              .Append(i.Periodic ? 1 : 0).Append('|').Append(i.PeriodicHeal ? 1 : 0).Append('|')
              .Append(i.TickAmount).Append('|').Append(i.TickIntervalMs).Append('|').Append(i.AuraDurationMs).Append('|')
              .Append(i.AuraBuff ? 1 : 0).Append('|').Append(i.AuraPositive ? 1 : 0).Append('|').Append(i.HealthBonus).Append('|')
              .Append((int)i.Movement).Append('|').Append(i.TriggerSpellId).Append('|')
              .Append(i.CreateItemId).Append('|').Append(i.CreateItemCount).Append('|')
              .Append(i.FamilyName).Append('|').Append(i.FamilyFlags).Append('|').Append(i.FamilyFlags2).Append('|')
              .Append(i.DirectEffectIndex).Append('|').Append(i.PeriodicEffectIndex).Append('\n');
        }
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
        Assert.Equal(ExpectedDigest, digest);
    }
}
