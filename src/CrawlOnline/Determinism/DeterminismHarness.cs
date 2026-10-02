using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Logging;
using CrawlOnline.Protocol;
using HarmonyLib;
using UnityEngine;

namespace CrawlOnline.Determinism
{
    internal enum HarnessMode
    {
        Off,
        Record,
        Replay
    }

    internal sealed class DeterminismHarness : IDisposable
    {
        private const string HarmonyId = "dev.teagar.crawl-online.determinism";
        private const byte ActionButton = 1;
        private const byte BackButton = 2;
        private const byte StartButton = 4;
        private static DeterminismHarness active;

        private readonly ManualLogSource log;
        private readonly HarnessMode mode;
        private readonly string tracePath;
        private int seed;
        private uint hashInterval;
        private readonly Harmony harmony;
        private readonly Dictionary<ulong, InputFrame> replayInputs = new Dictionary<ulong, InputFrame>();
        private readonly Dictionary<uint, StateHashRecord> expectedHashes = new Dictionary<uint, StateHashRecord>();
        private readonly Dictionary<int, InputFrame> currentInputs = new Dictionary<int, InputFrame>();
        private TraceWriter writer;
        private TraceWriter replayWriter;
        private StreamWriter report;
        private uint logicalFrame;
        private int fixedSteps;
        private int inputUpdateDepth;
        private bool randomSeeded;
        private bool failed;

        private DeterminismHarness(ManualLogSource logSource, HarnessMode harnessMode, string path)
        {
            log = logSource;
            mode = harnessMode;
            tracePath = path;
            seed = ReadIntEnvironment("CRAWL_ONLINE_SEED", 1337);
            hashInterval = (uint)Math.Max(1, ReadIntEnvironment("CRAWL_ONLINE_HASH_INTERVAL", 30));

            int targetFrameRate = ReadIntEnvironment("CRAWL_ONLINE_TARGET_FPS", 60);
            if (targetFrameRate > 0)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = targetFrameRate;
            }

            if (mode == HarnessMode.Record)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(tracePath)));
            }
            else
            {
                TraceHeader replayHeader = LoadReplay(tracePath);
                replayWriter = new TraceWriter(File.Create(tracePath + ".replay.cotr"), replayHeader, false);
                report = new StreamWriter(tracePath + ".report.csv", false);
                report.WriteLine("tick,exact_expected,exact_actual,quantized_expected,quantized_actual,match");
            }

            harmony = new Harmony(HarmonyId);
            active = this;
            PatchMethods();
            log.LogInfo("Determinism harness " + mode.ToString().ToLowerInvariant() + " active: " + tracePath);
        }

        public static DeterminismHarness TryCreate(ManualLogSource log)
        {
            string value = Environment.GetEnvironmentVariable("CRAWL_ONLINE_TRACE_MODE");
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            HarnessMode mode;
            if (string.Equals(value, "record", StringComparison.OrdinalIgnoreCase))
            {
                mode = HarnessMode.Record;
            }
            else if (string.Equals(value, "replay", StringComparison.OrdinalIgnoreCase))
            {
                mode = HarnessMode.Replay;
            }
            else
            {
                throw new InvalidOperationException("CRAWL_ONLINE_TRACE_MODE must be record or replay.");
            }

            string path = Environment.GetEnvironmentVariable("CRAWL_ONLINE_TRACE_PATH");
            if (string.IsNullOrEmpty(path))
            {
                throw new InvalidOperationException("CRAWL_ONLINE_TRACE_PATH is required in harness mode.");
            }

            return new DeterminismHarness(log, mode, path);
        }

        public void FixedTick()
        {
            if (failed) return;
            if (fixedSteps < byte.MaxValue)
            {
                fixedSteps++;
            }
        }

        public void LateTick()
        {
            if (failed) return;
            try
            {
                LateTickCore();
            }
            catch (Exception exception)
            {
                Disable(exception);
            }
        }

        private void LateTickCore()
        {
            if (!randomSeeded || logicalFrame == 0)
            {
                return;
            }

            if (mode == HarnessMode.Record)
            {
                writer.WriteFrameTiming(new FrameTimingRecord
                {
                    Tick = logicalFrame,
                    UnityFrameCount = Time.frameCount,
                    DeltaMicroseconds = ToMicroseconds(Time.deltaTime),
                    FixedDeltaMicroseconds = ToMicroseconds(Time.fixedDeltaTime),
                    FixedSteps = (byte)fixedSteps
                });
            }
            else
            {
                replayWriter.WriteFrameTiming(new FrameTimingRecord
                {
                    Tick = logicalFrame,
                    UnityFrameCount = Time.frameCount,
                    DeltaMicroseconds = ToMicroseconds(Time.deltaTime),
                    FixedDeltaMicroseconds = ToMicroseconds(Time.fixedDeltaTime),
                    FixedSteps = (byte)fixedSteps
                });
            }
            fixedSteps = 0;

            if (logicalFrame % hashInterval != 0)
            {
                return;
            }

            StateHashRecord actual = StateHasher.Calculate(logicalFrame);
            if (mode == HarnessMode.Record)
            {
                writer.WriteStateHash(actual);
                writer.Flush();
            }
            else
            {
                replayWriter.WriteStateHash(actual);
                replayWriter.Flush();
                StateHashRecord expected;
                bool found = expectedHashes.TryGetValue(logicalFrame, out expected);
                if (!found)
                {
                    return;
                }
                bool match = found && expected.ExactHash == actual.ExactHash;
                report.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1:x16},{2:x16},{3:x16},{4:x16},{5}",
                    logicalFrame,
                    found ? expected.ExactHash : 0UL,
                    actual.ExactHash,
                    found ? expected.QuantizedHash : 0UL,
                    actual.QuantizedHash,
                    match ? "1" : "0"));
                report.Flush();
                if (!match)
                {
                    log.LogWarning("Determinism divergence at logical frame " + logicalFrame);
                }
            }
        }

        public void Dispose()
        {
            if (active == this)
            {
                active = null;
            }
            harmony.UnpatchAll(HarmonyId);
            if (writer != null)
            {
                writer.Dispose();
                writer = null;
            }
            if (report != null)
            {
                report.Dispose();
                report = null;
            }
            if (replayWriter != null)
            {
                replayWriter.Dispose();
                replayWriter = null;
            }
        }

        private void PatchMethods()
        {
            PatchInputUpdate(GameApi.Type("SystemInput"));
            PatchInputUpdate(GameApi.Type("SystemInputCab"));
            PatchPlayerInput("GetInputMove", "MovePrefix");
            PatchPlayerInput("GetInputDownAction", "DownActionPrefix");
            PatchPlayerInput("GetInputDownBack", "DownBackPrefix");
            PatchPlayerInput("GetInputDownStart", "DownStartPrefix");
            PatchPlayerInput("GetInputUpAction", "UpActionPrefix");
            PatchPlayerInput("GetInputUpBack", "UpBackPrefix");
            PatchPlayerInput("GetInputUpStart", "UpStartPrefix");
            PatchPlayerInput("GetInputAction", "ActionPrefix");
            PatchPlayerInput("GetInputBack", "BackPrefix");
            PatchPlayerInput("GetInputStart", "StartPrefix");

            harmony.Patch(AccessTools.Method(GameApi.Type("SystemGame"), "OnLevelLoad"),
                new HarmonyMethod(typeof(DeterminismHarness), "LevelLoadPrefix"), null);
            harmony.Patch(AccessTools.Method(GameApi.Type("SystemSave"), "SaveAllUsers"),
                new HarmonyMethod(typeof(DeterminismHarness), "SkipSavePrefix"), null);
            harmony.Patch(AccessTools.Method(GameApi.Type("SystemSave"), "DeleteAll"),
                new HarmonyMethod(typeof(DeterminismHarness), "SkipVoidPrefix"), null);
            harmony.Patch(AccessTools.Method(GameApi.Type("AchievementStrategySteam"), "Unlock"),
                new HarmonyMethod(typeof(DeterminismHarness), "SkipVoidPrefix"), null);
            harmony.Patch(AccessTools.Method(GameApi.Type("AchievementStrategySteam"), "DebugClear"),
                new HarmonyMethod(typeof(DeterminismHarness), "SkipVoidPrefix"), null);
        }

        private void PatchInputUpdate(Type type)
        {
            harmony.Patch(AccessTools.Method(type, "UpdateInternal"),
                new HarmonyMethod(typeof(DeterminismHarness), "InputUpdatePrefix"),
                new HarmonyMethod(typeof(DeterminismHarness), "InputUpdatePostfix"));
        }

        private void PatchPlayerInput(string method, string prefix)
        {
            harmony.Patch(AccessTools.Method(GameApi.Type("PlayerData"), method),
                new HarmonyMethod(typeof(DeterminismHarness), prefix), null);
        }

        private void BeforeInputUpdate()
        {
            inputUpdateDepth++;
            if (inputUpdateDepth == 1 && randomSeeded)
            {
                logicalFrame++;
            }
        }

        private void AfterInputUpdate()
        {
            try
            {
                if (inputUpdateDepth == 1 && mode == HarnessMode.Record && randomSeeded)
                {
                    currentInputs.Clear();
                    object[] players = GameApi.GetPlayers();
                    for (int i = 0; i < players.Length; i++)
                    {
                        object player = players[i];
                        if (player != null && !GameApi.Property<bool>(player, "IsBot"))
                        {
                            InputFrame captured = CaptureInput(player);
                            currentInputs[GameApi.Property<int>(player, "Id")] = captured;
                            writer.WriteInput(captured);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                log.LogWarning("Input trace capture skipped: " + exception.GetType().Name);
            }
            finally
            {
                inputUpdateDepth--;
            }
        }

        private InputFrame CaptureInput(object player)
        {
            Vector2 move = GameApi.Invoke<Vector2>(player, "GetInputMove");
            return new InputFrame
            {
                Tick = logicalFrame,
                PlayerId = (byte)GameApi.Property<int>(player, "Id"),
                MoveX = QuantizeAxis(move.x),
                MoveY = QuantizeAxis(move.y),
                HeldButtons = MakeButtons(GameApi.Invoke<bool>(player, "GetInputAction"), GameApi.Invoke<bool>(player, "GetInputBack"), GameApi.Invoke<bool>(player, "GetInputStart")),
                DownButtons = MakeButtons(GameApi.Invoke<bool>(player, "GetInputDownAction"), GameApi.Invoke<bool>(player, "GetInputDownBack"), GameApi.Invoke<bool>(player, "GetInputDownStart")),
                UpButtons = MakeButtons(GameApi.Invoke<bool>(player, "GetInputUpAction"), GameApi.Invoke<bool>(player, "GetInputUpBack"), GameApi.Invoke<bool>(player, "GetInputUpStart"))
            };
        }

        private bool TryGetInput(object player, out InputFrame frame)
        {
            int playerId = GameApi.Property<int>(player, "Id");
            frame = new InputFrame { Tick = logicalFrame, PlayerId = (byte)playerId };
            if (!randomSeeded || GameApi.Property<bool>(player, "IsBot"))
            {
                return false;
            }

            if (mode == HarnessMode.Record)
            {
                return currentInputs.TryGetValue(playerId, out frame);
            }

            return replayInputs.TryGetValue(MakeKey(logicalFrame, playerId), out frame);
        }

        private void SeedRandom(bool isGameScene)
        {
            if (isGameScene && !randomSeeded)
            {
                UnityEngine.Random.InitState(seed);
                logicalFrame = 0;
                fixedSteps = 0;
                if (mode == HarnessMode.Record)
                {
                    writer = new TraceWriter(File.Create(tracePath), new TraceHeader
                    {
                        RandomSeed = seed,
                        FixedTicksPerSecond = (ushort)Math.Max(1, Mathf.RoundToInt(1f / Time.fixedDeltaTime)),
                        PlayerCount = DeterminePlayerCount(),
                        StateHashInterval = hashInterval
                    }, false);
                }
                randomSeeded = true;
                log.LogInfo("Determinism RNG initialized with seed " + seed);
            }
        }

        private void Disable(Exception exception)
        {
            failed = true;
            if (active == this) active = null;
            log.LogError("Determinism harness disabled after error: " + exception);
            if (writer != null)
            {
                writer.Dispose();
                writer = null;
            }
            if (report != null)
            {
                report.Dispose();
                report = null;
            }
            if (replayWriter != null)
            {
                replayWriter.Dispose();
                replayWriter = null;
            }
        }

        private TraceHeader LoadReplay(string path)
        {
            using (var reader = new TraceReader(File.OpenRead(path), false))
            {
                TraceHeader header = reader.Header;
                if (header.RandomSeed != seed)
                {
                    log.LogWarning("Replay seed environment differs from trace; trace seed wins.");
                }
                seed = header.RandomSeed;
                hashInterval = header.StateHashInterval;
                TraceRecord record;
                while (reader.TryRead(out record))
                {
                    if (record.Type == TraceRecordType.Input)
                    {
                        replayInputs[MakeKey(record.Input.Tick, record.Input.PlayerId)] = record.Input;
                    }
                    else if (record.Type == TraceRecordType.StateHash)
                    {
                        expectedHashes[record.StateHash.Tick] = record.StateHash;
                    }
                }
                return header;
            }
        }

        private static ulong MakeKey(uint tick, int playerId)
        {
            return ((ulong)tick << 8) | (byte)playerId;
        }

        private static short QuantizeAxis(float value)
        {
            return (short)Mathf.Clamp(Mathf.RoundToInt(value * short.MaxValue), short.MinValue, short.MaxValue);
        }

        private static float ExpandAxis(short value)
        {
            return value / (float)short.MaxValue;
        }

        private static byte MakeButtons(bool action, bool back, bool start)
        {
            return (byte)((action ? ActionButton : 0) | (back ? BackButton : 0) | (start ? StartButton : 0));
        }

        private static bool HasButton(byte buttons, byte button)
        {
            return (buttons & button) != 0;
        }

        private static uint ToMicroseconds(float seconds)
        {
            return (uint)Math.Max(0, Math.Round(seconds * 1000000.0, MidpointRounding.AwayFromZero));
        }

        private static int ReadIntEnvironment(string name, int fallback)
        {
            int parsed;
            return int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static byte DeterminePlayerCount()
        {
            try
            {
                int count = GameApi.InvokeStatic<int>("SystemPlayers", "GetNumActivePlayers");
                return (byte)Math.Max(1, Math.Min(4, count));
            }
            catch
            {
                return 4;
            }
        }

        private static void InputUpdatePrefix()
        {
            if (active != null) active.BeforeInputUpdate();
        }

        private static void InputUpdatePostfix()
        {
            if (active != null) active.AfterInputUpdate();
        }

        private static void LevelLoadPrefix(bool isGameScene)
        {
            if (active == null) return;
            try
            {
                active.SeedRandom(isGameScene);
            }
            catch (Exception exception)
            {
                active.Disable(exception);
            }
        }

        private static bool SkipSavePrefix(ref Coroutine __result)
        {
            __result = null;
            return false;
        }

        private static bool SkipVoidPrefix()
        {
            return false;
        }

        private static bool MovePrefix(object __instance, ref Vector2 __result)
        {
            InputFrame frame;
            if (active == null || !active.TryGetInput(__instance, out frame)) return true;
            __result = new Vector2(ExpandAxis(frame.MoveX), ExpandAxis(frame.MoveY));
            return false;
        }

        private static bool DownActionPrefix(object __instance, ref bool __result) { return ButtonPrefix(__instance, ref __result, 1, ActionButton); }
        private static bool DownBackPrefix(object __instance, ref bool __result) { return ButtonPrefix(__instance, ref __result, 1, BackButton); }
        private static bool DownStartPrefix(object __instance, ref bool __result) { return ButtonPrefix(__instance, ref __result, 1, StartButton); }
        private static bool UpActionPrefix(object __instance, ref bool __result) { return ButtonPrefix(__instance, ref __result, 2, ActionButton); }
        private static bool UpBackPrefix(object __instance, ref bool __result) { return ButtonPrefix(__instance, ref __result, 2, BackButton); }
        private static bool UpStartPrefix(object __instance, ref bool __result) { return ButtonPrefix(__instance, ref __result, 2, StartButton); }
        private static bool ActionPrefix(object __instance, ref bool __result) { return ButtonPrefix(__instance, ref __result, 0, ActionButton); }
        private static bool BackPrefix(object __instance, ref bool __result) { return ButtonPrefix(__instance, ref __result, 0, BackButton); }
        private static bool StartPrefix(object __instance, ref bool __result) { return ButtonPrefix(__instance, ref __result, 0, StartButton); }

        private static bool ButtonPrefix(object player, ref bool result, int maskKind, byte button)
        {
            InputFrame frame;
            if (active == null || !active.TryGetInput(player, out frame)) return true;
            byte buttons = maskKind == 0 ? frame.HeldButtons : (maskKind == 1 ? frame.DownButtons : frame.UpButtons);
            result = HasButton(buttons, button);
            return false;
        }
    }
}
