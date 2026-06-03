using AlexWoW.Common.Network;

namespace AlexWoW.WorldServer.Protocol;

/// <summary>
/// Сборка <c>SMSG_UPDATE_OBJECT</c> для спавна существа (NPC) — <c>UPDATETYPE_CREATE_OBJECT2</c>,
/// <c>TYPEID_UNIT</c>, без флага Self. Минимум полей, достаточный для отображения NPC у клиента.
/// </summary>
public static class CreatureUpdate
{
    // Стандартные скорости WoW (как у игрока — см. PlayerSpawn; единая модель будет в AlexWoW.Game, M5.2).
    private const float WalkSpeed = 2.5f;
    private const float RunSpeed = 7.0f;
    private const float RunBackSpeed = 4.5f;
    private const float SwimSpeed = 4.722222f;
    private const float SwimBackSpeed = 2.5f;
    private const float FlightSpeed = 7.0f;
    private const float FlightBackSpeed = 4.5f;
    private const float TurnRate = 3.141594f;
    private const float PitchRate = 3.141594f;

    public static byte[] BuildCreateObject(NpcSpawn spawn, uint serverTimeMs)
    {
        var w = new ByteWriter(192);

        w.UInt32(1);                       // количество блоков
        w.UInt8(UpdateType.CreateObject2);
        PackedGuid.Write(w, spawn.Guid);
        w.UInt8(TypeId.Unit);

        WriteMovementBlock(w, spawn, serverTimeMs);
        BuildValues(spawn).WriteTo(w);

        return w.ToArray();
    }

    private static void WriteMovementBlock(ByteWriter w, NpcSpawn spawn, uint serverTimeMs)
    {
        w.UInt16((ushort)ObjectUpdateFlags.Living); // Living, но без Self

        w.UInt32(0)            // movement flags
         .UInt16(0)            // movement flags 2
         .UInt32(serverTimeMs) // time
         .Single(spawn.X).Single(spawn.Y).Single(spawn.Z)
         .Single(spawn.O)      // orientation
         .UInt32(0);           // fall time

        w.Single(WalkSpeed).Single(RunSpeed).Single(RunBackSpeed)
         .Single(SwimSpeed).Single(SwimBackSpeed)
         .Single(FlightSpeed).Single(FlightBackSpeed)
         .Single(TurnRate).Single(PitchRate);
    }

    private static UpdateMask BuildValues(NpcSpawn spawn)
    {
        var t = spawn.Template;

        var m = new UpdateMask();
        m.SetUInt64(UpdateField.ObjectGuid, spawn.Guid);
        m.SetUInt32(UpdateField.ObjectEntry, t.Entry);
        m.SetUInt32(UpdateField.ObjectType, TypeMask.UnitObject);
        m.SetFloat(UpdateField.ObjectScaleX, 1.0f);

        m.SetUInt32(UpdateField.UnitHealth, 100);
        m.SetUInt32(UpdateField.UnitMaxHealth, 100);
        m.SetUInt32(UpdateField.UnitLevel, t.Level);
        m.SetUInt32(UpdateField.UnitFactionTemplate, t.Faction);
        m.SetUInt32(UpdateField.UnitDisplayId, t.DisplayId);
        m.SetUInt32(UpdateField.UnitNativeDisplayId, t.DisplayId);
        m.SetFloat(UpdateField.UnitBoundingRadius, 0.306f);
        m.SetFloat(UpdateField.UnitCombatReach, 1.5f);

        return m;
    }
}
