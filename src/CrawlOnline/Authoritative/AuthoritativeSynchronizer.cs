using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using CrawlOnline.Determinism;
using CrawlOnline.Protocol;
using CrawlOnline.Online;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CrawlOnline.Authoritative
{
    internal sealed class AuthoritativeSynchronizer : IDisposable
    {
        private const int SnapshotIntervalFrames = 6;
        private const float UnitsPerStep = 10000f;
        private const string LifecycleHarmonyId = "dev.teagar.crawl-online.authoritative-lifecycle";
        private static AuthoritativeSynchronizer activeLifecycleSynchronizer;
        private readonly IOnlineSession session;
        private readonly ManualLogSource log;
        private readonly AuthoritativeInputBridge inputBridge = new AuthoritativeInputBridge();
        private readonly Harmony lifecycleHarmony = new Harmony(LifecycleHarmonyId);
        private readonly Dictionary<GameObject, uint> hostEnemyIds = new Dictionary<GameObject, uint>();
        private readonly Dictionary<uint, GameObject> clientEnemies = new Dictionary<uint, GameObject>();
        private readonly bool[] hasPlayerLifeRequest = new bool[4];
        private readonly bool[] playerLifeRequest = new bool[4];
        private readonly int[] playerMaxHealthRequests = new int[4];
        private readonly Dictionary<uint, bool> enemyLifeRequests = new Dictionary<uint, bool>();
        private readonly Dictionary<uint, int> enemyMaxHealthRequests = new Dictionary<uint, int>();
        private readonly HashSet<uint> enemyDespawnRequests = new HashSet<uint>();
        private readonly HashSet<uint> enemySpawnRequests = new HashSet<uint>();
        private readonly HashSet<byte> managedBotSlots = new HashSet<byte>();
        private byte configuredLocalSlot = byte.MaxValue;
        private WorldSnapshot pendingSnapshot;
        private WorldSnapshot latestSnapshot;
        private uint mapPlanReadySequence;
        private bool mapLayoutTransitionApplied;
        private uint tick;
        private readonly uint[] lastInputSequences = new uint[4];
        private uint transitionGeneration;
        private string lastWorldKey;
        private string lastMismatchKey;
        private string lastRuntimeError;
        private string lastSnapshotFlowKey;
        private string lastRoomDiagnosticKey;
        private bool remoteInputObserved;
        private bool roomContentContractLogged;
        private int lastHostEnemyCount = -1;
        private int lastClientEnemyCount = -1;
        private uint nextEnemyId = 1;
        private uint requestedTransitionGeneration;
        private bool hasRequestedTransition;
        private uint requestedLevelCompleteGeneration;
        private bool hasRequestedLevelComplete;
        private byte requestedHeroSlot = byte.MaxValue;
        private bool hasRequestedGameLifecycle;
        private bool requestedGameInProgress;
        private int requestedLevel = -1;
        private uint gameStartGeneration;
        private uint[] gameStartRandomStateWords = new uint[4];
        private uint[] mapRandomStateWords = new uint[4];
        private readonly List<ulong> hostRoomArchetypeHashes = new List<ulong>();
        private readonly List<ulong> hostSpecialRoomArchetypeHashes = new List<ulong>();
        private readonly List<RoomCollisionDecision> hostRoomCollisionDecisions =
            new List<RoomCollisionDecision>();
        private readonly List<RoomSelectionDecision> hostRoomSelectionDecisions =
            new List<RoomSelectionDecision>();
        private ulong[] clientRoomArchetypeHashes = new ulong[0];
        private ulong[] clientSpecialRoomArchetypeHashes = new ulong[0];
        private readonly Dictionary<ulong, GameObject> clientRoomPrefabs = new Dictionary<ulong, GameObject>();
        private readonly Dictionary<object, uint[]> mapIteratorRandomStates = new Dictionary<object, uint[]>();
        private readonly Dictionary<ulong, Queue<RoomCollisionDecision>> clientRoomCollisionDecisions =
            new Dictionary<ulong, Queue<RoomCollisionDecision>>();
        private readonly Dictionary<ulong, Queue<ulong>> clientRoomSelectionDecisions =
            new Dictionary<ulong, Queue<ulong>>();
        private readonly Queue<RoomSelectionDecision> clientRoomDoorDecisions =
            new Queue<RoomSelectionDecision>();
        private readonly List<object> clientAuthoritativeRooms = new List<object>();
        private object activeMapIterator;
        private int clientKeyedRoomSelectionCount;
        private int clientRoomArchetypeIndex;
        private int clientSpecialRoomArchetypeIndex;
        private bool mapRandomStateReady;
        private int mapRandomScopeDepth;
        private int mapIteratorRandomScopeDepth;
        private int mapRandomScopeEntries;
        private bool roomSetLogged;
        private uint roomContentGeneration;
        private uint roomContentAppliedGeneration;
        private int pendingRoomContentRoomIndex = -1;
        private uint[] roomContentRandomStateWords = new uint[4];
        private object hostRoomContentRoom;
        private bool hostBootstrapPaused;
        private float hostPreviousTimeScale = 1f;

        public AuthoritativeSynchronizer(IOnlineSession lobbySession, ManualLogSource logSource)
        {
            session = lobbySession;
            log = logSource;
            session.InputReceived += OnInputReceived;
            session.InputEventReceived += OnInputEventReceived;
            session.SnapshotReceived += OnSnapshotReceived;
            activeLifecycleSynchronizer = this;
            lifecycleHarmony.Patch(AccessTools.Method(GameApi.Type("SystemMain"), "StartNewGame"),
                new HarmonyMethod(typeof(AuthoritativeSynchronizer), "StartNewGamePrefix"), null);
            Type mapGeneratorType = GameApi.Type("MapGenerator");
            Type systemLevelType = GameApi.Type("SystemLevel");
            lifecycleHarmony.Patch(AccessTools.Method(mapGeneratorType, "CreateRoomList"),
                new HarmonyMethod(typeof(AuthoritativeSynchronizer), "MapRandomScopePrefix"),
                new HarmonyMethod(typeof(AuthoritativeSynchronizer), "MapRandomScopePostfix"));
            lifecycleHarmony.Patch(AccessTools.Method(mapGeneratorType, "CoroutineSpawnRoomRandomlyOnMap"),
                null, new HarmonyMethod(typeof(AuthoritativeSynchronizer), "SpawnCoroutineCreatedPostfix"));
            Type[] nestedTypes = mapGeneratorType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < nestedTypes.Length; i++)
            {
                if (!typeof(IEnumerator).IsAssignableFrom(nestedTypes[i])) continue;
                MethodInfo moveNext = AccessTools.Method(nestedTypes[i], "MoveNext");
                if (moveNext == null) continue;
                if (nestedTypes[i].Name.IndexOf("BuildRoom", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    nestedTypes[i].Name.IndexOf("SpawnRoomRandomly", StringComparison.OrdinalIgnoreCase) >= 0)
                    lifecycleHarmony.Patch(moveNext,
                        new HarmonyMethod(typeof(AuthoritativeSynchronizer), "MapIteratorRandomScopePrefix"),
                        new HarmonyMethod(typeof(AuthoritativeSynchronizer), "MapIteratorRandomScopePostfix"));
                if (nestedTypes[i].Name.IndexOf("RoomBodyCollided", StringComparison.OrdinalIgnoreCase) >= 0)
                    lifecycleHarmony.Patch(moveNext, null,
                        new HarmonyMethod(typeof(AuthoritativeSynchronizer), "RoomCollisionDecisionPostfix"));
            }
            lifecycleHarmony.Patch(AccessTools.Method(mapGeneratorType, "CanSpawnFromDoor"),
                new HarmonyMethod(typeof(AuthoritativeSynchronizer), "RoomDoorSelectionPrefix"),
                new HarmonyMethod(typeof(AuthoritativeSynchronizer), "RoomDoorSelectionPostfix"));
            lifecycleHarmony.Patch(AccessTools.Method(GameApi.Type("SpawnGroupList"), "GetObject"),
                null,
                new HarmonyMethod(typeof(AuthoritativeSynchronizer), "RoomSelectionPostfix"));
            lifecycleHarmony.Patch(AccessTools.Method(systemLevelType, "OnEnteredRoomFirstTime"),
                new HarmonyMethod(typeof(AuthoritativeSynchronizer), "SpawnRoomContentPrefix"));
        }

        public void LateTick()
        {
            tick++;
            if (!session.InLobby)
            {
                RestoreHostTimeScale();
                try { ReleaseNetworkAssignments(); }
                catch (Exception exception) { ReportRuntimeError("assignment cleanup", exception); }
                inputBridge.Clear();
                hostEnemyIds.Clear();
                clientEnemies.Clear();
                Array.Clear(hasPlayerLifeRequest, 0, hasPlayerLifeRequest.Length);
                enemyLifeRequests.Clear();
                enemyDespawnRequests.Clear();
                enemySpawnRequests.Clear();
                managedBotSlots.Clear();
                configuredLocalSlot = byte.MaxValue;
                nextEnemyId = 1;
                hasRequestedTransition = false;
                hasRequestedLevelComplete = false;
                requestedHeroSlot = byte.MaxValue;
                hasRequestedGameLifecycle = false;
                requestedLevel = -1;
                gameStartGeneration = 0;
                gameStartRandomStateWords = new uint[4];
                mapRandomStateWords = new uint[4];
                hostRoomArchetypeHashes.Clear();
                hostSpecialRoomArchetypeHashes.Clear();
                hostRoomCollisionDecisions.Clear();
                hostRoomSelectionDecisions.Clear();
                clientRoomArchetypeHashes = new ulong[0];
                clientSpecialRoomArchetypeHashes = new ulong[0];
                clientRoomPrefabs.Clear();
                mapIteratorRandomStates.Clear();
                clientRoomCollisionDecisions.Clear();
                clientRoomSelectionDecisions.Clear();
                clientRoomDoorDecisions.Clear();
                clientAuthoritativeRooms.Clear();
                clientRoomArchetypeIndex = 0;
                clientSpecialRoomArchetypeIndex = 0;
                clientKeyedRoomSelectionCount = 0;
                activeMapIterator = null;
                mapRandomStateReady = false;
                mapRandomScopeDepth = 0;
                mapIteratorRandomScopeDepth = 0;
                mapRandomScopeEntries = 0;
                roomSetLogged = false;
                roomContentGeneration = 0;
                roomContentAppliedGeneration = 0;
                pendingRoomContentRoomIndex = -1;
                roomContentRandomStateWords = new uint[4];
                hostRoomContentRoom = null;
                pendingSnapshot = null;
                latestSnapshot = null;
                mapPlanReadySequence = 0;
                mapLayoutTransitionApplied = false;
                lastSnapshotFlowKey = null;
                lastRoomDiagnosticKey = null;
                remoteInputObserved = false;
                roomContentContractLogged = false;
                lastHostEnemyCount = -1;
                lastClientEnemyCount = -1;
                Array.Clear(playerMaxHealthRequests, 0, playerMaxHealthRequests.Length);
                enemyMaxHealthRequests.Clear();
                return;
            }

            if (session.IsAuthoritativeHost)
            {
                try { EnsureNetworkAssignments(null); }
                catch (Exception exception)
                {
                    ReportRuntimeError("host assignment", exception);
                    return;
                }
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
                UpdateHostBootstrapPause();
            }
            else if (session.LocalSlot != byte.MaxValue)
            {
                try { EnsureNetworkAssignments(latestSnapshot); }
                catch (Exception exception)
                {
                    ReportRuntimeError("client assignment", exception);
                    return;
                }
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
                        bool applied = ApplySnapshot(pendingSnapshot);
                        if (applied)
                        {
                            session.AcknowledgeSnapshot(pendingSnapshot.Sequence);
                            pendingSnapshot = null;
                            mapPlanReadySequence = 0;
                        }
                    }
                    catch (Exception exception) { ReportRuntimeError("correction", exception); }
                }
            }
        }

        private void UpdateHostBootstrapPause()
        {
            bool shouldPause = gameStartGeneration != 0 &&
                session.HasPendingMapPlanAcknowledgements;
            if (shouldPause == hostBootstrapPaused) return;
            if (shouldPause)
            {
                hostPreviousTimeScale = Time.timeScale;
                Time.timeScale = 0f;
                hostBootstrapPaused = true;
                log.LogInfo("MAP_BOOTSTRAP_PAUSE waiting-for-ack");
            }
            else RestoreHostTimeScale();
        }

        private void RestoreHostTimeScale()
        {
            if (!hostBootstrapPaused) return;
            Time.timeScale = hostPreviousTimeScale;
            hostBootstrapPaused = false;
            log.LogInfo("MAP_BOOTSTRAP_PAUSE resumed");
        }

        public void Dispose()
        {
            session.InputReceived -= OnInputReceived;
            session.InputEventReceived -= OnInputEventReceived;
            session.SnapshotReceived -= OnSnapshotReceived;
            if (ReferenceEquals(activeLifecycleSynchronizer, this)) activeLifecycleSynchronizer = null;
            lifecycleHarmony.UnpatchAll(LifecycleHarmonyId);
            inputBridge.Dispose();
        }

        private static void StartNewGamePrefix()
        {
            AuthoritativeSynchronizer synchronizer = activeLifecycleSynchronizer;
            if (synchronizer == null || !synchronizer.session.InLobby) return;
            synchronizer.configuredLocalSlot = byte.MaxValue;
            synchronizer.managedBotSlots.Clear();
            synchronizer.hasRequestedLevelComplete = false;
            if (!synchronizer.session.IsAuthoritativeHost)
                return;
            synchronizer.gameStartRandomStateWords = StateHasher.CaptureRandomStateWords();
            synchronizer.hostRoomArchetypeHashes.Clear();
            synchronizer.hostSpecialRoomArchetypeHashes.Clear();
            synchronizer.hostRoomCollisionDecisions.Clear();
            synchronizer.hostRoomSelectionDecisions.Clear();
            synchronizer.hostRoomContentRoom = null;
            synchronizer.roomContentRandomStateWords = new uint[4];
            synchronizer.mapIteratorRandomStates.Clear();
            synchronizer.mapRandomStateWords = (uint[])synchronizer.gameStartRandomStateWords.Clone();
            synchronizer.mapRandomStateReady = true;
            synchronizer.mapRandomScopeDepth = 0;
            synchronizer.mapIteratorRandomScopeDepth = 0;
            synchronizer.mapRandomScopeEntries = 0;
            synchronizer.gameStartGeneration++;
            if (synchronizer.gameStartGeneration == 0) synchronizer.gameStartGeneration = 1;
        }

        private static void MapRandomScopePrefix(ref uint[] __state)
        {
            AuthoritativeSynchronizer synchronizer = activeLifecycleSynchronizer;
            if (synchronizer == null || !synchronizer.session.InLobby ||
                !synchronizer.mapRandomStateReady) return;
            synchronizer.mapRandomScopeDepth++;
            if (synchronizer.mapRandomScopeDepth != 1) return;
            synchronizer.mapRandomScopeEntries++;
            __state = StateHasher.CaptureRandomStateWords();
            StateHasher.ApplyRandomStateWords(synchronizer.mapRandomStateWords);
        }

        private static void SpawnRoomContentPrefix(object __0)
        {
            AuthoritativeSynchronizer synchronizer = activeLifecycleSynchronizer;
            if (synchronizer == null || !synchronizer.session.InLobby || __0 == null) return;
            if (synchronizer.session.IsAuthoritativeHost)
            {
                synchronizer.roomContentRandomStateWords = StateHasher.CaptureRandomStateWords();
                synchronizer.hostRoomContentRoom = __0;
                synchronizer.roomContentGeneration++;
                if (synchronizer.roomContentGeneration == 0) synchronizer.roomContentGeneration = 1;
                synchronizer.log.LogInfo("ROOM_CONTENT_RNG role=host generation=" +
                    synchronizer.roomContentGeneration);
                return;
            }
            if (synchronizer.roomContentGeneration == 0 ||
                synchronizer.roomContentAppliedGeneration == synchronizer.roomContentGeneration) return;
            object generator = null;
            try { generator = GameApi.StaticProperty("SystemLevel", "MapGenerator"); }
            catch { }
            int localIndex = synchronizer.FindAuthoritativeRoomIndex(generator, __0);
            if (localIndex != synchronizer.pendingRoomContentRoomIndex) return;
            StateHasher.ApplyRandomStateWords(synchronizer.roomContentRandomStateWords);
            synchronizer.roomContentAppliedGeneration = synchronizer.roomContentGeneration;
            synchronizer.log.LogInfo("ROOM_CONTENT_RNG generation=" + synchronizer.roomContentGeneration +
                " room=" + localIndex);
        }

        private static void MapRandomScopePostfix(uint[] __state)
        {
            AuthoritativeSynchronizer synchronizer = activeLifecycleSynchronizer;
            if (synchronizer == null || synchronizer.mapRandomScopeDepth == 0) return;
            if (synchronizer.mapRandomScopeDepth == 1)
            {
                synchronizer.mapRandomStateWords = StateHasher.CaptureRandomStateWords();
                if (__state != null) StateHasher.ApplyRandomStateWords(__state);
            }
            synchronizer.mapRandomScopeDepth--;
        }

        private sealed class IteratorRandomScope
        {
            public object Iterator;
            public object PreviousIterator;
            public uint[] Previous;
        }

        private static void MapIteratorRandomScopePrefix(object __instance, ref IteratorRandomScope __state)
        {
            AuthoritativeSynchronizer synchronizer = activeLifecycleSynchronizer;
            if (synchronizer == null || !synchronizer.session.InLobby ||
                !synchronizer.mapRandomStateReady || __instance == null) return;
            uint[] stream;
            if (!synchronizer.mapIteratorRandomStates.TryGetValue(__instance, out stream))
            {
                stream = DeriveIteratorRandomState(synchronizer.gameStartRandomStateWords, __instance);
                synchronizer.mapIteratorRandomStates.Add(__instance, stream);
            }
            __state = new IteratorRandomScope
            {
                Iterator = __instance,
                PreviousIterator = synchronizer.activeMapIterator,
                Previous = StateHasher.CaptureRandomStateWords()
            };
            synchronizer.activeMapIterator = __instance;
            synchronizer.mapIteratorRandomScopeDepth++;
            synchronizer.mapRandomScopeEntries++;
            StateHasher.ApplyRandomStateWords(stream);
        }

        private static void MapIteratorRandomScopePostfix(IteratorRandomScope __state)
        {
            AuthoritativeSynchronizer synchronizer = activeLifecycleSynchronizer;
            if (synchronizer == null || __state == null) return;
            synchronizer.mapIteratorRandomStates[__state.Iterator] = StateHasher.CaptureRandomStateWords();
            StateHasher.ApplyRandomStateWords(__state.Previous);
            synchronizer.activeMapIterator = __state.PreviousIterator;
            if (synchronizer.mapIteratorRandomScopeDepth > 0) synchronizer.mapIteratorRandomScopeDepth--;
        }

        private static uint[] DeriveIteratorRandomState(uint[] basis, object iterator)
        {
            var words = new uint[4];
            Type iteratorType = iterator.GetType();
            string typeName = iteratorType.FullName ?? iteratorType.Name;
            FieldInfo prefabField = iteratorType.GetField("prefab", BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
            Component prefab = prefabField == null ? null : prefabField.GetValue(iterator) as Component;
            FieldInfo depthField = iteratorType.GetField("minDepth", BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
            FieldInfo distanceField = iteratorType.GetField("minFromStart", BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < words.Length; i++)
            {
                var hash = new StableHash64();
                hash.AddString(typeName);
                hash.AddInt32(i);
                for (int j = 0; j < basis.Length; j++) hash.AddUInt32(basis[j]);
                if (prefab != null) hash.AddString(prefab.gameObject.name);
                hash.AddInt32(depthField == null ? 0 : (int)depthField.GetValue(iterator));
                hash.AddInt32(distanceField == null ? 0 : (int)distanceField.GetValue(iterator));
                words[i] = (uint)(hash.Value ^ (hash.Value >> 32));
            }
            if ((words[0] | words[1] | words[2] | words[3]) == 0) words[0] = 1;
            return words;
        }

        private static void RoomSelectionPostfix(ref GameObject __result)
        {
            AuthoritativeSynchronizer synchronizer = activeLifecycleSynchronizer;
            if (synchronizer == null || !synchronizer.session.InLobby ||
                (synchronizer.mapRandomScopeDepth == 0 && synchronizer.mapIteratorRandomScopeDepth == 0)) return;
            ulong selectionKey;
            bool hasSelectionKey = TryGetRoomSelectionKey(synchronizer.activeMapIterator, out selectionKey);
            if (synchronizer.session.IsAuthoritativeHost)
            {
                if (__result == null) return;
                ulong archetypeHash = ArchetypeHash(__result);
                if (hasSelectionKey)
                {
                    if (synchronizer.hostRoomSelectionDecisions.Count < 512)
                        synchronizer.hostRoomSelectionDecisions.Add(new RoomSelectionDecision
                        {
                            Key = selectionKey,
                            ArchetypeHash = archetypeHash
                    });
                    else synchronizer.RejectMismatch("keyed-room-selection-limit");
                }
                else if (synchronizer.hostRoomArchetypeHashes.Count < 512)
                    synchronizer.hostRoomArchetypeHashes.Add(archetypeHash);
                else synchronizer.RejectMismatch("room-selection-tape-limit");
                return;
            }
            ulong hash;
            if (hasSelectionKey)
            {
                Queue<ulong> decisions;
                if (!synchronizer.clientRoomSelectionDecisions.TryGetValue(selectionKey, out decisions) ||
                    decisions.Count == 0)
                {
                    synchronizer.RejectMismatch("keyed-room-selection-missing:" +
                        selectionKey.ToString("x16"));
                    return;
                }
                hash = decisions.Dequeue();
                synchronizer.clientKeyedRoomSelectionCount++;
            }
            else
            {
                if (synchronizer.clientRoomArchetypeIndex >= synchronizer.clientRoomArchetypeHashes.Length)
                {
                    synchronizer.RejectMismatch("room-selection-tape-exhausted");
                    return;
                }
                hash = synchronizer.clientRoomArchetypeHashes[synchronizer.clientRoomArchetypeIndex++];
            }
            GameObject candidate;
            synchronizer.clientRoomPrefabs.TryGetValue(hash, out candidate);
            if (candidate == null)
            {
                synchronizer.RejectMismatch("room-plan-archetype-missing:" + hash.ToString("x16"));
                return;
            }
            __result = candidate;
        }

        private sealed class RoomDoorSelectionState
        {
            public ulong Key;
            public int DoorIndex;
        }

        private static bool RoomDoorSelectionPrefix(object __0, object __1, object __2,
            ref bool __result, ref RoomDoorSelectionState __state)
        {
            AuthoritativeSynchronizer synchronizer = activeLifecycleSynchronizer;
            if (synchronizer == null || !synchronizer.session.InLobby || __2 == null) return true;
            object room = RoomContainingDoor(__0, __2) ? __0 :
                (RoomContainingDoor(__1, __2) ? __1 : null);
            Component roomComponent = room as Component;
            if (roomComponent == null) return true;
            Component candidate = ReferenceEquals(room, __0) ? __1 as Component : __0 as Component;
            if (candidate == null) return true;
            int doorIndex = FindDoorIndex(room, __2);
            if (doorIndex < 0) return true;
            ulong key = RoomDoorCandidateKey(roomComponent, candidate);
            if (synchronizer.session.IsAuthoritativeHost)
            {
                __state = new RoomDoorSelectionState { Key = key, DoorIndex = doorIndex };
                return true;
            }
            if (synchronizer.clientRoomDoorDecisions.Count == 0)
            {
                __result = false;
                synchronizer.RejectMismatch("room-door-plan-missing:" + key.ToString("x16"));
                return false;
            }
            RoomSelectionDecision expected = synchronizer.clientRoomDoorDecisions.Peek();
            ulong encoded = expected.ArchetypeHash;
            int expectedIndex = checked((int)(encoded >> 1) - 1);
            if (doorIndex != expectedIndex)
            {
                __result = false;
                return false;
            }
            synchronizer.clientRoomDoorDecisions.Dequeue();
            synchronizer.clientKeyedRoomSelectionCount++;
            __result = (encoded & 1UL) != 0;
            return false;
        }

        private static void RoomDoorSelectionPostfix(RoomDoorSelectionState __state, bool __result)
        {
            AuthoritativeSynchronizer synchronizer = activeLifecycleSynchronizer;
            if (synchronizer == null || __state == null || !synchronizer.session.InLobby ||
                !synchronizer.session.IsAuthoritativeHost) return;
            if (synchronizer.hostRoomSelectionDecisions.Count >= 512)
            {
                synchronizer.RejectMismatch("room-door-plan-limit");
                return;
            }
            synchronizer.hostRoomSelectionDecisions.Add(new RoomSelectionDecision
            {
                Key = __state.Key,
                ArchetypeHash = ((ulong)(__state.DoorIndex + 1) << 1) | (__result ? 1UL : 0UL)
            });
        }

        private static bool RoomContainingDoor(object room, object targetDoor)
        {
            return room != null && FindDoorIndex(room, targetDoor) >= 0;
        }

        private static int FindDoorIndex(object room, object targetDoor)
        {
            IEnumerable doors;
            try { doors = GameApi.Field<IEnumerable>(room, "m_doors"); }
            catch { return -1; }
            if (doors == null) return -1;
            int index = 0;
            foreach (object door in doors)
            {
                if (door != null && (ReferenceEquals(door, targetDoor) || door.Equals(targetDoor)))
                    return index;
                index++;
            }
            return -1;
        }

        private static ulong RoomDoorCandidateKey(Component component, Component candidate)
        {
            var hash = new StableHash64();
            ulong source = ArchetypeHash(component.gameObject);
            hash.AddUInt32((uint)source);
            hash.AddUInt32((uint)(source >> 32));
            ulong archetype = ArchetypeHash(candidate.gameObject);
            hash.AddUInt32((uint)archetype);
            hash.AddUInt32((uint)(archetype >> 32));
            ulong result = hash.Value;
            return result == 0 ? 1UL : result;
        }

        private static bool TryGetRoomSelectionKey(object iterator, out ulong key)
        {
            key = 0;
            if (iterator == null) return false;
            Type type = iterator.GetType();
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
            object room = null;
            object door = null;
            for (int pass = 0; pass < 2 && (room == null || door == null); pass++)
            {
                for (int i = 0; i < fields.Length; i++)
                {
                    string name = fields[i].Name;
                    bool preferred = name.IndexOf("from", StringComparison.OrdinalIgnoreCase) >= 0;
                    if ((pass == 0) != preferred) continue;
                    object value;
                    try { value = fields[i].GetValue(iterator); }
                    catch { continue; }
                    if (value == null) continue;
                    Component component = value as Component;
                    if (room == null && component != null &&
                        component.GetType() == GameApi.Type("Room"))
                        room = value;
                    Type valueType = value.GetType();
                    if (door == null && AccessTools.Field(valueType, "m_position") != null &&
                        AccessTools.Field(valueType, "m_direction") != null)
                        door = value;
                }
            }
            object generator = GameApi.Field<object>(iterator, "<>f__this");
            if (door == null)
            {
                for (int i = 0; i < fields.Length && door == null; i++)
                {
                    object enumerator;
                    try { enumerator = fields[i].GetValue(iterator); }
                    catch { continue; }
                    if (enumerator == null) continue;
                    IEnumerator nonGeneric = enumerator as IEnumerator;
                    PropertyInfo currentProperty = null;
                    if (nonGeneric == null)
                    {
                        try
                        {
                            currentProperty = enumerator.GetType().GetProperty("Current",
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        }
                        catch { }
                        if (currentProperty == null) continue;
                    }
                    object current;
                    try
                    {
                        current = nonGeneric == null
                            ? currentProperty.GetValue(enumerator, null)
                            : nonGeneric.Current;
                    }
                    catch
                    {
                        try { current = currentProperty.GetValue(enumerator, null); }
                        catch { continue; }
                    }
                    if (current == null) continue;
                    Type currentType = current.GetType();
                    if (AccessTools.Field(currentType, "m_position") != null &&
                        AccessTools.Field(currentType, "m_direction") != null)
                        door = current;
                }
            }
            if (room == null && door != null && generator != null)
                room = FindRoomOwningDoor(generator, door);
            Component roomComponent = room as Component;
            if (roomComponent == null || door == null) return false;
            key = RoomCollisionKey(room, roomComponent, door);
            return true;
        }

        private static object FindRoomOwningDoor(object generator, object targetDoor)
        {
            List<object> rooms = EnumerateRooms(generator);
            UnityEngine.Object[] allRooms = UnityEngine.Object.FindObjectsOfType(GameApi.Type("Room"));
            for (int i = 0; i < allRooms.Length; i++)
                if (!rooms.Contains(allRooms[i])) rooms.Add(allRooms[i]);
            for (int i = 0; i < rooms.Count; i++)
            {
                IEnumerable doors;
                try { doors = GameApi.Field<IEnumerable>(rooms[i], "m_doors"); }
                catch { continue; }
                if (doors == null) continue;
                foreach (object candidate in doors)
                {
                    if (candidate == null) continue;
                    if (ReferenceEquals(candidate, targetDoor) || candidate.Equals(targetDoor))
                        return rooms[i];
                }
            }
            return null;
        }

        private static void SpawnCoroutineCreatedPostfix(IEnumerator __result)
        {
            AuthoritativeSynchronizer synchronizer = activeLifecycleSynchronizer;
            if (synchronizer == null || !synchronizer.session.InLobby || __result == null) return;
            Component prefab = GameApi.Field<object>(__result, "prefab") as Component;
            if (prefab == null) return;
            if (synchronizer.session.IsAuthoritativeHost)
            {
                if (synchronizer.hostSpecialRoomArchetypeHashes.Count >= 8)
                {
                    synchronizer.RejectMismatch("special-room-plan-limit");
                    return;
                }
                synchronizer.hostSpecialRoomArchetypeHashes.Add(ArchetypeHash(prefab.gameObject));
            }
            else
            {
                if (synchronizer.clientSpecialRoomArchetypeIndex >=
                    synchronizer.clientSpecialRoomArchetypeHashes.Length)
                {
                    synchronizer.RejectMismatch("special-room-plan-exhausted");
                    return;
                }
                ulong hash = synchronizer.clientSpecialRoomArchetypeHashes[
                    synchronizer.clientSpecialRoomArchetypeIndex++];
                GameObject candidate;
                synchronizer.clientRoomPrefabs.TryGetValue(hash, out candidate);
                Component room = candidate == null ? null : candidate.GetComponent(GameApi.Type("Room"));
                if (room == null)
                {
                    synchronizer.RejectMismatch("special-room-archetype-missing:" + hash.ToString("x16"));
                    return;
                }
                FieldInfo prefabField = AccessTools.Field(__result.GetType(), "prefab");
                FieldInfo capturedPrefabField = AccessTools.Field(__result.GetType(), "<$>prefab");
                if (prefabField == null || capturedPrefabField == null)
                {
                    synchronizer.RejectMismatch("special-room-iterator-contract");
                    return;
                }
                prefabField.SetValue(__result, room);
                capturedPrefabField.SetValue(__result, room);
                prefab = room;
            }
        }

        private static void RoomCollisionDecisionPostfix(object __instance, bool __result)
        {
            if (__result) return;
            AuthoritativeSynchronizer synchronizer = activeLifecycleSynchronizer;
            if (synchronizer == null || !synchronizer.session.InLobby) return;
            object generator = GameApi.Field<object>(__instance, "<>f__this");
            Component prefab = GameApi.Field<object>(__instance, "prefab") as Component;
            object fromRoom = GameApi.Field<object>(__instance, "fromRoom");
            object fromDoor = GameApi.Field<object>(__instance, "fromDoor");
            Component fromComponent = fromRoom as Component;
            if (generator == null || prefab == null || fromComponent == null || fromDoor == null) return;
            ulong key = RoomCollisionKey(fromRoom, fromComponent, fromDoor);
            if (synchronizer.session.IsAuthoritativeHost)
            {
                if (synchronizer.hostRoomCollisionDecisions.Count >= 512)
                {
                    synchronizer.RejectMismatch("room-collision-plan-limit");
                    return;
                }
                synchronizer.hostRoomCollisionDecisions.Add(new RoomCollisionDecision
                {
                    Key = key,
                    ArchetypeHash = ArchetypeHash(prefab.gameObject),
                    Collided = GameApi.Field<bool>(generator, "m_bodyCollided")
                });
                return;
            }
            Queue<RoomCollisionDecision> decisions;
            if (!synchronizer.clientRoomCollisionDecisions.TryGetValue(key, out decisions) ||
                decisions.Count == 0)
            {
                GameApi.SetField(generator, "m_bodyCollided", true);
                synchronizer.RejectMismatch("room-collision-plan-missing:" + key.ToString("x16"));
                return;
            }
            RoomCollisionDecision expected = decisions.Peek();
            if (ArchetypeHash(prefab.gameObject) != expected.ArchetypeHash)
            {
                GameApi.SetField(generator, "m_bodyCollided", true);
                return;
            }
            GameApi.SetField(generator, "m_bodyCollided", expected.Collided);
            decisions.Dequeue();
        }

        private static ulong RoomCollisionKey(object fromRoom, Component fromComponent, object fromDoor)
        {
            Vector2 doorPosition = GameApi.Field<Vector2>(fromDoor, "m_position");
            var hash = new StableHash64();
            ulong sourceHash = ArchetypeHash(fromComponent.gameObject);
            hash.AddUInt32((uint)sourceHash);
            hash.AddUInt32((uint)(sourceHash >> 32));
            hash.AddInt32(GameApi.Property<int>(fromRoom, "Depth"));
            hash.AddQuantized(doorPosition.x, UnitsPerStep);
            hash.AddQuantized(doorPosition.y, UnitsPerStep);
            hash.AddInt32(Convert.ToInt32(GameApi.Field<object>(fromDoor, "m_direction")));
            ulong result = hash.Value;
            return result == 0 ? 1UL : result;
        }

        private bool TryBuildRoomPrefabIndex(ulong[] hashes)
        {
            clientRoomPrefabs.Clear();
            UnityEngine.Object[] loadedObjects = Resources.FindObjectsOfTypeAll(typeof(GameObject));
            if (loadedObjects == null) return RejectMismatch("room-prefab-index-unavailable");
            for (int i = 0; i < loadedObjects.Length; i++)
            {
                GameObject candidate = loadedObjects[i] as GameObject;
                if (candidate == null) continue;
                ulong hash;
                try { hash = ArchetypeHash(candidate); }
                catch { continue; }
                if (!clientRoomPrefabs.ContainsKey(hash)) clientRoomPrefabs.Add(hash, candidate);
            }
            for (int i = 0; i < hashes.Length; i++)
            {
                if (clientRoomPrefabs.ContainsKey(hashes[i])) continue;
                RejectMismatch("room-plan-archetype-missing:" + hashes[i].ToString("x16"));
                clientRoomPrefabs.Clear();
                return false;
            }
            return true;
        }

        private void OnInputReceived(SessionInputFrame input)
        {
            lastInputSequences[input.Input.PlayerId] = input.Sequence;
            inputBridge.SetContinuous(input);
            if (!remoteInputObserved && (input.Input.MoveX != 0 || input.Input.MoveY != 0 ||
                input.Input.HeldButtons != 0))
            {
                remoteInputObserved = true;
                log.LogInfo("REMOTE_INPUT_OBSERVED slot=" + input.Input.PlayerId +
                    " sequence=" + input.Sequence + " move=" + input.Input.MoveX + ":" + input.Input.MoveY +
                    " held=" + input.Input.HeldButtons);
            }
        }

        private void OnInputEventReceived(SessionInputFrame input)
        {
            inputBridge.AddEdges(input);
            log.LogInfo("REMOTE_INPUT_EVENT slot=" + input.Input.PlayerId + " sequence=" + input.Sequence +
                " down=" + input.Input.DownButtons + " up=" + input.Input.UpButtons);
        }

        private void OnSnapshotReceived(WorldSnapshot snapshot)
        {
            string flowKey = snapshot.GameStartGeneration + ":" + snapshot.RoomIndex + ":" +
                snapshot.TransitionGeneration + ":" +
                (HasMapPlan(snapshot) ? "plan" : "compact");
            if (lastSnapshotFlowKey != flowKey)
            {
                lastSnapshotFlowKey = flowKey;
                log.LogInfo("SNAPSHOT_FLOW " + flowKey);
            }
            pendingSnapshot = snapshot;
            latestSnapshot = snapshot;
        }

        private static bool HasMapPlan(WorldSnapshot snapshot)
        {
            return snapshot != null &&
                ((snapshot.RoomArchetypeHashes != null && snapshot.RoomArchetypeHashes.Length != 0) ||
                 (snapshot.SpecialRoomArchetypeHashes != null && snapshot.SpecialRoomArchetypeHashes.Length != 0) ||
                 (snapshot.RoomCollisionDecisions != null && snapshot.RoomCollisionDecisions.Length != 0) ||
                 (snapshot.RoomSelectionDecisions != null && snapshot.RoomSelectionDecisions.Length != 0));
        }

        private void EnsureNetworkAssignments(WorldSnapshot snapshot)
        {
            object systemPlayers = UnityEngine.Object.FindObjectOfType(GameApi.Type("SystemPlayers"));
            if (systemPlayers == null) return;
            if (!session.IsAuthoritativeHost && configuredLocalSlot != session.LocalSlot)
            {
                if (!MoveLocalController(systemPlayers, session.LocalSlot)) return;
                configuredLocalSlot = session.LocalSlot;
            }

            var desiredBots = new HashSet<byte>();
            if (session.IsAuthoritativeHost)
            {
                byte[] slots = session.GetConnectedPeerSlots();
                for (int i = 0; i < slots.Length; i++) desiredBots.Add(slots[i]);
            }
            else if (snapshot != null)
            {
                for (int i = 0; i < snapshot.Players.Length; i++)
                {
                    PlayerSnapshot player = snapshot.Players[i];
                    if (player.Slot != session.LocalSlot && (player.Flags & PlayerSnapshotFlags.Active) != 0)
                        desiredBots.Add(player.Slot);
                }
            }

            object[] players = GameApi.GetPlayers();
            foreach (byte slot in desiredBots)
            {
                object player = FindPlayerBySlot(players, slot);
                if (player == null || !GameApi.Property<bool>(player, "IsActive"))
                {
                    GameApi.InvokeWithArgument(systemPlayers, "AssignBot", (int)slot);
                    players = GameApi.GetPlayers();
                }
                inputBridge.Reserve(slot);
                managedBotSlots.Add(slot);
            }

            var release = new List<byte>();
            foreach (byte slot in managedBotSlots)
                if (!desiredBots.Contains(slot)) release.Add(slot);
            for (int i = 0; i < release.Count; i++)
            {
                GameApi.InvokeWithArgument(systemPlayers, "UnassignPlayer", (int)release[i]);
                managedBotSlots.Remove(release[i]);
            }
        }

        private static bool MoveLocalController(object systemPlayers, byte targetSlot)
        {
            object[] players = GameApi.GetPlayers();
            object source = null;
            for (int i = 0; i < players.Length; i++)
            {
                object candidate = players[i];
                if (candidate != null && GameApi.Property<bool>(candidate, "IsActive") &&
                    GameApi.Property<bool>(candidate, "IsHuman"))
                {
                    source = candidate;
                    break;
                }
            }
            if (source == null) return false;
            int sourceSlot = GameApi.Property<int>(source, "Id");
            if (sourceSlot == targetSlot) return true;
            object controller = GameApi.Property<object>(source, "ControllerId");
            object profile = GameApi.Property<object>(source, "Profile");
            string name = GameApi.Property<string>(source, "Name");
            object target = FindPlayerBySlot(players, targetSlot);
            if (target != null && GameApi.Property<bool>(target, "IsActive"))
                GameApi.InvokeWithArgument(systemPlayers, "UnassignPlayer", (int)targetSlot);
            GameApi.InvokeWithArgument(systemPlayers, "UnassignPlayer", sourceSlot);
            GameApi.InvokeWithArguments(systemPlayers, "AssignPlayer", (int)targetSlot, controller);
            players = GameApi.GetPlayers();
            target = FindPlayerBySlot(players, targetSlot);
            if (target == null) return false;
            if (profile != null) GameApi.InvokeWithArgument(target, "SetProfile", profile);
            if (name != null) GameApi.InvokeWithArgument(target, "SetName", name);
            return true;
        }

        private void ReleaseNetworkAssignments()
        {
            if (managedBotSlots.Count == 0 && configuredLocalSlot == byte.MaxValue) return;
            object systemPlayers = UnityEngine.Object.FindObjectOfType(GameApi.Type("SystemPlayers"));
            if (systemPlayers == null) return;
            var release = new List<byte>(managedBotSlots);
            for (int i = 0; i < release.Count; i++)
                GameApi.InvokeWithArgument(systemPlayers, "UnassignPlayer", (int)release[i]);
            if (configuredLocalSlot != byte.MaxValue) MoveLocalController(systemPlayers, 0);
        }

        private void LogRoomContentContract(object room)
        {
            if (roomContentContractLogged || room == null) return;
            roomContentContractLogged = true;
            var entries = new List<string>();
            Type[] types = { room.GetType(), GameApi.Type("SystemLevel") };
            for (int t = 0; t < types.Length; t++)
            {
                MethodInfo[] methods = types[t].GetMethods(BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (int i = 0; i < methods.Length; i++)
                {
                    string name = methods[i].Name;
                    if (name.IndexOf("Room", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Content", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Enter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Monster", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Complete", StringComparison.OrdinalIgnoreCase) >= 0)
                        entries.Add(types[t].Name + "." + name + ":" + methods[i].GetParameters().Length);
                }
            }
            log.LogInfo("ROOM_CONTENT_CONTRACT hasContent=" + GameApi.Field<bool>(room, "m_hasContent") +
                " hasEnemies=" + GameApi.Field<bool>(room, "m_hasEnemies") +
                " playerInRoom=" + GameApi.Field<bool>(room, "m_playerInRoom") + " " +
                string.Join(",", entries.ToArray()));
        }

        private WorldSnapshot CaptureSnapshot()
        {
            var snapshot = new WorldSnapshot
            {
                HostTick = tick,
                LastInputSequences = (uint[])lastInputSequences.Clone(),
                RandomStateHash = StateHasher.CalculateRandomStateHash(),
                RandomStateWords = StateHasher.CaptureRandomStateWords(),
                GameStartGeneration = gameStartGeneration,
                GameStartRandomStateWords = (uint[])gameStartRandomStateWords.Clone(),
                RoomContentGeneration = roomContentGeneration,
                RoomContentRoomIndex = -1,
                RoomContentRandomStateWords = (uint[])roomContentRandomStateWords.Clone()
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
            Component roomComponent = room as Component;
            if (roomComponent != null)
            {
                LogRoomContentContract(room);
                snapshot.Flags |= WorldSnapshotFlags.HasCurrentRoom;
                Vector3 roomPosition = roomComponent.transform.position;
                snapshot.RoomX = Quantize(roomPosition.x);
                snapshot.RoomY = Quantize(roomPosition.y);
                snapshot.RoomDepth = GameApi.Property<int>(room, "Depth");
                object generator = GameApi.StaticProperty("SystemLevel", "MapGenerator");
                snapshot.RoomIndex = FindRoomIndex(generator, room);
                if (hostRoomContentRoom != null)
                    snapshot.RoomContentRoomIndex = FindRoomIndex(generator, hostRoomContentRoom);
                if (IsMapGenerationFinished(generator))
                {
                    snapshot.RoomArchetypeHashes = hostRoomArchetypeHashes.ToArray();
                    snapshot.SpecialRoomArchetypeHashes = hostSpecialRoomArchetypeHashes.ToArray();
                    snapshot.RoomCollisionDecisions = hostRoomCollisionDecisions.ToArray();
                    snapshot.RoomSelectionDecisions = hostRoomSelectionDecisions.ToArray();
                    snapshot.RoomLayout = CaptureRoomLayout(generator);
                    LogRoomSet(generator, session.IsAuthoritativeHost ? "host" : "client");
                }
            }
            string worldKey = snapshot.Level + ":" + snapshot.RoomX + ":" + snapshot.RoomY + ":" + snapshot.RoomDepth;
            if (lastWorldKey != null && lastWorldKey != worldKey) transitionGeneration++;
            lastWorldKey = worldKey;
            snapshot.TransitionGeneration = transitionGeneration;

            var players = new List<PlayerSnapshot>();
            object[] gamePlayers;
            try { gamePlayers = GameApi.GetPlayers(); }
            catch { gamePlayers = new object[0]; }
            object authoritativeHero = null;
            bool hasAuthoritativeHeroReference = false;
            try
            {
                object systemPlayers = UnityEngine.Object.FindObjectOfType(GameApi.Type("SystemPlayers"));
                if (systemPlayers != null)
                {
                    authoritativeHero = GameApi.Field<object>(systemPlayers, "m_hero");
                    hasAuthoritativeHeroReference = true;
                }
            }
            catch { }
            for (int i = 0; i < gamePlayers.Length && i < 4; i++)
            {
                if (gamePlayers[i] == null) continue;
                PlayerSnapshot player = CapturePlayer(gamePlayers[i]);
                if (hasAuthoritativeHeroReference)
                {
                    player.Flags &= ~PlayerSnapshotFlags.Hero;
                    if (ReferenceEquals(gamePlayers[i], authoritativeHero))
                        player.Flags |= PlayerSnapshotFlags.Hero;
                }
                if (roomComponent != null && (player.Flags & PlayerSnapshotFlags.Present) != 0)
                {
                    player.PositionX -= snapshot.RoomX;
                    player.PositionY -= snapshot.RoomY;
                }
                players.Add(player);
            }
            players.Sort(delegate(PlayerSnapshot left, PlayerSnapshot right)
            {
                return left.Slot.CompareTo(right.Slot);
            });
            snapshot.Players = players.ToArray();
            snapshot.Enemies = CaptureEnemies(gamePlayers);
            if (lastHostEnemyCount != snapshot.Enemies.Length)
            {
                lastHostEnemyCount = snapshot.Enemies.Length;
                log.LogInfo("ENEMY_FLOW role=host count=" + snapshot.Enemies.Length +
                    " room=" + snapshot.RoomIndex);
            }
            if (roomComponent != null)
            {
                for (int i = 0; i < snapshot.Enemies.Length; i++)
                {
                    snapshot.Enemies[i].PositionX -= snapshot.RoomX;
                    snapshot.Enemies[i].PositionY -= snapshot.RoomY;
                }
            }
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
            if (snapshot.RoomContentGeneration != 0 &&
                snapshot.RoomContentGeneration != roomContentAppliedGeneration &&
                snapshot.RoomContentRoomIndex >= 0)
            {
                roomContentGeneration = snapshot.RoomContentGeneration;
                pendingRoomContentRoomIndex = snapshot.RoomContentRoomIndex;
                roomContentRandomStateWords = (uint[])snapshot.RoomContentRandomStateWords.Clone();
            }
            int localLevel;
            bool localGameInProgress = false;
            try
            {
                localLevel = GameApi.InvokeStatic<int>("SystemGame", "GetLevel");
                localGameInProgress = GameApi.InvokeStatic<bool>("SystemGame", "GetGameInProgress");
            }
            catch { localLevel = -1; }
            bool hostGameInProgress = (snapshot.Flags & WorldSnapshotFlags.GameInProgress) != 0;
            if (localGameInProgress != hostGameInProgress)
            {
                if (!hasRequestedGameLifecycle || requestedGameInProgress != hostGameInProgress)
                {
                    if (hostGameInProgress)
                    {
                        if (snapshot.GameStartGeneration == 0)
                            return RejectMismatch("game-start-random-state-missing");
                        if (!HasMapPlan(snapshot))
                            return RejectMismatch("room-plan-pending");
                        if (snapshot.RoomLayout == null || snapshot.RoomLayout.Length == 0)
                            return RejectMismatch("room-layout-pending");
                        gameStartGeneration = snapshot.GameStartGeneration;
                        gameStartRandomStateWords = (uint[])snapshot.GameStartRandomStateWords.Clone();
                        clientRoomArchetypeHashes = (ulong[])snapshot.RoomArchetypeHashes.Clone();
                        clientSpecialRoomArchetypeHashes = (ulong[])snapshot.SpecialRoomArchetypeHashes.Clone();
                        clientRoomCollisionDecisions.Clear();
                        for (int i = 0; i < snapshot.RoomCollisionDecisions.Length; i++)
                        {
                            RoomCollisionDecision decision = snapshot.RoomCollisionDecisions[i];
                            Queue<RoomCollisionDecision> decisions;
                            if (!clientRoomCollisionDecisions.TryGetValue(decision.Key, out decisions))
                            {
                                decisions = new Queue<RoomCollisionDecision>();
                                clientRoomCollisionDecisions.Add(decision.Key, decisions);
                            }
                            decisions.Enqueue(decision);
                        }
                        clientRoomSelectionDecisions.Clear();
                        clientRoomDoorDecisions.Clear();
                        for (int i = 0; i < snapshot.RoomSelectionDecisions.Length; i++)
                        {
                            RoomSelectionDecision decision = snapshot.RoomSelectionDecisions[i];
                            clientRoomDoorDecisions.Enqueue(decision);
                            Queue<ulong> decisions;
                            if (!clientRoomSelectionDecisions.TryGetValue(decision.Key, out decisions))
                            {
                                decisions = new Queue<ulong>();
                                clientRoomSelectionDecisions.Add(decision.Key, decisions);
                            }
                            decisions.Enqueue(decision.ArchetypeHash);
                        }
                        var allRoomHashes = new ulong[clientRoomArchetypeHashes.Length +
                            clientSpecialRoomArchetypeHashes.Length + snapshot.RoomCollisionDecisions.Length];
                        Array.Copy(clientRoomArchetypeHashes, 0, allRoomHashes, 0,
                            clientRoomArchetypeHashes.Length);
                        Array.Copy(clientSpecialRoomArchetypeHashes, 0, allRoomHashes,
                            clientRoomArchetypeHashes.Length, clientSpecialRoomArchetypeHashes.Length);
                        int collisionOffset = clientRoomArchetypeHashes.Length +
                            clientSpecialRoomArchetypeHashes.Length;
                        for (int i = 0; i < snapshot.RoomCollisionDecisions.Length; i++)
                            allRoomHashes[collisionOffset + i] =
                                snapshot.RoomCollisionDecisions[i].ArchetypeHash;
                        if (!TryBuildRoomPrefabIndex(allRoomHashes)) return false;
                        clientRoomArchetypeIndex = 0;
                        clientSpecialRoomArchetypeIndex = 0;
                        clientKeyedRoomSelectionCount = 0;
                        mapLayoutTransitionApplied = false;
                        mapRandomStateWords = (uint[])snapshot.GameStartRandomStateWords.Clone();
                        mapRandomStateReady = true;
                        mapRandomScopeDepth = 0;
                        mapRandomScopeEntries = 0;
                        object systemGame = UnityEngine.Object.FindObjectOfType(GameApi.Type("SystemGame"));
                        if (systemGame == null) return RejectMismatch("system-game-pending");
                        if (snapshot.Level <= 0) return RejectMismatch("game-start-level-invalid:" + snapshot.Level);
                        GameApi.SetField(systemGame, "m_level", snapshot.Level - 1);
                        StateHasher.ApplyRandomStateWords(snapshot.GameStartRandomStateWords);
                        GameApi.InvokeStatic("SystemMain", "StartNewGame");
                    }
                    else GameApi.InvokeStatic("SystemMain", "ResetNewGame");
                    hasRequestedGameLifecycle = true;
                    requestedGameInProgress = hostGameInProgress;
                }
                return RejectMismatch("game-in-progress:" + localGameInProgress + "->" + hostGameInProgress);
            }
            hasRequestedGameLifecycle = false;
            if (localLevel != snapshot.Level)
            {
                if (hostGameInProgress && snapshot.Level == localLevel + 1 && requestedLevel != snapshot.Level)
                {
                    GameApi.InvokeStaticWithArgument("SystemMain", "LoadLevel", SceneManager.GetActiveScene().buildIndex);
                    requestedLevel = snapshot.Level;
                    return RejectMismatch("level-load-started:" + localLevel + "->" + snapshot.Level);
                }
                return RejectMismatch("level:" + localLevel + "->" + snapshot.Level);
            }
            requestedLevel = -1;

            object localRoom = null;
            try { localRoom = GameApi.InvokeStatic("SystemLevel", "GetCurrentRoom"); }
            catch { }
            Component localRoomComponent = localRoom as Component;
            bool hostHasRoom = (snapshot.Flags & WorldSnapshotFlags.HasCurrentRoom) != 0;
            bool snapshotHasMapPlan = HasMapPlan(snapshot);
            object localGenerator = null;
            try { localGenerator = GameApi.StaticProperty("SystemLevel", "MapGenerator"); }
            catch { return RejectMismatch("map-generator-pending"); }
            if (hostHasRoom && snapshotHasMapPlan && localGenerator != null &&
                !IsMapGenerationFinished(localGenerator))
                return RejectMismatch("map-generation-pending:selections=" + clientRoomArchetypeIndex + "/" +
                    clientRoomArchetypeHashes.Length + ":special=" + clientSpecialRoomArchetypeIndex + "/" +
                    clientSpecialRoomArchetypeHashes.Length);
            if (hostHasRoom && snapshotHasMapPlan &&
                (clientRoomArchetypeIndex != clientRoomArchetypeHashes.Length ||
                clientSpecialRoomArchetypeIndex != clientSpecialRoomArchetypeHashes.Length ||
                clientKeyedRoomSelectionCount != snapshot.RoomSelectionDecisions.Length))
                return RejectMismatch("map-plan-consumption:selections=" + clientRoomArchetypeIndex + "/" +
                    clientRoomArchetypeHashes.Length + ":special=" + clientSpecialRoomArchetypeIndex + "/" +
                    clientSpecialRoomArchetypeHashes.Length + ":keyed=" + clientKeyedRoomSelectionCount + "/" +
                    snapshot.RoomSelectionDecisions.Length);
            if (hostHasRoom && snapshotHasMapPlan)
            {
                if (!TryApplyRoomLayout(snapshot, localGenerator)) return false;
                if (!mapLayoutTransitionApplied)
                {
                    if (!TryBeginRoomTransition(snapshot))
                        return RejectMismatch("room-layout-transition-pending");
                    mapLayoutTransitionApplied = true;
                    return RejectMismatch("room-layout-transition-started");
                }
            }
            if ((localRoomComponent != null) != hostHasRoom)
            {
                if (!hostHasRoom && localRoomComponent != null &&
                    (!hasRequestedLevelComplete || SequenceWindow.IsNewer(snapshot.TransitionGeneration,
                        requestedLevelCompleteGeneration)))
                {
                    object systemLevel = UnityEngine.Object.FindObjectOfType(GameApi.Type("SystemLevel"));
                    if (systemLevel != null)
                    {
                        GameApi.Invoke(systemLevel, "DebugEndLevel");
                        FieldInfo currentRoomField = systemLevel.GetType().GetField("m_currentRoom",
                            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                            BindingFlags.NonPublic);
                        if (currentRoomField != null)
                            currentRoomField.SetValue(currentRoomField.IsStatic ? null : systemLevel, null);
                        AccessTools.Field(localGenerator.GetType(), "m_currentRoom").SetValue(localGenerator, null);
                        requestedLevelCompleteGeneration = snapshot.TransitionGeneration;
                        hasRequestedLevelComplete = true;
                        return RejectMismatch("level-complete-started:" + snapshot.TransitionGeneration);
                    }
                }
                return RejectMismatch("current-room-presence");
            }
            if (hostHasRoom) hasRequestedLevelComplete = false;
            if (localRoomComponent != null)
            {
                int roomDepth = GameApi.Property<int>(localRoom, "Depth");
                LogRoomSet(localGenerator, "client");
                int roomIndex = FindAuthoritativeRoomIndex(localGenerator, localRoom);
                string roomDiagnostic = roomIndex + ":" + roomDepth + "->" + snapshot.RoomIndex + ":" +
                    snapshot.RoomDepth;
                if (lastRoomDiagnosticKey != roomDiagnostic)
                {
                    lastRoomDiagnosticKey = roomDiagnostic;
                    log.LogInfo("ROOM_FLOW " + roomDiagnostic);
                }
                if (roomIndex != snapshot.RoomIndex || roomDepth != snapshot.RoomDepth)
                {
                    if ((!hasRequestedTransition || SequenceWindow.IsNewer(snapshot.TransitionGeneration,
                         requestedTransitionGeneration)) && TryBeginRoomTransition(snapshot))
                    {
                        requestedTransitionGeneration = snapshot.TransitionGeneration;
                        hasRequestedTransition = true;
                        return RejectMismatch("room-transition-started:" + snapshot.TransitionGeneration);
                    }
                    return RejectMismatch("room:" + roomIndex + ":" + roomDepth + "->" +
                                          snapshot.RoomIndex + ":" + snapshot.RoomDepth);
                }
            }
            if (snapshot.GameStartGeneration != 0 && HasMapPlan(snapshot))
                mapPlanReadySequence = snapshot.Sequence;

            object[] players;
            try { players = GameApi.GetPlayers(); }
            catch { return false; }

            byte localHeroSlot = byte.MaxValue;
            byte hostHeroSlot = byte.MaxValue;
            for (int i = 0; i < snapshot.Players.Length; i++)
            {
                PlayerSnapshot authoritative = snapshot.Players[i];
                object playerData = FindPlayerBySlot(players, authoritative.Slot);
                if (playerData == null) return RejectMismatch("missing-player:" + authoritative.Slot);
                PlayerSnapshot local = CapturePlayer(playerData);
                if ((local.Flags & PlayerSnapshotFlags.Hero) != 0) localHeroSlot = authoritative.Slot;
                if ((authoritative.Flags & PlayerSnapshotFlags.Hero) != 0) hostHeroSlot = authoritative.Slot;
            }
            if (localHeroSlot != hostHeroSlot)
            {
                if (hostHeroSlot == byte.MaxValue)
                    return RejectMismatch("hero-transition-without-authoritative-hero");
                if (requestedHeroSlot != hostHeroSlot && TryBeginHeroCorrection(players, hostHeroSlot))
                    requestedHeroSlot = hostHeroSlot;
                return RejectMismatch("hero-transition:" + localHeroSlot + "->" + hostHeroSlot);
            }
            requestedHeroSlot = byte.MaxValue;

            for (int i = 0; i < snapshot.Players.Length; i++)
            {
                PlayerSnapshot authoritative = snapshot.Players[i];
                object playerData = FindPlayerBySlot(players, authoritative.Slot);
                if (playerData == null) return RejectMismatch("missing-player:" + authoritative.Slot);
                PlayerSnapshot local = CapturePlayer(playerData);
                bool localAlive = (local.Flags & PlayerSnapshotFlags.Alive) != 0;
                bool hostAlive = (authoritative.Flags & PlayerSnapshotFlags.Alive) != 0;
                if (authoritative.Slot == hostHeroSlot && localAlive != hostAlive)
                {
                    if (!hasPlayerLifeRequest[authoritative.Slot] ||
                        playerLifeRequest[authoritative.Slot] != hostAlive)
                    {
                        if (TryBeginLifeCorrection(GameApi.Property<GameObject>(playerData, "GameObject"), hostAlive,
                                                   Expand(authoritative.HealthCurrent)))
                        {
                            GameApi.SetField(playerData, "m_alive", hostAlive);
                            hasPlayerLifeRequest[authoritative.Slot] = true;
                            playerLifeRequest[authoritative.Slot] = hostAlive;
                        }
                    }
                    return RejectMismatch("player-life-transition:" + authoritative.Slot + "->" + hostAlive);
                }
                hasPlayerLifeRequest[authoritative.Slot] = false;
                // Active and Bot are local controller-assignment details: the host represents
                // authenticated peers as active bots while a client can retain a present remote
                // avatar without marking that slot active. Non-hero slots are locally represented
                // as spirits or bots, so physical object presence is portable only for the hero.
                PlayerSnapshotFlags lifecycle = PlayerSnapshotFlags.Present;
                if (authoritative.Slot == hostHeroSlot &&
                    (local.Flags & lifecycle) != (authoritative.Flags & lifecycle))
                    return RejectMismatch("player-lifecycle:" + authoritative.Slot);
                if (local.HealthMaximum != authoritative.HealthMaximum)
                {
                    if (playerMaxHealthRequests[authoritative.Slot] != authoritative.HealthMaximum)
                    {
                        GameObject playerObject = GameApi.Property<GameObject>(playerData, "GameObject");
                        if (TryModifyHealthMaximum(playerObject, local.HealthMaximum,
                            authoritative.HealthMaximum))
                            playerMaxHealthRequests[authoritative.Slot] = authoritative.HealthMaximum;
                    }
                }
                else playerMaxHealthRequests[authoritative.Slot] = 0;
            }
            List<GameObject> localEnemies = GetMonsterObjects(players);
            if (lastClientEnemyCount != localEnemies.Count)
            {
                lastClientEnemyCount = localEnemies.Count;
                log.LogInfo("ENEMY_FLOW role=client local=" + localEnemies.Count +
                    " host=" + snapshot.Enemies.Length + " room=" + snapshot.RoomIndex);
            }
            if (TryBeginEnemyDespawns(snapshot, players))
                return RejectMismatch("enemy-despawn-pending");
            if (localEnemies.Count < snapshot.Enemies.Length && TryBeginEnemySpawns(snapshot, players, localEnemies))
                return RejectMismatch("enemy-spawn-pending");
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
                    enemy = MatchEnemy(authoritative, localEnemies, claimedEnemies, localRoomComponent);
                    if (enemy == null) return RejectMismatch("enemy-identity:" + authoritative.Id);
                    clientEnemies[authoritative.Id] = enemy;
                    enemySpawnRequests.Remove(authoritative.Id);
                }
                if (!claimedEnemies.Add(enemy)) return RejectMismatch("enemy-duplicate:" + authoritative.Id);
                EnemySnapshot local = CaptureEnemy(enemy, authoritative.Id);
                EnemySnapshotFlags lifecycle = EnemySnapshotFlags.AiControlled;
                if ((local.Flags & lifecycle) != (authoritative.Flags & lifecycle))
                    return RejectMismatch("enemy-lifecycle:" + authoritative.Id);
                if (local.HealthMaximum != authoritative.HealthMaximum)
                {
                    int requestedMaximum;
                    if (!enemyMaxHealthRequests.TryGetValue(authoritative.Id, out requestedMaximum) ||
                        requestedMaximum != authoritative.HealthMaximum)
                    {
                        if (TryModifyHealthMaximum(enemy, local.HealthMaximum,
                            authoritative.HealthMaximum))
                            enemyMaxHealthRequests[authoritative.Id] = authoritative.HealthMaximum;
                    }
                }
                else enemyMaxHealthRequests.Remove(authoritative.Id);
                bool localAlive = (local.Flags & EnemySnapshotFlags.Alive) != 0;
                bool hostAlive = (authoritative.Flags & EnemySnapshotFlags.Alive) != 0;
                if (localAlive != hostAlive)
                {
                    bool requestedTarget;
                    if (!enemyLifeRequests.TryGetValue(authoritative.Id, out requestedTarget) ||
                        requestedTarget != hostAlive)
                    {
                        if (TryBeginLifeCorrection(enemy, hostAlive, Expand(authoritative.HealthCurrent)))
                            enemyLifeRequests[authoritative.Id] = hostAlive;
                    }
                    return RejectMismatch("enemy-life-transition:" + authoritative.Id + "->" + hostAlive);
                }
                enemyLifeRequests.Remove(authoritative.Id);
                if ((local.Flags & EnemySnapshotFlags.Active) !=
                    (authoritative.Flags & EnemySnapshotFlags.Active))
                    return RejectMismatch("enemy-active:" + authoritative.Id);
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
                float roomPositionX = localRoomComponent == null ? 0f : localRoomComponent.transform.position.x;
                float roomPositionY = localRoomComponent == null ? 0f : localRoomComponent.transform.position.y;
                position.x = roomPositionX + Expand(authoritative.PositionX);
                position.y = roomPositionY + Expand(authoritative.PositionY);
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
                        GameApi.InvokeWithArgument(health, "ChangeHealth", target - current);
                }
            }
            for (int i = 0; i < snapshot.Enemies.Length; i++)
                ApplyEnemy(resolvedEnemies[i], snapshot.Enemies[i], localRoomComponent);
            StateHasher.ApplyRandomStateWords(snapshot.RandomStateWords);
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
            try
            {
                object systemLevel = UnityEngine.Object.FindObjectOfType(GameApi.Type("SystemLevel"));
                spawned = systemLevel == null ? null : GameApi.Invoke(systemLevel, "GetMonstersSpawned") as IEnumerable;
            }
            catch { return enemies; }
            if (spawned == null) return enemies;
            foreach (object item in spawned)
            {
                GameObject enemy = item as GameObject;
                if (enemy != null && !playerObjects.Contains(enemy)) enemies.Add(enemy);
            }
            return enemies;
        }

        private static int FindRoomIndex(object generator, object target)
        {
            if (generator == null || target == null) return -1;
            List<object> rooms = EnumerateRooms(generator);
            for (int i = 0; i < rooms.Count; i++)
                if (ReferenceEquals(rooms[i], target)) return i;
            return -1;
        }

        private int FindAuthoritativeRoomIndex(object generator, object target)
        {
            if (clientAuthoritativeRooms.Count != 0)
            {
                for (int i = 0; i < clientAuthoritativeRooms.Count; i++)
                    if (ReferenceEquals(clientAuthoritativeRooms[i], target)) return i;
            }
            return FindRoomIndex(generator, target);
        }

        private static RoomLayoutState[] CaptureRoomLayout(object generator)
        {
            List<object> rooms = EnumerateRooms(generator);
            var result = new RoomLayoutState[rooms.Count];
            for (int i = 0; i < rooms.Count; i++)
            {
                Component room = rooms[i] as Component;
                if (room == null) continue;
                result[i] = new RoomLayoutState
                {
                    ArchetypeHash = ArchetypeHash(room.gameObject),
                    ConnectionHash = RoomConnectionHash(rooms[i]),
                    Depth = GameApi.Property<int>(rooms[i], "Depth"),
                    PositionX = Quantize(room.transform.position.x),
                    PositionY = Quantize(room.transform.position.y),
                    RotationZ = Quantize(room.transform.eulerAngles.z)
                };
            }
            return result;
        }

        private bool TryApplyRoomLayout(WorldSnapshot snapshot, object generator)
        {
            if (snapshot.RoomLayout == null || snapshot.RoomLayout.Length == 0)
                return RejectMismatch("room-layout-missing");
            List<object> rooms = EnumerateRooms(generator);
            if (rooms.Count != snapshot.RoomLayout.Length)
                return RejectMismatch("room-layout-count:" + rooms.Count + "->" + snapshot.RoomLayout.Length);
            var claimed = new HashSet<object>();
            clientAuthoritativeRooms.Clear();
            for (int i = 0; i < snapshot.RoomLayout.Length; i++)
            {
                RoomLayoutState authoritative = snapshot.RoomLayout[i];
                object matched = null;
                for (int j = 0; j < rooms.Count; j++)
                {
                    Component candidate = rooms[j] as Component;
                    if (candidate == null || claimed.Contains(rooms[j])) continue;
                    if (ArchetypeHash(candidate.gameObject) == authoritative.ArchetypeHash &&
                        GameApi.Property<int>(rooms[j], "Depth") == authoritative.Depth &&
                        RoomConnectionHash(rooms[j]) == authoritative.ConnectionHash)
                    {
                        matched = rooms[j];
                        break;
                    }
                }
                if (matched == null)
                {
                    clientAuthoritativeRooms.Clear();
                    return RejectMismatch("room-layout-identity:" + i);
                }
                claimed.Add(matched);
                clientAuthoritativeRooms.Add(matched);
            }
            for (int i = 0; i < clientAuthoritativeRooms.Count; i++)
            {
                Component room = (Component)clientAuthoritativeRooms[i];
                Vector3 position = room.transform.position;
                position.x = Expand(snapshot.RoomLayout[i].PositionX);
                position.y = Expand(snapshot.RoomLayout[i].PositionY);
                room.transform.position = position;
                Vector3 angles = room.transform.eulerAngles;
                angles.z = Expand(snapshot.RoomLayout[i].RotationZ);
                room.transform.eulerAngles = angles;
            }
            return true;
        }

        private static ulong RoomConnectionHash(object room)
        {
            var hash = new StableHash64();
            IEnumerable doors = GameApi.Field<IEnumerable>(room, "m_doors");
            int count = 0;
            if (doors != null)
            {
                foreach (object door in doors)
                {
                    count++;
                    Vector2 position = GameApi.Field<Vector2>(door, "m_position");
                    hash.AddQuantized(position.x, UnitsPerStep);
                    hash.AddQuantized(position.y, UnitsPerStep);
                    hash.AddInt32(Convert.ToInt32(GameApi.Field<object>(door, "m_direction")));
                    object target = GameApi.Field<object>(door, "m_room");
                    Component targetComponent = target as Component;
                    hash.AddBoolean(targetComponent != null);
                    if (targetComponent != null)
                    {
                        ulong targetHash = ArchetypeHash(targetComponent.gameObject);
                        hash.AddUInt32((uint)targetHash);
                        hash.AddUInt32((uint)(targetHash >> 32));
                        hash.AddInt32(GameApi.Property<int>(target, "Depth"));
                    }
                }
            }
            hash.AddInt32(count);
            ulong result = hash.Value;
            return result == 0 ? 1UL : result;
        }

        private static bool IsMapGenerationFinished(object generator)
        {
            return generator != null && GameApi.Field<bool>(generator, "m_finished") &&
                GameApi.Field<object>(generator, "m_finalRoom") as Component != null;
        }

        private static object FindRoomByIndex(object generator, int index)
        {
            if (generator == null || index < 0) return null;
            List<object> rooms = EnumerateRooms(generator);
            return index < rooms.Count ? rooms[index] : null;
        }

        private static List<object> EnumerateRooms(object generator)
        {
            var rooms = new List<object>();
            var pending = new Queue<object>();
            var seen = new HashSet<object>();
            object root = GameApi.Invoke(generator, "GetRootRoom");
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

        private void LogRoomSet(object generator, string role)
        {
            if (roomSetLogged || generator == null) return;
            List<object> rooms = role == "client" && clientAuthoritativeRooms.Count != 0
                ? new List<object>(clientAuthoritativeRooms)
                : EnumerateRooms(generator);
            if (rooms.Count == 0) return;
            var layout = new StableHash64();
            layout.AddInt32(rooms.Count);
            for (int i = 0; i < rooms.Count; i++)
            {
                Component room = rooms[i] as Component;
                if (room == null) continue;
                ulong archetype = ArchetypeHash(room.gameObject);
                layout.AddInt32(i);
                layout.AddInt32(GameApi.Property<int>(rooms[i], "Depth"));
                layout.AddUInt32((uint)archetype);
                layout.AddUInt32((uint)(archetype >> 32));
                layout.AddQuantized(room.transform.position.x, UnitsPerStep);
                layout.AddQuantized(room.transform.position.y, UnitsPerStep);
                layout.AddQuantized(room.transform.eulerAngles.z, UnitsPerStep);
                IEnumerable doors = GameApi.Field<IEnumerable>(rooms[i], "m_doors");
                if (doors == null) continue;
                ICollection doorCollection = doors as ICollection;
                layout.AddInt32(doorCollection == null ? -1 : doorCollection.Count);
                foreach (object door in doors)
                {
                    Vector2 position = GameApi.Field<Vector2>(door, "m_position");
                    layout.AddQuantized(position.x, UnitsPerStep);
                    layout.AddQuantized(position.y, UnitsPerStep);
                    layout.AddInt32(Convert.ToInt32(GameApi.Field<object>(door, "m_direction")));
                    object target = GameApi.Field<object>(door, "m_room");
                    int targetIndex = -1;
                    for (int j = 0; j < rooms.Count; j++)
                        if (ReferenceEquals(rooms[j], target)) { targetIndex = j; break; }
                    layout.AddInt32(targetIndex);
                }
            }
            roomSetLogged = true;
            log.LogInfo("MAP_READY role=" + role + " count=" + rooms.Count +
                        " layout=" + layout.Value.ToString("x16") +
                        " rngScopes=" + mapRandomScopeEntries +
                        " selections=" + (session.IsAuthoritativeHost ? hostRoomArchetypeHashes.Count :
                            clientRoomArchetypeIndex) +
                        " keyed=" + (session.IsAuthoritativeHost ? hostRoomSelectionDecisions.Count :
                            clientKeyedRoomSelectionCount) +
                        " special=" + (session.IsAuthoritativeHost ? hostSpecialRoomArchetypeHashes.Count :
                            clientSpecialRoomArchetypeIndex));
        }

        private bool TryBeginRoomTransition(WorldSnapshot snapshot)
        {
            object generator = GameApi.StaticProperty("SystemLevel", "MapGenerator");
            if (generator == null) return false;
            object room = clientAuthoritativeRooms.Count != 0 && snapshot.RoomIndex >= 0 &&
                snapshot.RoomIndex < clientAuthoritativeRooms.Count
                ? clientAuthoritativeRooms[snapshot.RoomIndex]
                : FindRoomByIndex(generator, snapshot.RoomIndex);
            if (room == null || GameApi.Property<int>(room, "Depth") != snapshot.RoomDepth) return false;
            object systemLevel = UnityEngine.Object.FindObjectOfType(GameApi.Type("SystemLevel"));
            if (systemLevel == null) return false;
            Component destination = room as Component;
            if (destination == null) return false;
            object[] players = GameApi.GetPlayers();
            for (int i = 0; i < snapshot.Players.Length; i++)
            {
                PlayerSnapshot authoritative = snapshot.Players[i];
                if ((authoritative.Flags & PlayerSnapshotFlags.Present) == 0) continue;
                object playerData = FindPlayerBySlot(players, authoritative.Slot);
                if (playerData == null) continue;
                GameObject playerObject = GameApi.Property<GameObject>(playerData, "GameObject");
                if (playerObject == null) continue;
                Vector3 position = playerObject.transform.position;
                position.x = destination.transform.position.x + Expand(authoritative.PositionX);
                position.y = destination.transform.position.y + Expand(authoritative.PositionY);
                playerObject.transform.position = position;
            }
            GameApi.InvokeWithArgument(systemLevel, "OnTeleportToRoom", room);
            AccessTools.Field(generator.GetType(), "m_currentRoom").SetValue(generator, room);
            FieldInfo systemCurrentRoom = systemLevel.GetType().GetField("m_currentRoom",
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (systemCurrentRoom != null)
                systemCurrentRoom.SetValue(systemCurrentRoom.IsStatic ? null : systemLevel, room);
            return true;
        }

        private bool TryBeginEnemyDespawns(WorldSnapshot snapshot, object[] players)
        {
            var desired = new HashSet<uint>();
            for (int i = 0; i < snapshot.Enemies.Length; i++) desired.Add(snapshot.Enemies[i].Id);
            bool pending = false;
            var removeMappings = new List<uint>();
            foreach (KeyValuePair<uint, GameObject> pair in clientEnemies)
            {
                if (desired.Contains(pair.Key)) continue;
                GameObject enemy = pair.Value;
                if (enemy == null || IsAssignedPlayerObject(enemy, players))
                {
                    removeMappings.Add(pair.Key);
                    continue;
                }
                pending = true;
                Component health = enemy.GetComponent(GameApi.Type("Health"));
                if (health != null && GameApi.Invoke<bool>(health, "IsAlive"))
                {
                    bool requestedAlive;
                    if (!enemyLifeRequests.TryGetValue(pair.Key, out requestedAlive) || requestedAlive)
                    {
                        GameApi.Invoke(health, "Suicide");
                        enemyLifeRequests[pair.Key] = false;
                    }
                }
                else if (enemyDespawnRequests.Add(pair.Key))
                {
                    Component player = enemy.GetComponent(GameApi.Type("Player"));
                    if (player != null) GameApi.Invoke(player, "Despawn");
                }
            }
            for (int i = 0; i < removeMappings.Count; i++)
            {
                clientEnemies.Remove(removeMappings[i]);
                enemyDespawnRequests.Remove(removeMappings[i]);
                enemyLifeRequests.Remove(removeMappings[i]);
                enemySpawnRequests.Remove(removeMappings[i]);
            }
            return pending;
        }

        private bool TryBeginEnemySpawns(WorldSnapshot snapshot, object[] players, List<GameObject> localEnemies)
        {
            bool began = false;
            var availableByArchetype = new Dictionary<ulong, int>();
            for (int i = 0; i < localEnemies.Count; i++)
            {
                ulong hash = ArchetypeHash(localEnemies[i]);
                int count;
                availableByArchetype.TryGetValue(hash, out count);
                availableByArchetype[hash] = count + 1;
            }
            for (int i = 0; i < snapshot.Enemies.Length; i++)
            {
                EnemySnapshot enemy = snapshot.Enemies[i];
                GameObject mapped;
                if (clientEnemies.TryGetValue(enemy.Id, out mapped) && mapped != null) continue;
                int available;
                if (availableByArchetype.TryGetValue(enemy.ArchetypeHash, out available) && available > 0)
                {
                    availableByArchetype[enemy.ArchetypeHash] = available - 1;
                    continue;
                }
                if (enemy.OwnerSlot == byte.MaxValue || enemySpawnRequests.Contains(enemy.Id)) continue;
                object owner = FindPlayerBySlot(players, enemy.OwnerSlot);
                if (owner == null) continue;
                GameObject prefab;
                try { prefab = GameApi.Invoke<GameObject>(owner, "GetFodderPrefab"); }
                catch { continue; }
                if (prefab == null || ArchetypeHash(prefab) != enemy.ArchetypeHash) continue;
                object systemLevel = UnityEngine.Object.FindObjectOfType(GameApi.Type("SystemLevel"));
                if (systemLevel == null) continue;
                object currentRoom = GameApi.InvokeStatic("SystemLevel", "GetCurrentRoom");
                Component currentRoomComponent = currentRoom as Component;
                if (currentRoomComponent == null) continue;
                var position = new Vector2(currentRoomComponent.transform.position.x + Expand(enemy.PositionX),
                    currentRoomComponent.transform.position.y + Expand(enemy.PositionY));
                bool spawned = (bool)GameApi.InvokeWithArguments(systemLevel, "SpawnMonster", owner, prefab, position, false);
                if (spawned)
                {
                    enemySpawnRequests.Add(enemy.Id);
                    began = true;
                }
            }
            return began;
        }

        private static bool IsAssignedPlayerObject(GameObject gameObject, object[] players)
        {
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] != null && GameApi.Property<GameObject>(players[i], "GameObject") == gameObject)
                    return true;
            }
            return false;
        }

        private static bool TryBeginLifeCorrection(GameObject gameObject, bool alive, float health)
        {
            if (gameObject == null) return false;
            Component component = gameObject.GetComponent(GameApi.Type("Health"));
            if (component == null) return false;
            if (alive) GameApi.InvokeWithArgument(component, "Resurrect", Math.Max(health, 0.0001f));
            else GameApi.Invoke(component, "Suicide");
            return true;
        }

        private static bool TryBeginHeroCorrection(object[] players, byte heroSlot)
        {
            object systemPlayers = UnityEngine.Object.FindObjectOfType(GameApi.Type("SystemPlayers"));
            if (systemPlayers == null) return false;
            object hero = null;
            for (int i = 0; i < players.Length; i++)
            {
                object player = players[i];
                if (player == null) continue;
                bool isHero = GameApi.Property<int>(player, "Id") == heroSlot;
                GameApi.SetField(player, "m_isHero", isHero);
                if (isHero) hero = player;
            }
            if (hero == null) return false;
            GameApi.SetField(systemPlayers, "m_hero", hero);
            GameApi.Invoke(systemPlayers, "OnHeroChange");
            return true;
        }

        private static EnemySnapshot CaptureEnemy(GameObject gameObject, uint id)
        {
            var snapshot = new EnemySnapshot
            {
                Id = id,
                ArchetypeHash = ArchetypeHash(gameObject),
                OwnerSlot = byte.MaxValue,
                PositionX = Quantize(gameObject.transform.position.x),
                PositionY = Quantize(gameObject.transform.position.y)
            };
            if (gameObject.activeInHierarchy) snapshot.Flags |= EnemySnapshotFlags.Active;
            Component player = gameObject.GetComponent(GameApi.Type("Player"));
            if (player != null)
            {
                snapshot.State = Convert.ToInt16(GameApi.Invoke(player, "GetState"));
                if (GameApi.Invoke<bool>(player, "GetIsAI")) snapshot.Flags |= EnemySnapshotFlags.AiControlled;
                object owner = GameApi.Invoke(player, "GetPlayerData");
                if (owner == null) owner = GameApi.Field<object>(player, "m_spawner");
                if (owner != null)
                {
                    int slot = GameApi.Property<int>(owner, "Id");
                    if (slot >= 0 && slot <= 3) snapshot.OwnerSlot = (byte)slot;
                }
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
            HashSet<GameObject> claimed, Component room)
        {
            GameObject best = null;
            double bestDistance = double.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                GameObject candidate = candidates[i];
                if (claimed.Contains(candidate) || ArchetypeHash(candidate) != authoritative.ArchetypeHash) continue;
                float roomX = room == null ? 0f : room.transform.position.x;
                float roomY = room == null ? 0f : room.transform.position.y;
                long dx = (long)Quantize(candidate.transform.position.x - roomX) -
                    authoritative.PositionX;
                long dy = (long)Quantize(candidate.transform.position.y - roomY) -
                    authoritative.PositionY;
                double distance = (double)dx * dx + (double)dy * dy;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }
            return best;
        }

        private static void ApplyEnemy(GameObject gameObject, EnemySnapshot authoritative, Component room)
        {
            Vector3 position = gameObject.transform.position;
            float roomX = room == null ? 0f : room.transform.position.x;
            float roomY = room == null ? 0f : room.transform.position.y;
            position.x = roomX + Expand(authoritative.PositionX);
            position.y = roomY + Expand(authoritative.PositionY);
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
                    GameApi.InvokeWithArgument(health, "ChangeHealth", target - current);
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

        private bool TryModifyHealthMaximum(GameObject gameObject, int localMaximum,
            int authoritativeMaximum)
        {
            Component health = gameObject == null ? null : gameObject.GetComponent(GameApi.Type("Health"));
            if (health == null) return false;
            MethodInfo method = AccessTools.Method(health.GetType(), "ModifyHealthMax",
                new[] { typeof(float) });
            if (method == null)
                method = AccessTools.Method(health.GetType(), "ModifyHealthMaxInternal",
                    new[] { typeof(float) });
            object modifier = null;
            if (method == null)
            {
                MethodInfo[] healthMethods = health.GetType().GetMethods(BindingFlags.Instance |
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (int pass = 0; pass < 2 && method == null; pass++)
                {
                    string methodName = pass == 0 ? "ModifyHealthMaxInternal" : "ModifyHealthMax";
                    for (int i = 0; i < healthMethods.Length; i++)
                    {
                        if (healthMethods[i].Name != methodName) continue;
                        ParameterInfo[] parameters = healthMethods[i].GetParameters();
                        if (parameters.Length != 1) continue;
                        if (TryCreateHealthModifier(parameters[0].ParameterType,
                            Expand(localMaximum), Expand(authoritativeMaximum), out modifier))
                        {
                            method = healthMethods[i];
                            break;
                        }
                    }
                }
            }
            if (method == null)
            {
                return false;
            }
            method.Invoke(health, new[] { modifier ?? (object)Expand(authoritativeMaximum - localMaximum) });
            return true;
        }

        private static bool TryCreateHealthModifier(Type stackType, float localMaximum,
            float authoritativeMaximum, out object modifier)
        {
            modifier = null;
            ConstructorInfo constructor = stackType.GetConstructor(Type.EmptyTypes);
            if (constructor == null) return false;
            MethodInfo add = null;
            MethodInfo modify = null;
            MethodInfo[] methods = stackType.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int i = 0; i < methods.Length; i++)
            {
                ParameterInfo[] parameters = methods[i].GetParameters();
                if (methods[i].Name == "Add" && parameters.Length == 4 &&
                    parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType.IsEnum &&
                    parameters[2].ParameterType == typeof(float) && parameters[3].ParameterType == typeof(float))
                    add = methods[i];
                else if (methods[i].Name == "Modify" && parameters.Length == 1 &&
                    parameters[0].ParameterType == typeof(float))
                    modify = methods[i];
            }
            if (add == null || modify == null) return false;
            Type enumType = add.GetParameters()[1].ParameterType;
            Array enumValues = Enum.GetValues(enumType);
            float delta = authoritativeMaximum - localMaximum;
            float ratio = Math.Abs(localMaximum) < 0.001f ? 1f : authoritativeMaximum / localMaximum;
            float[,] arguments =
            {
                { delta, 0f }, { delta, 1f },
                { authoritativeMaximum, 0f }, { authoritativeMaximum, 1f },
                { ratio, 0f }, { ratio, 1f }
            };
            for (int e = 0; e < enumValues.Length; e++)
            {
                for (int a = 0; a < arguments.GetLength(0); a++)
                {
                    object candidate = constructor.Invoke(null);
                    add.Invoke(candidate, new object[]
                    {
                        "crawl-online-authority", enumValues.GetValue(e),
                        arguments[a, 0], arguments[a, 1]
                    });
                    float result = Convert.ToSingle(modify.Invoke(candidate,
                        new object[] { localMaximum }));
                    if (Math.Abs(result - authoritativeMaximum) > 0.001f) continue;
                    modifier = candidate;
                    return true;
                }
            }
            return false;
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
