using AlexWoW.WorldServer.Handlers;
using AlexWoW.WorldServer.Net.SessionState;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Tests;

/// <summary>Каркас рун DK (RUNE.1): кодирование SMSG_RESYNC_RUNES (тип + пройденный КД 0..255).</summary>
public sealed class RuneScaffoldTests
{
    private const int MaxCd = 10000;

    /// <summary>Стандартная раскладка DK, все готовы: 2 крови, 2 нечестия, 2 мороза.</summary>
    private static RuneSlot[] FullRunes() =>
    [
        new() { BaseType = RuneType.Blood, CurrentType = RuneType.Blood },
        new() { BaseType = RuneType.Blood, CurrentType = RuneType.Blood },
        new() { BaseType = RuneType.Unholy, CurrentType = RuneType.Unholy },
        new() { BaseType = RuneType.Unholy, CurrentType = RuneType.Unholy },
        new() { BaseType = RuneType.Frost, CurrentType = RuneType.Frost },
        new() { BaseType = RuneType.Frost, CurrentType = RuneType.Frost },
    ];

    [Fact]
    public void CanAfford_true_when_required_type_available()
    {
        // Death Strike — 1 мороз + 1 нечестие; в полном наборе хватает.
        Assert.True(RuneService.CanAfford(FullRunes(), new RuneService.RuneCost(0, 1, 1, 15)));
    }

    [Fact]
    public void CanAfford_false_when_type_on_cooldown()
    {
        var runes = FullRunes();
        runes[4].CooldownMs = 5000; // обе руны мороза на КД
        runes[5].CooldownMs = 5000;
        Assert.False(RuneService.CanAfford(runes, new RuneService.RuneCost(0, 1, 0, 10))); // нужен мороз
    }

    [Fact]
    public void Death_rune_substitutes_any_type()
    {
        var runes = FullRunes();
        runes[4].CooldownMs = 5000; // оба мороза недоступны
        runes[5].CooldownMs = 5000;
        runes[0].CurrentType = RuneType.Death; // но руна крови сконвертирована в death
        Assert.True(RuneService.CanAfford(runes, new RuneService.RuneCost(0, 1, 0, 10))); // death платит за мороз
    }

    [Fact]
    public void Resync_encodes_count_then_type_and_passed_cooldown_per_rune()
    {
        // Раскладка DK: Blood,Blood,Unholy,Unholy,Frost,Frost — все готовы (КД 0 → passed 255).
        var runes = new (byte, int)[]
        {
            (0, 0), (0, 0), (1, 0), (1, 0), (2, 0), (2, 0),
        };
        var bytes = CombatPackets.BuildResyncRunes(runes, MaxCd);

        // u32 count + 6×(u8 type + u8 passed) = 4 + 12.
        Assert.Equal(4 + 6 * 2, bytes.Length);
        Assert.Equal(6u, BitConverter.ToUInt32(bytes, 0));
        // Первая руна: type=Blood(0), passed=255 (готова).
        Assert.Equal(0, bytes[4]);
        Assert.Equal(255, bytes[5]);
        // Третья руна: type=Unholy(1).
        Assert.Equal(1, bytes[8]);
    }

    [Theory]
    [InlineData(0, 255)]       // готова — весь КД пройден
    [InlineData(10000, 0)]     // только что потрачена — 0 пройдено
    [InlineData(5000, 128)]    // половина КД — ~середина шкалы (255 − 127)
    public void Passed_cooldown_byte_is_inverse_of_remaining(int remainingMs, byte expectedPassed)
    {
        var bytes = CombatPackets.BuildResyncRunes([(0, remainingMs)], MaxCd);
        Assert.Equal(expectedPassed, bytes[5]);
    }
}
