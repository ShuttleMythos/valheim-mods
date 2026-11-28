using System;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace Knochenbrecher
{
    /// <summary>
    /// Testmodus: legt dem eigenen Spieler beim Betreten der Welt einen Knochenbrecher ins Inventar,
    /// falls er noch keinen hat. So lässt sich der Hammer ohne Spielfortschritt ausprobieren.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class TestModePatch
    {
        private static bool _broken;

        // Wie beim Treffer-Patch: Fehler bleiben hier drin und stören das Spiel nicht.
        private static void Postfix(Player __instance)
        {
            if (_broken)
            {
                return;
            }

            try
            {
                if (GiveHammer(__instance))
                {
                    try
                    {
                        Announce(__instance);
                    }
                    catch (Exception e)
                    {
                        Jotunn.Logger.LogDebug($"Testmodus-Meldung nicht angezeigt: {e.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                _broken = true;
                Jotunn.Logger.LogWarning($"Testmodus abgeschaltet (passt nicht zu dieser Valheim-Version): {e}");
            }
        }

        /// <returns>true, wenn ein Hammer ins Inventar gelegt wurde.</returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool GiveHammer(Player player)
        {
            if (!KnochenbrecherPlugin.TestMode.Value || player == null || player != Player.m_localPlayer)
            {
                return false;
            }

            var inventory = player.GetInventory();
            foreach (var item in inventory.GetAllItems())
            {
                if (item?.m_shared != null && item.m_shared.m_name == KnochenbrecherPlugin.ItemToken)
                {
                    return false;
                }
            }

            var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(KnochenbrecherPlugin.PrefabName) : null;
            if (prefab == null)
            {
                Jotunn.Logger.LogWarning("Testmodus: Knochenbrecher nicht gefunden, es wurde nichts ins Inventar gelegt.");
                return false;
            }

            if (!inventory.AddItem(prefab, 1))
            {
                Jotunn.Logger.LogWarning("Testmodus: Inventar ist voll, der Knochenbrecher passt nicht hinein.");
                return false;
            }

            Jotunn.Logger.LogInfo("Testmodus: Knochenbrecher ins Inventar gelegt.");
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Announce(Player player)
        {
            player.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$knochenbrecher_testmode"));
        }
    }
}
