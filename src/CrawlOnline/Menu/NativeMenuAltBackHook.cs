using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace CrawlOnline.Menu
{
    // Patches only the reflected controller overload confirmed by runtime observation.
    // The postfix never changes the game's result or menu while it is dispatching input.
    internal sealed class NativeMenuAltBackHook : IDisposable
    {
        private const string HarmonyId = "com.teagar.crawlonline.native-menu-alt-back";
        private static NativeMenuAltBackHook active;
        private readonly ManualLogSource log;
        private readonly NativeMainMenuIntegration nativeMenu;
        private readonly NativeMenuBackLatch pendingNativeBack = new NativeMenuBackLatch();
        private readonly Harmony harmony;
        private readonly object altInput;
        private bool disposed;

        private NativeMenuAltBackHook(ManualLogSource logSource, NativeMainMenuIntegration nativeMenuIntegration)
        {
            log = logSource;
            nativeMenu = nativeMenuIntegration;
            if (nativeMenu == null) throw new ArgumentNullException("nativeMenuIntegration");

            Type menuType = AccessTools.TypeByName("MenuTextMenu");
            Type inputType = AccessTools.TypeByName("eInput");
            Type controllerType = AccessTools.TypeByName("eController");
            if (menuType == null || inputType == null || controllerType == null || !inputType.IsEnum)
                throw new InvalidOperationException("MenuTextMenu input contract is unavailable");

            altInput = Enum.Parse(inputType, "Alt");
            MethodInfo target = menuType.GetMethod("MenuInputDown",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                new[] { inputType, controllerType.MakeByRefType() }, null);
            if (target == null || target.ReturnType != typeof(bool))
                throw new MissingMethodException(menuType.FullName, "MenuInputDown(eInput,eController&)");
            ParameterInfo[] parameters = target.GetParameters();
            if (parameters.Length != 2 || !parameters[1].ParameterType.IsByRef)
                throw new InvalidOperationException("MenuInputDown controller parameter is not ByRef");

            harmony = new Harmony(HarmonyId);
            try
            {
                harmony.Patch(target, null,
                    new HarmonyMethod(typeof(NativeMenuAltBackHook), "MenuInputDownPostfix"));
                active = this;
                log.LogInfo("Native controller BACK hook installed.");
            }
            catch
            {
                harmony.UnpatchAll(HarmonyId);
                throw;
            }
        }

        public static NativeMenuAltBackHook TryCreate(ManualLogSource log,
            NativeMainMenuIntegration nativeMenu)
        {
            try
            {
                return new NativeMenuAltBackHook(log, nativeMenu);
            }
            catch (Exception exception)
            {
                log.LogWarning("Native controller BACK hook disabled safely: " +
                    exception.GetType().Name + ".");
                return null;
            }
        }

        // Harmony binds these special names to the original return value, instance,
        // and first parameter without requiring build-time Crawl type references.
        private static void MenuInputDownPostfix(ref bool __result, object __instance, object __0)
        {
            NativeMenuAltBackHook hook = active;
            if (hook == null || hook.disposed) return;
            try
            {
                bool isAltInput = hook.altInput.Equals(__0);
                bool isInstalledMenu = hook.nativeMenu.IsInstalledMenu(__instance);
                hook.pendingNativeBack.TryLatch(__result, isAltInput, isInstalledMenu,
                    hook.nativeMenu.IsSubmenuOpen);
            }
            catch
            {
                // Input dispatch must remain untouched if any reflected contract changes.
            }
        }

        // Called from the runtime Tick, outside the native menu's input dispatch.
        public void Tick()
        {
            if (disposed || !pendingNativeBack.TryConsume()) return;
            nativeMenu.CloseSubmenuForNativeBack();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (active == this) active = null;
            harmony.UnpatchAll(HarmonyId);
        }
    }
}
