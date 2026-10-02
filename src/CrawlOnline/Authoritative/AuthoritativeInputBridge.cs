using System;
using System.Collections.Generic;
using CrawlOnline.Determinism;
using CrawlOnline.Protocol;
using HarmonyLib;
using UnityEngine;

namespace CrawlOnline.Authoritative
{
    internal sealed class AuthoritativeInputBridge : IDisposable
    {
        private const string HarmonyId = "dev.teagar.crawl-online.authoritative-input";
        private static AuthoritativeInputBridge active;
        private readonly Dictionary<int, InputFrameBuffer> inputs = new Dictionary<int, InputFrameBuffer>();
        private readonly Harmony harmony = new Harmony(HarmonyId);

        public AuthoritativeInputBridge()
        {
            active = this;
            Patch("GetInputMove", "MovePrefix");
            Patch("GetInputDownAction", "DownActionPrefix");
            Patch("GetInputDownBack", "DownBackPrefix");
            Patch("GetInputDownStart", "DownStartPrefix");
            Patch("GetInputUpAction", "UpActionPrefix");
            Patch("GetInputUpBack", "UpBackPrefix");
            Patch("GetInputUpStart", "UpStartPrefix");
            Patch("GetInputAction", "ActionPrefix");
            Patch("GetInputBack", "BackPrefix");
            Patch("GetInputStart", "StartPrefix");
        }

        public void Set(SessionInputFrame input)
        {
            InputFrameBuffer buffered;
            if (!inputs.TryGetValue(input.Input.PlayerId, out buffered))
            {
                buffered = new InputFrameBuffer();
                inputs.Add(input.Input.PlayerId, buffered);
            }
            buffered.Set(input.Input, Time.frameCount);
        }

        public void Clear()
        {
            inputs.Clear();
        }

        public void Dispose()
        {
            if (active == this) active = null;
            harmony.UnpatchAll(HarmonyId);
            inputs.Clear();
        }

        private void Patch(string method, string prefix)
        {
            harmony.Patch(AccessTools.Method(GameApi.Type("PlayerData"), method),
                new HarmonyMethod(typeof(AuthoritativeInputBridge), prefix), null);
        }

        private bool TryGet(object player, out InputFrameBuffer input)
        {
            int slot = GameApi.Property<int>(player, "Id");
            return inputs.TryGetValue(slot, out input);
        }

        private static bool MovePrefix(object __instance, ref Vector2 __result)
        {
            InputFrameBuffer input;
            if (active == null || !active.TryGet(__instance, out input)) return true;
            __result = new Vector2(Expand(input.Latest.MoveX), Expand(input.Latest.MoveY));
            return false;
        }

        private static bool DownActionPrefix(object __instance, ref bool __result) { return ButtonPrefix(__instance, ref __result, 1, 1); }
        private static bool DownBackPrefix(object __instance, ref bool __result) { return ButtonPrefix(__instance, ref __result, 1, 2); }
        private static bool DownStartPrefix(object __instance, ref bool __result) { return ButtonPrefix(__instance, ref __result, 1, 4); }
        private static bool UpActionPrefix(object __instance, ref bool __result) { return ButtonPrefix(__instance, ref __result, 2, 1); }
        private static bool UpBackPrefix(object __instance, ref bool __result) { return ButtonPrefix(__instance, ref __result, 2, 2); }
        private static bool UpStartPrefix(object __instance, ref bool __result) { return ButtonPrefix(__instance, ref __result, 2, 4); }
        private static bool ActionPrefix(object __instance, ref bool __result) { return ButtonPrefix(__instance, ref __result, 0, 1); }
        private static bool BackPrefix(object __instance, ref bool __result) { return ButtonPrefix(__instance, ref __result, 0, 2); }
        private static bool StartPrefix(object __instance, ref bool __result) { return ButtonPrefix(__instance, ref __result, 0, 4); }

        private static bool ButtonPrefix(object player, ref bool result, int kind, byte button)
        {
            InputFrameBuffer input;
            if (active == null || !active.TryGet(player, out input)) return true;
            byte buttons = kind == 0 ? input.Latest.HeldButtons :
                (kind == 1 ? input.GetDown(Time.frameCount) : input.GetUp(Time.frameCount));
            result = (buttons & button) != 0;
            return false;
        }

        private static float Expand(short value)
        {
            return value / (float)short.MaxValue;
        }
    }
}
