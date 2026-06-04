using AlexWoW.Common.Network;
using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Protocol;

/// <summary>
/// Сборка <c>SMSG_UPDATE_OBJECT</c> для существа (NPC): полный спавн (<c>CREATE_OBJECT2</c>,
/// <c>TYPEID_UNIT</c>) и частичные VALUES-апдейты (здоровье в бою, M6.3).
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

    public static byte[] BuildCreateObject(WorldCreature creature, uint serverTimeMs)
    {
        var w = new ByteWriter(192);

        w.UInt32(1);                       // количество блоков
        w.UInt8(UpdateType.CreateObject2);
        PackedGuid.Write(w, creature.Guid);
        w.UInt8(TypeId.Unit);

        WriteMovementBlock(w, creature, serverTimeMs);
        BuildValues(creature).WriteTo(w);

        return w.ToArray();
    }

    /// <summary>VALUES-апдейт здоровья существа (UNIT_FIELD_HEALTH) — в бою/при смерти/респавне. M6.3.</summary>
    public static byte[] BuildHealthUpdate(ulong guid, uint health)
        => BuildValuesUpdate(guid, m => m.SetUInt32(UpdateField.UnitHealth, health));

    /// <summary>VALUES-апдейт динамических флагов существа (UNIT_DYNAMIC_FLAGS) — LOOTABLE труп. M6.6.</summary>
    public static byte[] BuildDynamicFlagsUpdate(ulong guid, uint flags)
        => BuildValuesUpdate(guid, m => m.SetUInt32(UpdateField.UnitDynamicFlags, flags));

    /// <summary>Каркас SMSG_UPDATE_OBJECT с одним VALUES-блоком для существа (произвольный набор полей).</summary>
    private static byte[] BuildValuesUpdate(ulong guid, Action<UpdateMask> fill)
    {
        var m = new UpdateMask();
        fill(m);
        var w = new ByteWriter(32);
        w.UInt32(1);
        w.UInt8(UpdateType.Values);
        PackedGuid.Write(w, guid);
        m.WriteTo(w);
        return w.ToArray();
    }

    private static void WriteMovementBlock(ByteWriter w, WorldCreature creature, uint serverTimeMs)
    {
        w.UInt16((ushort)ObjectUpdateFlags.Living); // Living, но без Self

        w.UInt32(0)            // movement flags
         .UInt16(0)            // movement flags 2
         .UInt32(serverTimeMs) // time
         .Single(creature.X).Single(creature.Y).Single(creature.Z)
         .Single(creature.O)   // orientation
         .UInt32(0);           // fall time

        w.Single(WalkSpeed).Single(RunSpeed).Single(RunBackSpeed)
         .Single(SwimSpeed).Single(SwimBackSpeed)
         .Single(FlightSpeed).Single(FlightBackSpeed)
         .Single(TurnRate).Single(PitchRate);
    }

    private static UpdateMask BuildValues(WorldCreature creature)
    {
        var t = creature.Template;

        var m = new UpdateMask();
        m.SetUInt64(UpdateField.ObjectGuid, creature.Guid);
        m.SetUInt32(UpdateField.ObjectEntry, t.Entry);
        m.SetUInt32(UpdateField.ObjectType, TypeMask.UnitObject);
        m.SetFloat(UpdateField.ObjectScaleX, t.Scale);

        m.SetBytes(UpdateField.UnitBytes0, 0, t.UnitClass, 0, 0); // race=0|class|gender=0|powertype=0
        m.SetUInt32(UpdateField.UnitHealth, creature.Health);
        m.SetUInt32(UpdateField.UnitMaxHealth, creature.MaxHealth);
        m.SetUInt32(UpdateField.UnitLevel, t.Level);
        m.SetUInt32(UpdateField.UnitFactionTemplate, t.Faction);
        m.SetUInt32(UpdateField.UnitNpcFlags, t.NpcFlags);
        m.SetUInt32(UpdateField.UnitDisplayId, t.DisplayId);
        m.SetUInt32(UpdateField.UnitNativeDisplayId, t.DisplayId);
        m.SetFloat(UpdateField.UnitBoundingRadius, 0.306f);
        m.SetFloat(UpdateField.UnitCombatReach, 1.5f);

        return m;
    }
}
