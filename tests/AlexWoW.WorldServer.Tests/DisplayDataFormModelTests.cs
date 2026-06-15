using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Tests;

/// <summary>Модель облика по форме (DisplayData.ModelForForm): §1 Метаморфоза ЧК + феральные формы друида.</summary>
public class DisplayDataFormModelTests
{
    [Theory]
    [InlineData((byte)1)]   // Human
    [InlineData((byte)2)]   // Orc
    [InlineData((byte)5)]   // Undead
    [InlineData((byte)7)]   // Gnome
    [InlineData((byte)8)]   // Troll
    [InlineData((byte)10)]  // Blood Elf
    public void Metamorphosis_DemonModel_ForAllWarlockRaces(byte race)
        => Assert.Equal(25277u, DisplayData.ModelForForm(race, form: 22));

    [Fact]
    public void NonFormShapeshift_NoModel_FallsBackToNative()
        => Assert.Equal(0u, DisplayData.ModelForForm(race: 1, form: 99)); // нет такой формы → 0 (нативная модель)
}
