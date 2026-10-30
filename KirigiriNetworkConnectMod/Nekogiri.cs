using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using Steamworks;

namespace NekogiriMod
{
    [BepInPlugin("kirigiri.repo.nekogiri", "Nekogiri", "1.1.0.8")]
    public class NekogiriMod : BaseUnityPlugin
    {
        private void Awake()
        {
            // Set up plugin logging
            Logger.LogInfo(@"
 ██ ▄█▀ ██▓ ██▀███   ██▓  ▄████  ██▓ ██▀███   ██▓
 ██▄█▒ ▓██▒▓██ ▒ ██▒▓██▒ ██▒ ▀█▒▓██▒▓██ ▒ ██▒▓██▒
▓███▄░ ▒██▒▓██ ░▄█ ▒▒██▒▒██░▄▄▄░▒██▒▓██ ░▄█ ▒▒██▒
▓██ █▄ ░██░▒██▀▀█▄  ░██░░▓█  ██▓░██░▒██▀▀█▄  ░██░
▒██▒ █▄░██░░██▓ ▒██▒░██░░▒▓███▀▒░██░░██▓ ▒██▒░██░
▒ ▒▒ ▓▒░▓  ░ ▒▓ ░▒▓░░▓   ░▒   ▒ ░▓  ░ ▒▓ ░▒▓░░▓  
░ ░▒ ▒░ ▒ ░  ░▒ ░ ▒░ ▒ ░  ░   ░  ▒ ░  ░▒ ░ ▒░ ▒ ░
░ ░░ ░  ▒ ░  ░░   ░  ▒ ░░ ░   ░  ▒ ░  ░░   ░  ▒ ░
░  ░    ░     ░      ░        ░  ░     ░      ░  
                                                 
");
            Logger.LogInfo("Nekogiri has loaded!");

            Instance = this;
            var harmony = new Harmony("kirigiri.repo.nekogiri");

            // v0.5.0 moved Steam initialization into Platforms.Steam.SteamPlatform.
            // Patch Facepunch SteamClient.Init's appId argument instead of relying on
            // the removed SteamManager.Awake hook. This preserves the game's own
            // initialization flow and avoids initializing Steam twice.
            PatchIfPresent(harmony,
                AccessTools.Method(typeof(SteamClient), "Init", new Type[] { typeof(uint), typeof(bool) }),
                nameof(SteamClientInitPrefix), true);

            PatchIfPresent(harmony,
                AccessTools.Method(typeof(DataDirector), "PhotonSetAppId"),
                nameof(PhotonSetAppIdPrefix), true);

            PatchIfPresent(harmony,
                AccessTools.Method(typeof(MenuPageMain), "Start"),
                nameof(MenuPageMainStartPrefix), false);

            // Auth=none: override the game's Steam AuthenticationValues immediately before
            // Photon sends OpAuthenticate. Auth=steam (or any other value) leaves the game
            // authentication flow untouched.
            PatchPhotonAuthenticationMethods(harmony);

            Logger.LogInfo("Nekogiri v1.1.0.8 patches installed for R.E.P.O. v0.5.0.");
            Logger.LogInfo("Made with <3 By Kirigiri \nhttps://discord.gg/zn2a2A65My");
        }

        // The custom method to replace the original Start method
        private void CustomStart()
        {
            Logger.LogInfo("Custom Start method executed!");

            string configFilePath = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Kirigiri.ini");

            // Default App IDs
            string appIdRealtime = "99515094-45b7-4c98-ab70-a448c548c83d";
            string appIdVoice = "dfc40a01-c3a4-4395-9707-b71118816d2b";
            bool useDefaultRepoServers = false;

            if (File.Exists(configFilePath))
            {
                try
                {
                    var settings = File.ReadAllLines(configFilePath)
                                       .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith(";"))
                                       .Select(line => line.Split('='))
                                       .Where(parts => parts.Length == 2)
                                       .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());

                    // Check for UseDefaultRepoServers flag
                    if (settings.TryGetValue("UseDefaultRepoServers", out string useDefaultValue) && useDefaultValue == "1")
                    {
                        useDefaultRepoServers = true;
                    }

                    // If not using defaults, try to get AppIdRealtime and AppIdVoice from INI file
                    if (!useDefaultRepoServers)
                    {
                        if (settings.TryGetValue("AppIdRealtime", out string iniAppIdRealtime) && !string.IsNullOrWhiteSpace(iniAppIdRealtime))
                        {
                            appIdRealtime = iniAppIdRealtime;
                            Debug.Log($"[Photon] AppIdRealtime from INI: {appIdRealtime}");
                        }

                        if (settings.TryGetValue("AppIdVoice", out string iniAppIdVoice) && !string.IsNullOrWhiteSpace(iniAppIdVoice))
                        {
                            appIdVoice = iniAppIdVoice;
                            Debug.Log($"[Photon] AppIdVoice from INI: {appIdVoice}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error reading INI file, using default App IDs. Details: {ex.Message}");
                }
            }
            else
            {
                Logger.LogWarning($"INI file not found at {configFilePath}, using default App IDs.");
                useDefaultRepoServers = true; // Use default if the INI file is missing
            }

            // Apply the final AppId settings based on the condition
            PhotonNetwork.PhotonServerSettings.AppSettings.AppIdRealtime = appIdRealtime;
            PhotonNetwork.PhotonServerSettings.AppSettings.AppIdVoice = appIdVoice;
        }





        // Read the configured Steam App ID. The Harmony prefix below changes the
        // argument passed by the game's own SteamPlatform.Initialise() method.
        private uint ReadSteamAppId()
        {
            string configFilePath = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Kirigiri.ini");
            const uint defaultAppId = 3241660U;
            uint appId = defaultAppId;

            try
            {
                if (!File.Exists(configFilePath))
                {
                    Logger.LogWarning("Kirigiri.ini not found; using the game's default Steam App ID " + defaultAppId + ".");
                    return appId;
                }

                var settings = File.ReadAllLines(configFilePath)
                    .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith(";") && !line.TrimStart().StartsWith("//"))
                    .Select(line => line.Split(new char[] { '=' }, 2))
                    .Where(parts => parts.Length == 2)
                    .GroupBy(parts => parts[0].Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Last()[1].Trim(), StringComparer.OrdinalIgnoreCase);

                // UseDefaultRepoServers controls Photon App IDs only. Keep SteamAppId
                // independent so the separate Steam-app launch feature remains usable.

                if (settings.TryGetValue("SteamAppId", out string configured) && uint.TryParse(configured, out uint parsed) && parsed > 0)
                    appId = parsed;
                else
                    Logger.LogWarning("SteamAppId missing or invalid in Kirigiri.ini; using " + defaultAppId + ".");
            }
            catch (Exception ex)
            {
                Logger.LogError("Could not read SteamAppId: " + ex.Message + ". Using " + defaultAppId + ".");
            }

            Logger.LogInfo("Steam App ID selected by Nekogiri: " + appId);
            return appId;
        }

        private string ReadAuthMode()
        {
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Kirigiri.ini");
            if (!File.Exists(path)) return "steam";
            try
            {
                bool inGameSection = false;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("//")) continue;
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        inGameSection = string.Equals(line, "[Game]", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }
                    if (inGameSection)
                    {
                        int equals = line.IndexOf('=');
                        if (equals > 0 && string.Equals(line.Substring(0, equals).Trim(), "Auth", StringComparison.OrdinalIgnoreCase))
                            return line.Substring(equals + 1).Trim().ToLowerInvariant();
                    }
                }
            }
            catch (Exception ex) { Logger.LogError("Could not read Auth setting: " + ex.Message); }
            return "steam";
        }

        private static void PatchPhotonAuthenticationMethods(Harmony harmony)
        {
            // Photon versions used by R.E.P.O. expose overloads with parameters. Harmony's
            // AccessTools.Method(type, name) only resolves a zero-argument signature, so
            // enumerate the real methods and patch every OpAuthenticate overload.
            string[] names = { "OpAuthenticate", "OpAuthenticateOnce" };
            int patched = 0;
            foreach (MethodInfo method in typeof(LoadBalancingClient).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!names.Contains(method.Name)) continue;
                try
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(NekogiriMod), nameof(PhotonOpAuthenticatePrefix)));
                    Instance.Logger.LogInfo("Patched Photon authentication method: " + method.Name + "(" + string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name).ToArray()) + ")");
                    patched++;
                }
                catch (Exception ex) { Instance.Logger.LogError("Failed to patch " + method.Name + ": " + ex.Message); }
            }
            if (patched == 0)
                Instance.Logger.LogWarning("No Photon OpAuthenticate methods found; Auth=none cannot be applied by this patch.");
        }

        private static void PhotonOpAuthenticatePrefix(object __instance, object[] __args)
        {
            if (Instance == null || Instance.ReadAuthMode() != "none") return;

            // In this Photon build, authentication values are held on the client instance;
            // they are not necessarily passed as method arguments.
            object authValues = null;
            if (__instance != null)
            {
                Type clientType = __instance.GetType();
                PropertyInfo valuesProperty = clientType.GetProperty("AuthValues", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (valuesProperty != null && valuesProperty.CanRead)
                    authValues = valuesProperty.GetValue(__instance, null);
                if (authValues == null)
                {
                    FieldInfo valuesField = clientType.GetField("AuthValues", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (valuesField != null) authValues = valuesField.GetValue(__instance);
                }
            }

            if (authValues == null && __args != null)
                authValues = __args.FirstOrDefault(arg => arg != null && arg.GetType().GetProperty("AuthType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null);
            if (authValues == null) return;

            try
            {
                Type type = authValues.GetType();
                PropertyInfo authType = type.GetProperty("AuthType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (authType == null || !authType.CanWrite || !authType.PropertyType.IsEnum)
                {
                    Instance.Logger.LogWarning("Auth=none requested, but Photon AuthenticationValues.AuthType was not writable.");
                    return;
                }
                object noneValue;
                try { noneValue = Enum.Parse(authType.PropertyType, "None", true); }
                catch { noneValue = Enum.ToObject(authType.PropertyType, 255); }
                authType.SetValue(authValues, noneValue, null);
                ClearAuthPayload(type, authValues, "AuthGetParameters");
                ClearAuthPayload(type, authValues, "AuthPostData");
                ClearAuthPayload(type, authValues, "Token");
                Instance.Logger.LogInfo("Photon authentication overridden: Auth=none (AuthType=255; Steam ticket/custom payload omitted).");
            }
            catch (Exception ex) { Instance.Logger.LogError("Failed to apply Auth=none to Photon: " + ex); }
        }

        private static void ClearAuthPayload(Type type, object target, string memberName)
        {
            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite)
            {
                if (!property.PropertyType.IsValueType || Nullable.GetUnderlyingType(property.PropertyType) != null)
                    property.SetValue(target, null, null);
                else if (property.PropertyType == typeof(string))
                    property.SetValue(target, string.Empty, null);
                return;
            }
            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && !field.IsInitOnly)
            {
                if (!field.FieldType.IsValueType || Nullable.GetUnderlyingType(field.FieldType) != null)
                    field.SetValue(target, null);
                else if (field.FieldType == typeof(string))
                    field.SetValue(target, string.Empty);
            }
        }

        private static NekogiriMod Instance;

        private static void SteamClientInitPrefix(ref uint __0)
        {
            if (Instance == null) return;
            __0 = Instance.ReadSteamAppId();
        }

        private static bool PhotonSetAppIdPrefix()
        {
            if (Instance != null) Instance.CustomStart();
            return false; // The custom Photon App IDs replace the game's defaults.
        }

        private static bool MenuPageMainStartPrefix()
        {
            if (Instance != null) Instance.WelcomeMessage();
            return true; // Keep the game's original menu startup.
        }

        private void PatchIfPresent(Harmony harmony, MethodBase original, string prefixName, bool required)
        {
            if (original == null)
            {
                string message = "Could not find patch target for " + prefixName + ".";
                if (required) Logger.LogError(message); else Logger.LogWarning(message);
                return;
            }

            MethodInfo prefix = AccessTools.Method(typeof(NekogiriMod), prefixName);
            if (prefix == null)
            {
                Logger.LogError("Could not find Nekogiri prefix " + prefixName + ".");
                return;
            }

            harmony.Patch(original, prefix: new HarmonyMethod(prefix));
            Logger.LogInfo("Patched " + original.DeclaringType.FullName + "." + original.Name);
        }


        private void WelcomeMessage()
        {
            string configFilePath = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Kirigiri.ini");

            try
            {
                if (File.Exists(configFilePath))
                {
                    // Read all lines from the INI file while preserving sections and comments
                    var lines = File.ReadAllLines(configFilePath).ToList();
                    bool welcomeReadUpdated = false;

                    for (int i = 0; i < lines.Count; i++)
                    {
                        // Look for the line containing "FirstLaunch"
                        if (lines[i].StartsWith("FirstLaunch"))
                        {
                            // If FirstLaunch is 1, show the message and update to 0
                            if (lines[i].Contains("FirstLaunch=1"))
                            {
                                // Show the welcome message with the correct PagePopUp signature
                                MenuManager.instance.PagePopUp(
                                    "Nekogiri",
                                    UnityEngine.Color.magenta,
                                    "<size=20>This mod has been made by Kirigiri.\nMake sure to create an account on <color=#808080>https://www.photonengine.com/</color> and to fill the values inside the <color=#cc00ff>Kirigiri.ini</color> file !\nThis message will appear only once, Have fun !",
                                    "OK",
                                    true // richText enabled
                                );

                                // Update FirstLaunch to 0
                                lines[i] = "FirstLaunch=0";
                                welcomeReadUpdated = true;
                            }
                            break;
                        }
                    }

                    // If the FirstLaunch value was updated, write back the modified lines
                    if (welcomeReadUpdated)
                    {
                        File.WriteAllLines(configFilePath, lines);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error reading or updating Kirigiri.ini: {ex.Message}");
            }
        }




    }
}
