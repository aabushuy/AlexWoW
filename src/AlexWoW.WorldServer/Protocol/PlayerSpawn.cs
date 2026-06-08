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
    /// <paramref name="inventory"/> — предметы персонажа: видимая экипировка (slot 0..18) одевает
    /// модель у всех; guid'ы слотов (private) проставляются только себе.
    /// </summary>
    public static byte[] BuildCreateObject(Character c, float x, float y, float z, float o,
        uint serverTimeMs, bool isSelf, IReadOnlyList<InventoryItem>? inventory = null)
    {
        var w = new ByteWriter(256);

        w.UInt32(1);                                   // количество блоков
        w.UInt8(UpdateType.CreateObject2);
        PackedGuid.Write(w, c.Guid);
        w.UInt8(TypeId.Player);

        WriteMovementBlock(w, x, y, z, o, serverTimeMs, isSelf);
        BuildValues(c, inventory, isSelf).WriteTo(w);

        return w.ToArray();
    }

    /// <summary>
    /// SMSG_UPDATE_OBJECT с UPDATETYPE_VALUES — только поля видимой экипировки (slots 0..18).
    /// Для досылки экипировки на УЖЕ созданный у клиента объект игрока (повторный CREATE клиент
    /// игнорирует). Возвращает null, если надетых видимых предметов нет.
    /// </summary>
    public static byte[]? BuildEquipmentValuesUpdate(Character c, IReadOnlyList<InventoryItem> inventory)
    {
        var m = new UpdateMask();
        var any = false;
        foreach (var item in inventory)
            if (item.Bag == InventorySlots.MainBag
                && item.Slot >= InventorySlots.EquipmentStart && item.Slot < InventorySlots.EquipmentEnd)
            {
                m.SetUInt32(UpdateField.VisibleItemEntry(item.Slot), item.ItemEntry);
                any = true;
            }
        if (!any)
            return null;

        var w = new ByteWriter(64);
        w.UInt32(1);                  // количество блоков
        w.UInt8(UpdateType.Values);   // 0 — обновление значений существующего объекта
        PackedGuid.Write(w, c.Guid);
        m.WriteTo(w);
        return w.ToArray();
    }

    /// <summary>VALUES-апдейт с деньгами (PLAYER_FIELD_COINAGE) — после покупки/продажи. M6.2.</summary>
    public static byte[] BuildCoinageUpdate(ulong guid, uint money)
        => BuildPlayerValuesUpdate(guid, m => m.SetUInt32(UpdateField.PlayerFieldCoinage, money));

    /// <summary>VALUES-апдейт текущей маны (UNIT_FIELD_POWER1) — после каста/регена, только себе. M6.4.</summary>
    public static byte[] BuildPowerUpdate(ulong guid, uint power)
        => BuildPlayerValuesUpdate(guid, m => m.SetUInt32(UpdateField.UnitPower1, power));

    /// <summary>VALUES-апдейт с GUID предмета в слоте-контейнере (slot 0..38; 0 = пусто). M6.2.</summary>
    public static byte[] BuildInvSlotUpdate(ulong guid, int slot, ulong itemGuid)
        => BuildPlayerValuesUpdate(guid, m => m.SetUInt64(UpdateField.InvSlotGuid(slot), itemGuid));

    /// <summary>Каркас SMSG_UPDATE_OBJECT с одним VALUES-блоком для игрока (произвольный набор полей). M6.9.</summary>
    public static byte[] BuildPlayerValuesUpdate(ulong guid, Action<UpdateMask> fill)
    {
        var m = new UpdateMask();
        fill(m);
        var w = new ByteWriter(48);
        w.UInt32(1);
        w.UInt8(UpdateType.Values);
        PackedGuid.Write(w, guid);
        m.WriteTo(w);
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

    private static UpdateMask BuildValues(Character c, IReadOnlyList<InventoryItem>? inventory, bool isSelf)
    {
        var powerType = DisplayData.PowerTypeForClass(c.Class);
        var model = DisplayData.ModelForRace(c.Race, c.Gender);

        var m = new UpdateMask();
        m.SetUInt64(UpdateField.ObjectGuid, c.Guid);
        m.SetUInt32(UpdateField.ObjectType, TypeMask.PlayerObject);
        m.SetFloat(UpdateField.ObjectScaleX, 1.0f);

        m.SetBytes(UpdateField.UnitBytes0, c.Race, c.Class, c.Gender, powerType);
        // Здоровье игрока — упрощённо по уровню (точные статы по классу/уровню позже). M6.7: существо
        // отвечает ударом, текущий HP меняется в бою (VALUES-апдейты); в create спавним полным.
        var maxHealth = DisplayData.MaxHealthForLevel(c.Level);
        m.SetUInt32(UpdateField.UnitHealth, maxHealth);
        m.SetUInt32(UpdateField.UnitMaxHealth, maxHealth);
        // M6.4: мана-классам — пул из MaxManaForClass (расход спеллами, реген в тике); rage/energy
        // (powertype != 0) сохраняют дефолтные 100 (точная модель ресурсов — позже).
        var maxPower = DisplayData.MaxManaForClass(c.Class, c.Level);
        if (maxPower == 0)
            maxPower = 100;
        m.SetUInt32(UpdateField.UnitPower1, maxPower);
        m.SetUInt32(UpdateField.UnitMaxPower1, maxPower);
        m.SetUInt32(UpdateField.UnitLevel, c.Level);
        m.SetUInt32(UpdateField.UnitFactionTemplate, DisplayData.FactionForRace(c.Race));
        // UNIT_FLAG_PLAYER_CONTROLLED (0x8): без него клиент идёт по ветке CvC (существо-vs-существо) в
        // CanAttack и разрешает атаку лишь по ВРАЖДЕБНЫМ целям → нейтральных мобов нельзя бить (M7 #11).
        // С флагом — ветка PvC, нейтралы атакуемы. Сверено с TrinityCore IsValidAttackTarget (зеркало клиента).
        m.SetUInt32(UpdateField.UnitFlags, UnitFlags.PlayerControlled);
        m.SetUInt32(UpdateField.UnitDisplayId, model);
        m.SetUInt32(UpdateField.UnitNativeDisplayId, model);
        m.SetFloat(UpdateField.UnitBoundingRadius, 0.306f);
        m.SetFloat(UpdateField.UnitCombatReach, 1.5f);
        m.SetFloat(UpdateField.UnitModCastSpeed, 1.0f); // M6.4: иначе анимация каста ломается (масштаб ×0)

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

        // M6.2: деньги (private-поле) — только себе.
        if (isSelf)
            m.SetUInt32(UpdateField.PlayerFieldCoinage, c.Money);

        // M6.1: экипировка. Видимые предметы (entry) одевают модель — у всех наблюдателей;
        // guid'ы слотов-контейнеров — private-поля, шлём только себе.
        if (inventory is not null)
            foreach (var item in inventory)
            {
                if (item.Bag == InventorySlots.MainBag
                    && item.Slot >= InventorySlots.EquipmentStart && item.Slot < InventorySlots.EquipmentEnd)
                    m.SetUInt32(UpdateField.VisibleItemEntry(item.Slot), item.ItemEntry);

                if (isSelf && item.Bag == InventorySlots.MainBag)
                    m.SetUInt64(UpdateField.InvSlotGuid(item.Slot), ItemObject.ItemGuid(item.ItemGuid));
            }

        return m;
    }
}
