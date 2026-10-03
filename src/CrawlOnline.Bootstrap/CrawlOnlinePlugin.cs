using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using UnityEngine;

namespace CrawlOnline.Bootstrap
{
#if CRAWLONLINE_DEV_SIMULATION
    internal sealed class SimulationEnabledBuildMarker { }
#endif

    // BepInEx 5 parses this field as System.Version and rejects prerelease
    // suffixes. Keep its loader identity numeric while advertising the full
    // release/build string to Crawl Online peers and diagnostics.
    [BepInPlugin(Id, Name, LoaderVersion)]
    public sealed class CrawlOnlinePlugin : BaseUnityPlugin
    {
        public const string Id = "dev.teagar.crawl-online";
        public const string Name = "Crawl Online";
        public const string LoaderVersion = "0.2.0";
        public const string Version = "0.2.0-alpha.1";
        private const string SupportedUnityVersion = "5.4.2f2";
        private const string SupportedLinuxGameAssemblySha256 = "d6f169535cf2123568359550d75fe1a9924948e04d8d0beb2eed7eb187542f84";
        private const string SupportedWindowsGameAssemblySha256 = "e93e8fb49fd3c3ebe622d0f9f9557c1e4dd475c2a277be19e2c05cbb1f05f61e";
        private object runtime;
        private MethodInfo tick;
        private MethodInfo shutdown;
        private MethodInfo fixedTick;
        private MethodInfo lateTick;
        private MethodInfo drawHud;
        private bool waitingLogged;
        private string gameBuildFingerprint;
#if CRAWLONLINE_DEV_SIMULATION
        private bool localSimulationEnabled;
#endif

        private void Awake()
        {
            Logger.LogInfo(Name + " " + Version + " bootstrap loading");
            if (!string.Equals(Application.unityVersion, SupportedUnityVersion, StringComparison.Ordinal))
            {
                Logger.LogError("Unsupported Unity version. Expected " + SupportedUnityVersion + ".");
                enabled = false;
                return;
            }

            string gameAssembly = Path.Combine(Application.dataPath, "Managed/Assembly-CSharp.dll");
            string gameAssemblyHash;
            try
            {
                gameAssemblyHash = CalculateSha256(gameAssembly);
            }
            catch (Exception exception)
            {
                Logger.LogError("Could not fingerprint the Crawl game assembly: " + exception.Message);
                enabled = false;
                return;
            }

            if (!IsSupportedGameAssembly(gameAssemblyHash))
            {
                Logger.LogError("Unsupported Crawl build. Assembly-CSharp SHA-256=" + gameAssemblyHash);
                enabled = false;
                return;
            }

            gameBuildFingerprint = gameAssemblyHash.ToLowerInvariant();
            Logger.LogInfo("Compatibility fingerprint accepted");
#if CRAWLONLINE_DEV_SIMULATION
            localSimulationEnabled = Config.Bind("Development", "EnableLocalSimulation", false,
                "Enables the isolated local multiplayer simulation. Never creates or joins Steam lobbies.").Value;
            if (localSimulationEnabled)
                Logger.LogWarning("Development SIMULATION configuration accepted by a simulation-enabled build.");
#endif
        }

        private void Update()
        {
            if (runtime == null)
            {
                if (!IsSteamReady())
                {
                    if (!waitingLogged)
                    {
                        Logger.LogInfo("Waiting for Crawl assemblies and Steamworks...");
                        waitingLogged = true;
                    }

                    return;
                }

                LoadRuntime();
                if (runtime == null)
                {
                    enabled = false;
                    return;
                }
            }

            tick.Invoke(runtime, null);
        }

        private void OnDestroy()
        {
            if (runtime != null && shutdown != null)
            {
                shutdown.Invoke(runtime, null);
            }
        }

        private void FixedUpdate()
        {
            if (runtime != null && fixedTick != null)
            {
                fixedTick.Invoke(runtime, null);
            }
        }

        private void LateUpdate()
        {
            if (runtime != null && lateTick != null)
            {
                lateTick.Invoke(runtime, null);
            }
        }

        private void OnGUI()
        {
            if (runtime != null && drawHud != null)
            {
                drawHud.Invoke(runtime, null);
            }
        }

        private void LoadRuntime()
        {
            try
            {
                string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                Assembly assembly = Assembly.LoadFrom(Path.Combine(directory, "CrawlOnline.Runtime.dll"));
                Type type = assembly.GetType("CrawlOnline.CrawlOnlineRuntime", true);
#if CRAWLONLINE_DEV_SIMULATION
                runtime = Activator.CreateInstance(type,
                    new object[] { Logger, gameBuildFingerprint, localSimulationEnabled });
#else
                runtime = Activator.CreateInstance(type, new object[] { Logger, gameBuildFingerprint });
#endif
                tick = type.GetMethod("Tick", BindingFlags.Public | BindingFlags.Instance);
                shutdown = type.GetMethod("Shutdown", BindingFlags.Public | BindingFlags.Instance);
                fixedTick = type.GetMethod("FixedTick", BindingFlags.Public | BindingFlags.Instance);
                lateTick = type.GetMethod("LateTick", BindingFlags.Public | BindingFlags.Instance);
                drawHud = type.GetMethod("DrawHud", BindingFlags.Public | BindingFlags.Instance);
                Logger.LogInfo("Runtime loaded after Crawl initialization");
            }
            catch (Exception exception)
            {
                Logger.LogError("Runtime load failed: " + exception);
                runtime = null;
            }
        }

        private static bool IsSteamReady()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                if (assemblies[i].GetName().Name != "Assembly-CSharp")
                {
                    continue;
                }

                Type systemSteam = assemblies[i].GetType("SystemSteam", false);
                PropertyInfo initialized = systemSteam == null
                    ? null
                    : systemSteam.GetProperty("Initialized", BindingFlags.Public | BindingFlags.Static);
                if (initialized == null)
                {
                    return false;
                }

                try
                {
                    return (bool)initialized.GetValue(null, null);
                }
                catch (TargetInvocationException)
                {
                    return false;
                }
            }

            return false;
        }

        private static string CalculateSha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] digest = algorithm.ComputeHash(stream);
                StringBuilder hex = new StringBuilder(digest.Length * 2);
                for (int i = 0; i < digest.Length; i++)
                {
                    hex.Append(digest[i].ToString("x2"));
                }

                return hex.ToString();
            }
        }

        private static bool IsSupportedGameAssembly(string hash)
        {
            return string.Equals(hash, SupportedLinuxGameAssemblySha256, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(hash, SupportedWindowsGameAssemblySha256, StringComparison.OrdinalIgnoreCase);
        }
    }
}
