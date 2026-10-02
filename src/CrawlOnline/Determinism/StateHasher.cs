using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CrawlOnline.Protocol;
using UnityEngine;

namespace CrawlOnline.Determinism
{
    internal static class StateHasher
    {
        public static StateHashRecord Calculate(uint tick)
        {
            var exact = new StableHash64();
            var quantized = new StableHash64();
            AddBoth(ref exact, ref quantized, unchecked((int)tick));

            try
            {
                AddBoth(ref exact, ref quantized, GameApi.InvokeStatic<int>("SystemGame", "GetLevel"));
                AddBoth(ref exact, ref quantized, GameApi.InvokeStatic<bool>("SystemGame", "GetGameInProgress"));
            }
            catch
            {
                AddBoth(ref exact, ref quantized, -1);
            }

            AddLevel(ref exact, ref quantized);

            try
            {
                object[] players = GameApi.GetPlayers();
                AddBoth(ref exact, ref quantized, players.Length);
                for (int i = 0; i < players.Length; i++)
                {
                    AddPlayer(ref exact, ref quantized, players[i]);
                }
            }
            catch
            {
                AddBoth(ref exact, ref quantized, -1);
            }

            AddRandomState(ref exact, ref quantized);

            return new StateHashRecord
            {
                Tick = tick,
                ExactHash = exact.Value,
                QuantizedHash = quantized.Value
            };
        }

        private static void AddRandomState(ref StableHash64 exact, ref StableHash64 quantized)
        {
            object state = UnityEngine.Random.state;
            FieldInfo[] fields = state.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Array.Sort(fields, delegate(FieldInfo left, FieldInfo right)
            {
                return string.CompareOrdinal(left.Name, right.Name);
            });
            AddBoth(ref exact, ref quantized, fields.Length);
            for (int i = 0; i < fields.Length; i++)
            {
                exact.AddString(fields[i].Name);
                quantized.AddString(fields[i].Name);
                object value = fields[i].GetValue(state);
                if (value is int)
                {
                    AddBoth(ref exact, ref quantized, (int)value);
                }
                else if (value is uint)
                {
                    exact.AddUInt32((uint)value);
                    quantized.AddUInt32((uint)value);
                }
                else
                {
                    throw new InvalidOperationException("Unsupported Unity random-state field " + fields[i].FieldType.FullName + ".");
                }
            }
        }

        internal static ulong CalculateRandomStateHash()
        {
            var exact = new StableHash64();
            var quantized = new StableHash64();
            AddRandomState(ref exact, ref quantized);
            return exact.Value;
        }

        private static void AddLevel(ref StableHash64 exact, ref StableHash64 quantized)
        {
            try
            {
                AddBoth(ref exact, ref quantized, GameApi.InvokeStatic<int>("SystemLevel", "GetEnemiesAlive"));
                object generator = GameApi.StaticProperty("SystemLevel", "MapGenerator");
                AddBoth(ref exact, ref quantized, generator != null);
                if (generator == null)
                {
                    return;
                }

                AddBoth(ref exact, ref quantized, GameApi.Invoke<bool>(generator, "GetFinished"));
                AddBoth(ref exact, ref quantized, GameApi.Invoke<int>(generator, "GetNumRooms"));
                object root = GameApi.Invoke(generator, "GetRootRoom");
                List<object> rooms = CollectRooms(root);
                AddBoth(ref exact, ref quantized, rooms.Count);
                rooms.Sort(CompareRooms);
                for (int i = 0; i < rooms.Count; i++)
                {
                    AddRoom(ref exact, ref quantized, rooms[i]);
                }

                object current = GameApi.InvokeStatic("SystemLevel", "GetCurrentRoom");
                AddBoth(ref exact, ref quantized, current != null);
                if (current != null)
                {
                    AddRoomKey(ref exact, ref quantized, current);
                }
            }
            catch
            {
                AddBoth(ref exact, ref quantized, -1);
            }
        }

        private static List<object> CollectRooms(object root)
        {
            var rooms = new List<object>();
            var pending = new Queue<object>();
            var seen = new HashSet<object>();
            if (root != null) pending.Enqueue(root);

            while (pending.Count > 0)
            {
                object room = pending.Dequeue();
                if (room == null || !seen.Add(room)) continue;
                rooms.Add(room);
                IEnumerable doors = GameApi.Field<IEnumerable>(room, "m_doors");
                if (doors == null) continue;
                foreach (object door in doors)
                {
                    object target = door == null ? null : GameApi.Field<object>(door, "m_room");
                    if (target != null && !seen.Contains(target)) pending.Enqueue(target);
                }
            }
            return rooms;
        }

        private static int CompareRooms(object left, object right)
        {
            Vector3 a = ((Component)left).transform.position;
            Vector3 b = ((Component)right).transform.position;
            int result = a.x.CompareTo(b.x);
            if (result == 0) result = a.y.CompareTo(b.y);
            if (result == 0) result = GameApi.Property<int>(left, "Depth").CompareTo(GameApi.Property<int>(right, "Depth"));
            return result;
        }

        private static void AddRoom(ref StableHash64 exact, ref StableHash64 quantized, object room)
        {
            AddRoomKey(ref exact, ref quantized, room);
            AddBoth(ref exact, ref quantized, GameApi.Invoke<bool>(room, "GetSeenRoom"));
            AddBoth(ref exact, ref quantized, GameApi.Invoke<int>(room, "GetTimesVisited"));
            AddBoth(ref exact, ref quantized, GameApi.Invoke<bool>(room, "IsFullyInRoom"));
            AddBoth(ref exact, ref quantized, GameApi.Invoke<bool>(room, "GetDoorsLocked"));
            AddBoth(ref exact, ref quantized, GameApi.Invoke<bool>(room, "HasContent"));
            AddBoth(ref exact, ref quantized, GameApi.Invoke<bool>(room, "HasStairs"));
            AddBoth(ref exact, ref quantized, GameApi.Invoke<bool>(room, "HasPortal"));
            AddBoth(ref exact, ref quantized, GameApi.Invoke<bool>(room, "HasPowerups"));
            AddBoth(ref exact, ref quantized, GameApi.Invoke<bool>(room, "HasTraps"));

            IEnumerable doors = GameApi.Field<IEnumerable>(room, "m_doors");
            var sorted = new List<object>();
            if (doors != null)
            {
                foreach (object door in doors) if (door != null) sorted.Add(door);
            }
            sorted.Sort(CompareDoors);
            AddBoth(ref exact, ref quantized, sorted.Count);
            for (int i = 0; i < sorted.Count; i++)
            {
                object door = sorted[i];
                AddBoth(ref exact, ref quantized, Convert.ToInt32(GameApi.Field<object>(door, "m_direction")));
                Vector2 position = GameApi.Field<Vector2>(door, "m_position");
                AddFloat(ref exact, ref quantized, position.x);
                AddFloat(ref exact, ref quantized, position.y);
                object target = GameApi.Field<object>(door, "m_room");
                AddBoth(ref exact, ref quantized, target != null);
                if (target != null) AddRoomKey(ref exact, ref quantized, target);
            }
        }

        private static int CompareDoors(object left, object right)
        {
            int result = Convert.ToInt32(GameApi.Field<object>(left, "m_direction")).CompareTo(
                Convert.ToInt32(GameApi.Field<object>(right, "m_direction")));
            Vector2 a = GameApi.Field<Vector2>(left, "m_position");
            Vector2 b = GameApi.Field<Vector2>(right, "m_position");
            if (result == 0) result = a.x.CompareTo(b.x);
            if (result == 0) result = a.y.CompareTo(b.y);
            return result;
        }

        private static void AddRoomKey(ref StableHash64 exact, ref StableHash64 quantized, object room)
        {
            Vector3 position = ((Component)room).transform.position;
            AddFloat(ref exact, ref quantized, position.x);
            AddFloat(ref exact, ref quantized, position.y);
            AddBoth(ref exact, ref quantized, GameApi.Property<int>(room, "Depth"));
        }

        private static void AddPlayer(ref StableHash64 exact, ref StableHash64 quantized, object playerData)
        {
            if (playerData == null)
            {
                AddBoth(ref exact, ref quantized, false);
                return;
            }

            AddBoth(ref exact, ref quantized, true);
            AddBoth(ref exact, ref quantized, GameApi.Property<int>(playerData, "Id"));
            AddBoth(ref exact, ref quantized, GameApi.Property<bool>(playerData, "IsActive"));
            AddBoth(ref exact, ref quantized, GameApi.Property<bool>(playerData, "IsHero"));
            AddBoth(ref exact, ref quantized, GameApi.Property<bool>(playerData, "IsAlive"));
            AddBoth(ref exact, ref quantized, GameApi.Property<bool>(playerData, "IsBot"));
            AddBoth(ref exact, ref quantized, System.Convert.ToInt32(GameApi.Property<object>(playerData, "ControllerId")));

            GameObject gameObject = GameApi.Property<GameObject>(playerData, "GameObject");
            AddBoth(ref exact, ref quantized, gameObject != null);
            if (gameObject == null)
            {
                return;
            }

            Vector3 position = gameObject.transform.position;
            AddFloat(ref exact, ref quantized, position.x);
            AddFloat(ref exact, ref quantized, position.y);

            Component player = gameObject.GetComponent(GameApi.Type("Player"));
            AddBoth(ref exact, ref quantized, player != null);
            if (player == null)
            {
                return;
            }

            AddBoth(ref exact, ref quantized, System.Convert.ToInt32(GameApi.Invoke(player, "GetState")));
            Component health = gameObject.GetComponent(GameApi.Type("Health"));
            AddBoth(ref exact, ref quantized, health != null);
            if (health != null)
            {
                AddFloat(ref exact, ref quantized, GameApi.Invoke<float>(health, "GetHealthCurrent"));
                AddFloat(ref exact, ref quantized, GameApi.Invoke<float>(health, "GetHealthMax"));
            }

            Rigidbody body = gameObject.GetComponent<Rigidbody>();
            AddBoth(ref exact, ref quantized, body != null);
            if (body != null)
            {
                AddFloat(ref exact, ref quantized, body.velocity.x);
                AddFloat(ref exact, ref quantized, body.velocity.y);
            }
        }

        private static void AddFloat(ref StableHash64 exact, ref StableHash64 quantized, float value)
        {
            exact.AddSingleBits(value);
            quantized.AddQuantized(value, 10000f);
        }

        private static void AddBoth(ref StableHash64 exact, ref StableHash64 quantized, int value)
        {
            exact.AddInt32(value);
            quantized.AddInt32(value);
        }

        private static void AddBoth(ref StableHash64 exact, ref StableHash64 quantized, bool value)
        {
            exact.AddBoolean(value);
            quantized.AddBoolean(value);
        }
    }
}
