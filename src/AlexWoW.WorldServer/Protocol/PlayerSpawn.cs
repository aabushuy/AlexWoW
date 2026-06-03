using AlexWoW.Common.Network;
using AlexWoW.Database.Models;

namespace AlexWoW.WorldServer.Protocol;

/// <summary>
/// Сборка SMSG_UPDATE_OBJECT для спавна собственного игрока (UPDATETYPE_CREATE_OBJECT2).
/// Минимальный набор полей, достаточный для появления персонажа в мире.
/// </summary>
public static class PlayerSpawn
{
    // Базовые скорости (стандартные значения WoW).
    private const float WalkSpeed = 2.5f;
    private const float RunSpeed = 7.0f;
    private const float RunBackSpeed = 4.5f;
    private const float SwimSpeed = 4.722222f;
    private const float SwimBackSpeed = 2.5f;
    private const float FlightSpeed = 7.0f;
    private const float FlightBackSpeed = 4.5f;
    private const float TurnRate = 3.141594f;
    private const float PitchRate = 3.141594f;

    /// <summary>
    /// Спавн игрока. <paramref name="isSelf"/> = true — для самого владельца сессии (флаг Self);
    /// false — для отображения этого игрока другим (без Self). Координаты — живые (для соседей).
    /// </summary>
    public static byte[] BuildCreateObject(Character c, float x, float y, float z, float o,
        uint serverTimeMs, bool isSelf)
    {
        var w = new ByteWriter(256);

        w.UInt32(1);                                   // количество блоков
        w.UInt8(UpdateType.CreateObject2);
        PackedGuid.Write(w, c.Guid);
        w.UInt8(TypeId.Player);

        WriteMovementBlock(w, x, y, z, o, serverTimeMs, isSelf);
        BuildValues(c).WriteTo(w);

        return w.ToArray();
    }

    private static void WriteMovementBlock(ByteWriter w, float x, float y, float z, float o,
        uint serverTimeMs, bool isSelf)
    {
        var flags = ObjectUpdateFlags.Living | (isSelf ? ObjectUpdateFlags.Self : ObjectUpdateFlags.None);
        w.UInt16((ushort)flags);

        w.UInt32(0)            // movement flags
         .UInt16(0)            // movement flags 2
         .UInt32(serverTimeMs) // time
         .Single(x).Single(y).Single(z)
         .Single(o)            // orientation
         .UInt32(0);           // fall time

        // 9 скоростей
        w.Single(WalkSpeed).Single(RunSpeed).Single(RunBackSpeed)
         .Single(SwimSpeed).Single(SwimBackSpeed)
         .Single(FlightSpeed).Single(FlightBackSpeed)
         .Single(TurnRate).Single(PitchRate);
    }

    private static UpdateMask BuildValues(Character c)
    {
        var powerType = DisplayData.PowerTypeForClass(c.Class);
        var model = DisplayData.ModelForRace(c.Race, c.Gender);

        var m = new UpdateMask();
        m.SetUInt64(UpdateField.ObjectGuid, c.Guid);
        m.SetUInt32(UpdateField.ObjectType, TypeMask.PlayerObject);
        m.SetFloat(UpdateField.ObjectScaleX, 1.0f);

        m.SetBytes(UpdateField.UnitBytes0, c.Race, c.Class, c.Gender, powerType);
        m.SetUInt32(UpdateField.UnitHealth, 100);
        m.SetUInt32(UpdateField.UnitMaxHealth, 100);
        m.SetUInt32(UpdateField.UnitPower1, 100);
        m.SetUInt32(UpdateField.UnitMaxPower1, 100);
        m.SetUInt32(UpdateField.UnitLevel, c.Level);
        m.SetUInt32(UpdateField.UnitFactionTemplate, DisplayData.FactionForRace(c.Race));
        m.SetUInt32(UpdateField.UnitDisplayId, model);
        m.SetUInt32(UpdateField.UnitNativeDisplayId, model);
        m.SetFloat(UpdateField.UnitBoundingRadius, 0.306f);
        m.SetFloat(UpdateField.UnitCombatReach, 1.5f);

        m.SetBytes(UpdateField.PlayerBytes, c.Skin, c.Face, c.HairStyle, c.HairColor);
        m.SetBytes(UpdateField.PlayerBytes2, c.FacialHair, 0, 0, 0);
        m.SetBytes(UpdateField.PlayerBytes3, c.Gender, 0, 0, 0);

        // Языковые навыки — иначе клиент блокирует /say («не знаете языка») и скилл-таб пуст.
        var languageSkills = LanguageSkills.ForRace(c.Race);
        for (var slot = 0; slot < languageSkills.Count; slot++)
        {
            var baseIdx = UpdateField.PlayerSkillInfo11 + slot * 3;
            m.SetUInt32(baseIdx, (uint)languageSkills[slot]); // skillId | step(0)
            m.SetUInt32(baseIdx + 1, 300u | (300u << 16));    // value | max
            m.SetUInt32(baseIdx + 2, 0);                      // временный/постоянный бонус
        }

        return m;
    }
}
