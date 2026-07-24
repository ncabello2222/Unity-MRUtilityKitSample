using System;
using System.IO;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace ShipBridgePrototype
{
    /// <summary>
    /// Persists confirmed bow orientation per room (UUID + size signature).
    /// </summary>
    public static class BridgeCalibrationStore
    {
        public const int FormatVersion = 1;
        private const string FileName = "bridge_orientation_calibration.json";

        [Serializable]
        public class Record
        {
            public int version = FormatVersion;
            public string roomUuid = string.Empty;
            public float roomSizeX;
            public float roomSizeY;
            public float roomSizeZ;
            public Vector3 forwardLocalInRoom = Vector3.forward;
            public float frontWallWidthM;
            public Vector3 frontWallCenterLocal;
        }

        [Serializable]
        private class RecordList
        {
            public Record[] records = Array.Empty<Record>();
        }

        public static string FilePath =>
            Path.Combine(Application.persistentDataPath, FileName);

        public static bool TryLoad(MRUKRoom room, out Record record)
        {
            record = null;
            if (room == null || !File.Exists(FilePath))
            {
                return false;
            }

            try
            {
                var json = File.ReadAllText(FilePath);
                var list = JsonUtility.FromJson<RecordList>(json);
                if (list?.records == null)
                {
                    return false;
                }

                var key = RoomKey(room);
                var size = room.GetRoomBounds().size;
                foreach (var entry in list.records)
                {
                    if (entry == null || entry.version != FormatVersion)
                    {
                        continue;
                    }

                    if (!string.Equals(entry.roomUuid, key, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!SizeMatches(entry, size))
                    {
                        Debug.LogWarning(
                            "[BridgeCalibrationStore] Room signature size changed; calibration discarded.");
                        return false;
                    }

                    record = entry;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BridgeCalibrationStore] Load failed: {ex.Message}");
            }

            return false;
        }

        public static void Save(MRUKRoom room, Vector3 forwardWorld, float frontWallWidthM, Vector3 frontWallCenterWorld)
        {
            if (room == null)
            {
                return;
            }

            var roomT = room.transform;
            var record = new Record
            {
                version = FormatVersion,
                roomUuid = RoomKey(room),
                roomSizeX = room.GetRoomBounds().size.x,
                roomSizeY = room.GetRoomBounds().size.y,
                roomSizeZ = room.GetRoomBounds().size.z,
                forwardLocalInRoom = roomT.InverseTransformDirection(
                    BridgeReferenceFrame.Flatten(forwardWorld).normalized),
                frontWallWidthM = frontWallWidthM,
                frontWallCenterLocal = roomT.InverseTransformPoint(frontWallCenterWorld)
            };

            var list = LoadAll();
            var key = record.roomUuid;
            var replaced = false;
            for (var i = 0; i < list.records.Length; i++)
            {
                if (list.records[i] != null &&
                    string.Equals(list.records[i].roomUuid, key, StringComparison.OrdinalIgnoreCase))
                {
                    list.records[i] = record;
                    replaced = true;
                    break;
                }
            }

            if (!replaced)
            {
                var next = new Record[list.records.Length + 1];
                Array.Copy(list.records, next, list.records.Length);
                next[list.records.Length] = record;
                list.records = next;
            }

            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                File.WriteAllText(FilePath, JsonUtility.ToJson(list, true));
                Debug.Log($"[BridgeCalibrationStore] Saved bow calibration → {FilePath}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BridgeCalibrationStore] Save failed: {ex.Message}");
            }
        }

        public static void ClearForRoom(MRUKRoom room)
        {
            if (room == null || !File.Exists(FilePath))
            {
                return;
            }

            var list = LoadAll();
            var key = RoomKey(room);
            var kept = Array.FindAll(
                list.records,
                r => r != null && !string.Equals(r.roomUuid, key, StringComparison.OrdinalIgnoreCase));
            list.records = kept;
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(list, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BridgeCalibrationStore] Clear failed: {ex.Message}");
            }
        }

        public static void ClearAll()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BridgeCalibrationStore] ClearAll failed: {ex.Message}");
            }
        }

        public static string RoomKey(MRUKRoom room)
        {
            if (room?.Anchor != null && room.Anchor.Uuid != Guid.Empty)
            {
                return room.Anchor.Uuid.ToString("N");
            }

            var size = room != null ? room.GetRoomBounds().size : Vector3.zero;
            return $"size_{size.x:F2}_{size.y:F2}_{size.z:F2}";
        }

        private static bool SizeMatches(Record entry, Vector3 size, float tol = 0.35f)
        {
            return Mathf.Abs(entry.roomSizeX - size.x) <= tol &&
                   Mathf.Abs(entry.roomSizeY - size.y) <= tol &&
                   Mathf.Abs(entry.roomSizeZ - size.z) <= tol;
        }

        private static RecordList LoadAll()
        {
            if (!File.Exists(FilePath))
            {
                return new RecordList();
            }

            try
            {
                var json = File.ReadAllText(FilePath);
                return JsonUtility.FromJson<RecordList>(json) ?? new RecordList();
            }
            catch
            {
                return new RecordList();
            }
        }
    }
}
