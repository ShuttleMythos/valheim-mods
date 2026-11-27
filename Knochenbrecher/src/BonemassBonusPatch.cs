using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace Knochenbrecher
{
    /// <summary>
    /// Erhöht den Schaden des Knochenbrechers gegen den Knochenwanst.
    /// Character.Damage läuft beim Angreifer, bevor der Treffer an den Besitzer des Gegners
    /// geschickt wird – der Bonus wirkt dadurch auch im Koop korrekt und genau einmal.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class BonemassBonusPatch
    {
        private const string BonemassPrefab = "Bonemass";
        private static float _lastBonk;
        private static bool _bonusBroken;
        private static bool _bonkBroken;

        // Der Prefix hängt an JEDEM Treffer im Spiel. Er darf deshalb nie einen Fehler nach außen
        // geben – auch nicht, wenn ein Valheim-Update eine benutzte Methode ändert. Die eigentliche
        // Arbeit steckt in eigenen Methoden, damit so ein Fehler hier abgefangen werden kann.
        private static void Prefix(Character __instance, HitData hit)
        {
            if (_bonusBroken)
            {
                return;
            }

            try
            {
                if (ApplyBonus(__instance, hit) && !_bonkBroken)
                {
                    try
                    {
                        ShowBonk();
                    }
                    catch (Exception e)
                    {
                        _bonkBroken = true;
                        Jotunn.Logger.LogWarning($"BONK-Meldung abgeschaltet: {e.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                _bonusBroken = true;
                Jotunn.Logger.LogWarning($"Knochenwanst-Bonus abgeschaltet (passt nicht zu dieser Valheim-Version): {e}");
            }
        }

        /// <returns>true, wenn der lokale Spieler den Knochenwanst mit dem Knochenbrecher getroffen hat.</returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ApplyBonus(Character target, HitData hit)
        {
            if (target == null || hit == null)
            {
                return false;
            }

            var player = Player.m_localPlayer;
            if (player == null || hit.GetAttacker() != player)
            {
                return false;
            }

            if (Utils.GetPrefabName(target.gameObject) != BonemassPrefab)
            {
                return false;
            }

            var weapon = player.GetCurrentWeapon();
            if (weapon?.m_shared == null || weapon.m_shared.m_name != KnochenbrecherPlugin.ItemToken)
            {
                return false;
            }

            var multiplier = KnochenbrecherPlugin.BonemassMultiplier.Value;
            if (multiplier > 1f)
            {
                hit.m_damage.Modify(multiplier);
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ShowBonk()
        {
            if (!KnochenbrecherPlugin.BonkMessage.Value || MessageHud.instance == null || Time.time - _lastBonk < 4f)
            {
                return;
            }

            _lastBonk = Time.time;
            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center,
                Localization.instance.Localize("$knochenbrecher_bonk"));
        }
    }
}
