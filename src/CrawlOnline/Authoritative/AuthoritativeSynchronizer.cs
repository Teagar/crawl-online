using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using CrawlOnline.Determinism;
using CrawlOnline.Protocol;
using UnityEngine;

namespace CrawlOnline.Authoritative
{
    internal sealed class AuthoritativeSynchronizer : IDisposable
    {
        private const int SnapshotIntervalFrames = 6;
        private const float UnitsPerStep = 10000f;
        private readonly SteamLobbySession session;
        private readonly ManualLogSource log;
        private readonly AuthoritativeInputBridge inputBridge = new AuthoritativeInputBridge();
        private readonly Dictionary<GameObject, uint> hostEnemyIds = new Dictionary<GameObject, uint>();
        private readonly Dictionary<uint, GameObject> clientEnemies = new Dictionary<uint, GameObject>();
        private WorldSnapshot pendingSnapshot;
        private uint tick;
        private readonly uint[] lastInputSequences = new uint[4];
        private uint transitionGeneration;
        private string lastWorldKey;
        private string lastMismatchKey;
        private string lastRuntimeError;
        private uint nextEnemyId = 1;

        public AuthoritativeSynchronizer(SteamLobbySession lobbySession, ManualLogSource logSource)
        {
            session = lobbySession;
            log = logSource;
            session.InputReceived += OnInputReceived;
            session.SnapshotReceived += OnSnapshotReceived;
        }

        public void LateTick()
        {
            tick++;
            if (!session.InLobby)
            {
                inputBridge.Clear();
                hostEnemyIds.Clear();
                clientEnemies.Clear();
                nextEnemyId = 1;
                pendingSnapshot = null;
                return;
            }

            if (session.IsAuthoritativeHost)
            {
                if (tick % SnapshotIntervalFrames == 0)
                {
                    try
                    {
                        session.BroadcastSnapshot(CaptureSnapshot());
                        lastRuntimeError = null;
                    }
                    catch (Exception exception)
                    {
                        ReportRuntimeError("capture", exception);
                    }
                }
            }
            else if (session.LocalSlot != byte.MaxValue)
            {
                InputFrame input;
                try
                {
                    if (TryCaptureInput(session.LocalSlot, out input))
                    {
                        input.Tick = tick;
                        session.SendLocalInput(input);
                    }
                }
                catch (Exception exception) { ReportRuntimeError("input", exception); }
                if (pendingSnapshot != null)
                {
                    try
                    {
                        if (ApplySnapshot(pendingSnapshot))
                            session.AcknowledgeSnapshot(pendingSnapshot.Sequence);
                    }
                    catch (Exception exception) { ReportRuntimeError("correction", exception); }
                    pendingSnapshot = null;
                }
            }
        }

        public void Dispose()
        {
            session.InputReceived -= OnInputReceived;
            session.SnapshotReceived -= OnSnapshotReceived;
            inputBridge.Dispose();
        }

        private void OnInputReceived(SessionInputFrame input)
        {
            lastInputSequences[input.Input.PlayerId] = input.Sequence;
            inputBridge.Set(input);
        }

        private void OnSnapshotReceived(WorldSnapshot snapshot)
        {
            pendingSnapshot = snapshot;
        }

        private WorldSnapshot CaptureSnapshot()
        {
            var snapshot = new WorldSnapshot
            {
                HostTick = tick,
                LastInputSequences = (uint[])lastInputSequences.Clone(),
                RandomStateHash = StateHasher.CalculateRandomStateHash()
            };
            try
            {
                snapshot.Level = GameApi.InvokeStatic<int>("SystemGame", "GetLevel");
                if (GameApi.InvokeStatic<bool>("SystemGame", "GetGameInProgress"))
                    snapshot.Flags |= WorldSnapshotFlags.GameInProgress;
            }
            catch { snapshot.Level = -1; }

            object room = null;
            try { room = GameApi.InvokeStatic("SystemLevel", "GetCurrentRoom"); }
            catch { }
            if (room != null)
            {
                snapshot.Flags |= WorldSnapshotFlags.HasCurrentRoom;
                Vector3 roomPosition = ((Component)room).transform.position;
                snapshot.RoomX = Quantize(roomPosition.x);
                snapshot.RoomY = Quantize(roomPosition.y);
                snapshot.RoomDepth = GameApi.Property<int>(room, "Depth");
            }
            string worldKey = snapshot.Level + ":" + snapshot.RoomX + ":" + snapshot.RoomY + ":" + snapshot.RoomDepth;
            if (lastWorldKey != null && lastWorldKey != worldKey) transitionGeneration++;
            lastWorldKey = worldKey;
            snapshot.TransitionGeneration = transitionGeneration;

            var players = new List<PlayerSnapshot>();
            object[] gamePlayers;
            try { gamePlayers = GameApi.GetPlayers(); }
            catch { gamePlayers = new object[0]; }
            for (int i = 0; i < gamePlayers.Length && i < 4; i++)
            {
                if (gamePlayers[i] != null) players.Add(CapturePlayer(gamePlayers[i]));
            }
            players.Sort(delegate(PlayerSnapshot left, PlayerSnapshot right)
            {
                return left.Slot.CompareTo(right.Slot);
            });
            snapshot.Players = players.ToArray();
            snapshot.Enemies = CaptureEnemies(gamePlayers);
            return snapshot;
        }

        private EnemySnapshot[] CaptureEnemies(object[] gamePlayers)
        {
            List<GameObject> enemies = GetMonsterObjects(gamePlayers);
            var snapshots = new List<EnemySnapshot>();
            for (int i = 0; i < enemies.Count; i++)
            {
                GameObject enemy = enemies[i];
                uint id;
                if (!hostEnemyIds.TryGetValue(enemy, out id))
                {
                    id = nextEnemyId++;
                    if (id == 0) id = nextEnemyId++;
                    hostEnemyIds.Add(enemy, id);
                }
                snapshots.Add(CaptureEnemy(enemy, id));
            }
            snapshots.Sort(delegate(EnemySnapshot left, EnemySnapshot right)
            {
                return left.Id.CompareTo(right.Id);
            });
            return snapshots.ToArray();
        }

        private static PlayerSnapshot CapturePlayer(object playerData)
        {
            var snapshot = new PlayerSnapshot { Slot = (byte)GameApi.Property<int>(playerData, "Id") };
            if (GameApi.Property<bool>(playerData, "IsActive")) snapshot.Flags |= PlayerSnapshotFlags.Active;
            if (GameApi.Property<bool>(playerData, "IsHero")) snapshot.Flags |= PlayerSnapshotFlags.Hero;
            if (GameApi.Property<bool>(playerData, "IsAlive")) snapshot.Flags |= PlayerSnapshotFlags.Alive;
            if (GameApi.Property<bool>(playerData, "IsBot")) snapshot.Flags |= PlayerSnapshotFlags.Bot;
            GameObject gameObject = GameApi.Property<GameObject>(playerData, "GameObject");
            if (gameObject == null) return snapshot;
            snapshot.Flags |= PlayerSnapshotFlags.Present;
            snapshot.PositionX = Quantize(gameObject.transform.position.x);
            snapshot.PositionY = Quantize(gameObject.transform.position.y);
            Rigidbody body = gameObject.GetComponent<Rigidbody>();
            if (body != null)
            {
                snapshot.VelocityX = Quantize(body.velocity.x);
                snapshot.VelocityY = Quantize(body.velocity.y);
            }
            Component player = gameObject.GetComponent(GameApi.Type("Player"));
            if (player != null) snapshot.State = Convert.ToInt16(GameApi.Invoke(player, "GetState"));
            Component health = gameObject.GetComponent(GameApi.Type("Health"));
            if (health != null)
            {
                snapshot.HealthCurrent = Quantize(GameApi.Invoke<float>(health, "GetHealthCurrent"));
                snapshot.HealthMaximum = Quantize(GameApi.Invoke<float>(health, "GetHealthMax"));
            }
            return snapshot;
        }

        private static bool TryCaptureInput(byte slot, out InputFrame input)
        {
            input = new InputFrame { PlayerId = slot };
            object[] players;
            try { players = GameApi.GetPlayers(); }
            catch { return false; }
            object player = FindPlayerBySlot(players, slot);
            if (player == null) return false;
            Vector2 move = GameApi.Invoke<Vector2>(player, "GetInputMove");
            input.MoveX = QuantizeAxis(move.x);
            input.MoveY = QuantizeAxis(move.y);
            input.HeldButtons = Buttons(player, "GetInputAction", "GetInputBack", "GetInputStart");
            input.DownButtons = Buttons(player, "GetInputDownAction", "GetInputDownBack", "GetInputDownStart");
            input.UpButtons = Buttons(player, "GetInputUpAction", "GetInputUpBack", "GetInputUpStart");
            return true;
        }

        private bool ApplySnapshot(WorldSnapshot snapshot)
        {
            int localLevel;
            bool localGameInProgress = false;
            try
            {
                localLevel = GameApi.InvokeStatic<int>("SystemGame", "GetLevel");
                localGameInProgress = GameApi.InvokeStatic<bool>("SystemGame", "GetGameInProgress");
            }
            catch { localLevel = -1; }
            if (localLevel != snapshot.Level)
                return RejectMismatch("level:" + localLevel + "->" + snapshot.Level);
            bool hostGameInProgress = (snapshot.Flags & WorldSnapshotFlags.GameInProgress) != 0;
            if (localGameInProgress != hostGameInProgress)
                return RejectMismatch("game-in-progress:" + localGameInProgress + "->" + hostGameInProgress);

            object localRoom = null;
            try { localRoom = GameApi.InvokeStatic("SystemLevel", "GetCurrentRoom"); }
            catch { }
            bool hostHasRoom = (snapshot.Flags & WorldSnapshotFlags.HasCurrentRoom) != 0;
            if ((localRoom != null) != hostHasRoom)
                return RejectMismatch("current-room-presence");
            if (localRoom != null)
            {
                Vector3 roomPosition = ((Component)localRoom).transform.position;
                int roomX = Quantize(roomPosition.x);
                int roomY = Quantize(roomPosition.y);
                int roomDepth = GameApi.Property<int>(localRoom, "Depth");
                if (roomX != snapshot.RoomX || roomY != snapshot.RoomY || roomDepth != snapshot.RoomDepth)
                    return RejectMismatch("room:" + roomX + ":" + roomY + ":" + roomDepth + "->" +
                                          snapshot.RoomX + ":" + snapshot.RoomY + ":" + snapshot.RoomDepth);
            }

            object[] players;
            try { players = GameApi.GetPlayers(); }
            catch { return false; }

            for (int i = 0; i < snapshot.Players.Length; i++)
            {
                PlayerSnapshot authoritative = snapshot.Players[i];
                object playerData = FindPlayerBySlot(players, authoritative.Slot);
                if (playerData == null) return RejectMismatch("missing-player:" + authoritative.Slot);
                PlayerSnapshot local = CapturePlayer(playerData);
                PlayerSnapshotFlags lifecycle = PlayerSnapshotFlags.Active | PlayerSnapshotFlags.Hero |
                    PlayerSnapshotFlags.Alive | PlayerSnapshotFlags.Bot | PlayerSnapshotFlags.Present;
                if ((local.Flags & lifecycle) != (authoritative.Flags & lifecycle))
                    return RejectMismatch("player-lifecycle:" + authoritative.Slot);
                if (local.State != authoritative.State)
                    return RejectMismatch("player-state:" + authoritative.Slot + ":" + local.State + "->" + authoritative.State);
                if (local.HealthMaximum != authoritative.HealthMaximum)
                    return RejectMismatch("player-max-health:" + authoritative.Slot);
            }

            List<GameObject> localEnemies = GetMonsterObjects(players);
            if (localEnemies.Count != snapshot.Enemies.Length)
                return RejectMismatch("enemy-count:" + localEnemies.Count + "->" + snapshot.Enemies.Length);
            var claimedEnemies = new HashSet<GameObject>();
            var resolvedEnemies = new GameObject[snapshot.Enemies.Length];
            for (int i = 0; i < snapshot.Enemies.Length; i++)
            {
                EnemySnapshot authoritative = snapshot.Enemies[i];
                GameObject enemy;
                if (!clientEnemies.TryGetValue(authoritative.Id, out enemy) || enemy == null ||
                    ArchetypeHash(enemy) != authoritative.ArchetypeHash)
                {
                    enemy = MatchEnemy(authoritative, localEnemies, claimedEnemies);
                    if (enemy == null) return RejectMismatch("enemy-identity:" + authoritative.Id);
                    clientEnemies[authoritative.Id] = enemy;
                }
                if (!claimedEnemies.Add(enemy)) return RejectMismatch("enemy-duplicate:" + authoritative.Id);
                EnemySnapshot local = CaptureEnemy(enemy, authoritative.Id);
                if (local.Flags != authoritative.Flags || local.State != authoritative.State ||
                    local.HealthMaximum != authoritative.HealthMaximum)
                    return RejectMismatch("enemy-lifecycle:" + authoritative.Id);
                resolvedEnemies[i] = enemy;
            }

            lastMismatchKey = null;
            for (int i = 0; i < snapshot.Players.Length; i++)
            {
                PlayerSnapshot authoritative = snapshot.Players[i];
                if ((authoritative.Flags & PlayerSnapshotFlags.Present) == 0) continue;
                object playerData = FindPlayerBySlot(players, authoritative.Slot);
                GameObject gameObject = GameApi.Property<GameObject>(playerData, "GameObject");
                if (gameObject == null) continue;
                Vector3 position = gameObject.transform.position;
                position.x = Expand(authoritative.PositionX);
                position.y = Expand(authoritative.PositionY);
                gameObject.transform.position = position;
                Rigidbody body = gameObject.GetComponent<Rigidbody>();
                if (body != null)
                    body.velocity = new Vector3(Expand(authoritative.VelocityX), Expand(authoritative.VelocityY), body.velocity.z);
                Component health = gameObject.GetComponent(GameApi.Type("Health"));
                if (health != null && authoritative.HealthCurrent > 0)
                {
                    float current = GameApi.Invoke<float>(health, "GetHealthCurrent");
                    float target = Expand(authoritative.HealthCurrent);
                    if (Math.Abs(current - target) > 0.001f)
                        GameApi.InvokeWithArgument(health, "ChangeHealth", target);
                }
            }
            for (int i = 0; i < snapshot.Enemies.Length; i++)
                ApplyEnemy(resolvedEnemies[i], snapshot.Enemies[i]);
            return true;
        }

        private static List<GameObject> GetMonsterObjects(object[] players)
        {
            var playerObjects = new HashSet<GameObject>();
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] == null) continue;
                GameObject playerObject = GameApi.Property<GameObject>(players[i], "GameObject");
                if (playerObject != null) playerObjects.Add(playerObject);
            }

            var enemies = new List<GameObject>();
            IEnumerable spawned;
            try { spawned = GameApi.InvokeStatic("SystemLevel", "GetMonstersSpawned") as IEnumerable; }
            catch { return enemies; }
            if (spawned == null) return enemies;
            foreach (object item in spawned)
            {
                GameObject enemy = item as GameObject;
                if (enemy != null && !playerObjects.Contains(enemy)) enemies.Add(enemy);
            }
            return enemies;
        }

        private static EnemySnapshot CaptureEnemy(GameObject gameObject, uint id)
        {
            var snapshot = new EnemySnapshot
            {
                Id = id,
                ArchetypeHash = ArchetypeHash(gameObject),
                PositionX = Quantize(gameObject.transform.position.x),
                PositionY = Quantize(gameObject.transform.position.y)
            };
            if (gameObject.activeInHierarchy) snapshot.Flags |= EnemySnapshotFlags.Active;
            Component player = gameObject.GetComponent(GameApi.Type("Player"));
            if (player != null)
            {
                snapshot.State = Convert.ToInt16(GameApi.Invoke(player, "GetState"));
                if (GameApi.Invoke<bool>(player, "GetIsAI")) snapshot.Flags |= EnemySnapshotFlags.AiControlled;
            }
            Rigidbody body = gameObject.GetComponent<Rigidbody>();
            if (body != null)
            {
                snapshot.VelocityX = Quantize(body.velocity.x);
                snapshot.VelocityY = Quantize(body.velocity.y);
            }
            Component health = gameObject.GetComponent(GameApi.Type("Health"));
            if (health != null)
            {
                float current = GameApi.Invoke<float>(health, "GetHealthCurrent");
                snapshot.HealthCurrent = Quantize(current);
                snapshot.HealthMaximum = Quantize(GameApi.Invoke<float>(health, "GetHealthMax"));
                if (current > 0f) snapshot.Flags |= EnemySnapshotFlags.Alive;
            }
            return snapshot;
        }

        private static GameObject MatchEnemy(EnemySnapshot authoritative, List<GameObject> candidates,
            HashSet<GameObject> claimed)
        {
            GameObject best = null;
            double bestDistance = double.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                GameObject candidate = candidates[i];
                if (claimed.Contains(candidate) || ArchetypeHash(candidate) != authoritative.ArchetypeHash) continue;
                long dx = (long)Quantize(candidate.transform.position.x) - authoritative.PositionX;
                long dy = (long)Quantize(candidate.transform.position.y) - authoritative.PositionY;
                double distance = (double)dx * dx + (double)dy * dy;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }
            return best;
        }

        private static void ApplyEnemy(GameObject gameObject, EnemySnapshot authoritative)
        {
            Vector3 position = gameObject.transform.position;
            position.x = Expand(authoritative.PositionX);
            position.y = Expand(authoritative.PositionY);
            gameObject.transform.position = position;
            Rigidbody body = gameObject.GetComponent<Rigidbody>();
            if (body != null)
                body.velocity = new Vector3(Expand(authoritative.VelocityX), Expand(authoritative.VelocityY), body.velocity.z);
            Component health = gameObject.GetComponent(GameApi.Type("Health"));
            if (health != null && authoritative.HealthCurrent > 0)
            {
                float current = GameApi.Invoke<float>(health, "GetHealthCurrent");
                float target = Expand(authoritative.HealthCurrent);
                if (Math.Abs(current - target) > 0.001f)
                    GameApi.InvokeWithArgument(health, "ChangeHealth", target);
            }
        }

        private static ulong ArchetypeHash(GameObject gameObject)
        {
            string name = gameObject.name ?? string.Empty;
            const string CloneSuffix = "(Clone)";
            if (name.EndsWith(CloneSuffix, StringComparison.Ordinal))
                name = name.Substring(0, name.Length - CloneSuffix.Length).TrimEnd();
            var hash = new StableHash64();
            hash.AddString(name);
            ulong value = hash.Value;
            return value == 0 ? 1UL : value;
        }

        private bool RejectMismatch(string mismatch)
        {
            if (lastMismatchKey != mismatch)
                log.LogWarning("Authoritative transition requires explicit safe handler: " + mismatch);
            lastMismatchKey = mismatch;
            return false;
        }

        private void ReportRuntimeError(string operation, Exception exception)
        {
            string key = operation + ":" + exception.GetType().FullName + ":" + exception.Message;
            if (lastRuntimeError != key)
                log.LogError("Authoritative " + operation + " failed closed: " + exception);
            lastRuntimeError = key;
        }

        private static object FindPlayerBySlot(object[] players, byte slot)
        {
            for (int i = 0; i < players.Length; i++)
            {
                object player = players[i];
                if (player != null && GameApi.Property<int>(player, "Id") == slot) return player;
            }
            return null;
        }

        private static byte Buttons(object player, string action, string back, string start)
        {
            return (byte)((GameApi.Invoke<bool>(player, action) ? 1 : 0) |
                          (GameApi.Invoke<bool>(player, back) ? 2 : 0) |
                          (GameApi.Invoke<bool>(player, start) ? 4 : 0));
        }

        private static short QuantizeAxis(float value)
        {
            return (short)Mathf.Clamp(Mathf.RoundToInt(value * short.MaxValue), short.MinValue, short.MaxValue);
        }

        private static int Quantize(float value)
        {
            double scaled = Math.Round(value * UnitsPerStep, MidpointRounding.AwayFromZero);
            if (scaled < int.MinValue) return int.MinValue;
            if (scaled > int.MaxValue) return int.MaxValue;
            return (int)scaled;
        }

        private static float Expand(int value)
        {
            return value / UnitsPerStep;
        }
    }
}
