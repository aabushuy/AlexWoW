using AlexWoW.Common.Network;

namespace AlexWoW.WorldServer.Protocol;

/// <summary>
/// Сборка <c>SMSG_UPDATE_OBJECT</c> для спавна гейм-объекта (<c>TYPEID_GAMEOBJECT</c>).
/// Статический объект: updateFlags = STATIONARY_POSITION (без LIVING) → движение-блок = x,y,z,o.
/// </summary>
public static class GameObjectUpdate
{
    public static byte[] BuildCreateObject(GoSpawn go)
    {
        var w = new ByteWriter(128);

        w.UInt32(1);                       // количество блоков
        w.UInt8(UpdateType.CreateObject2);
        PackedGuid.Write(w, go.Guid);
        w.UInt8(TypeId.GameObject);

        // Движение-блок: стационарная позиция + упакованный поворот.
        w.UInt16((ushort)(ObjectUpdateFlags.StationaryPosition | ObjectUpdateFlags.Rotation));
        w.Single(go.X).Single(go.Y).Single(go.Z).Single(go.O);
        w.UInt64((ulong)PackRotation(go.Rot0, go.Rot1, go.Rot2, go.Rot3)); // UPDATEFLAG_ROTATION

        BuildValues(go).WriteTo(w);
        return w.ToArray();
    }

    /// <summary>
    /// Упаковка кватerniona поворота GO в int64 (CMaNGOS QuaternionCompressed):
    /// <c>raw = Z | (Y&lt;&lt;21) | (X&lt;&lt;42)</c>; X·2^21 (22 бита), Y/Z·2^20 (по 21 биту), знак по w.
    /// </summary>
    private static long PackRotation(float qx, float qy, float qz, float qw)
    {
        var len = Math.Sqrt((double)qx * qx + (double)qy * qy + (double)qz * qz + (double)qw * qw);
        if (len > 0)
        {
            qx = (float)(qx / len);
            qy = (float)(qy / len);
            qz = (float)(qz / len);
            qw = (float)(qw / len);
        }

        var wSign = qw >= 0f ? 1 : -1;
        long x = ((int)(qx * 2097152.0) * wSign) & 0x3FFFFF; // 1<<21, маска 22 бита
        long y = ((int)(qy * 1048576.0) * wSign) & 0x1FFFFF; // 1<<20, маска 21 бит
        long z = ((int)(qz * 1048576.0) * wSign) & 0x1FFFFF;
        return (x << 42) | (y << 21) | z;
    }

    private static UpdateMask BuildValues(GoSpawn go)
    {
        var t = go.Template;

        var m = new UpdateMask();
        m.SetUInt64(UpdateField.ObjectGuid, go.Guid);
        m.SetUInt32(UpdateField.ObjectEntry, t.Entry);
        m.SetUInt32(UpdateField.ObjectType, TypeMask.GameObjectObject);
        m.SetFloat(UpdateField.ObjectScaleX, t.Size);

        m.SetUInt32(UpdateField.GoDisplayId, t.DisplayId);
        m.SetUInt32(UpdateField.GoFlags, t.Flags);
        m.SetFloat(UpdateField.GoParentRotation + 0, go.Rot0);
        m.SetFloat(UpdateField.GoParentRotation + 1, go.Rot1);
        m.SetFloat(UpdateField.GoParentRotation + 2, go.Rot2);
        m.SetFloat(UpdateField.GoParentRotation + 3, go.Rot3);
        m.SetUInt32(UpdateField.GoFaction, t.Faction);
        // bytes1: state=1 (GO_STATE_READY) | type | artKit=0 | animProgress=100
        m.SetBytes(UpdateField.GoBytes1, 1, (byte)t.Type, 0, 100);

        return m;
    }
}
