using System;
using HarmonyLib;

namespace CustomKillSoundFramework
{
    /// <summary>
    /// Soldier feedback ONLY.
    ///   batch opener -> DamageFeedback.OnSoldierHit (local player -> enemy soldier)
    ///   upgrades     -> Soldier.Kill / KillSynched, GameStatistics.OnPlayerGetKill
    ///   head hits    -> Soldier.DamageWithAnimation, BodyPart.HitPart,
    ///                   ItemHelmet.HitPart (unit-attributed),
    ///                   GameStatistics.OnHeadshotHelmet (weak, no unit)
    ///   cfg reload   -> BattleManager.Start (once per battle load)
    ///
    /// Only OnSoldierHit can OPEN a batch, and it requires both
    /// "shooter is the local player" and "target is an enemy". Every other
    /// hook is attribution-only: head signals are accepted solely when their
    /// unit pointer belongs to the current batch's targets, so enemy->player,
    /// ally->enemy and enemy->ally combat can never produce a sound.
    /// </summary>
    internal static class Hooks
    {
        private static int _warnings;

        internal static bool IsLocalPlayer(Soldier shooter)
        {
            if (shooter == null)
                return false;
            try
            {
                if (PlayerController.CharacterIsPlayer(shooter))
                    return true;
            }
            catch (Exception e) { Warn("CharacterIsPlayer", e); }
            try
            {
                if (shooter.IsFPSPlayer())
                    return true;
            }
            catch (Exception e) { Warn("IsFPSPlayer", e); }
            return false;
        }

        /// <summary>The faction string of the soldier the local player controls.</summary>
        internal static string LocalFaction()
        {
            try
            {
                var s = Soldier.CurrentControlledSoldierOrNull();
                return s == null ? null : s.faction;
            }
            catch (Exception e) { Warn("LocalFaction", e); return null; }
        }

        /// <summary>
        /// True only when the target is on the opposite side from the shooter.
        /// If factions cannot be read, we still refuse a hit on the player's own unit.
        /// </summary>
        internal static bool IsEnemy(Soldier shooter, Soldier target)
        {
            if (shooter == null || target == null)
                return false;
            string a = null, b = null;
            try { a = shooter.faction; b = target.faction; }
            catch (Exception e) { Warn("IsEnemy", e); }

            if (Diag.Enabled)
                Diag.Signal("factions shooter='" + a + "' target='" + b + "'");

            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return !IsLocalPlayerPtr(Ptr(target));
            return !string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True for a soldier that is not the unit the local player controls.</summary>
        internal static bool IsEnemyUnit(Soldier unit)
        {
            if (unit == null)
                return false;
            long ptr = Ptr(unit);
            if (ptr == 0L || IsLocalPlayerPtr(ptr))
                return false;
            try
            {
                string mine = LocalFaction();
                string theirs = unit.faction;
                // Faction data can be unavailable while the player is inside a
                // vehicle. Fall back to "not the controlled soldier": this death
                // alone never plays a sound, it must still pair with the player
                // kill statistic, so a friendly death cannot leak through.
                if (string.IsNullOrEmpty(mine) || string.IsNullOrEmpty(theirs))
                    return true;
                return !string.Equals(mine, theirs, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception e) { Warn("IsEnemyUnit", e); return false; }
        }

        internal static bool IsDead(Soldier target)
        {
            if (target == null)
                return false;
            try { return target.IsDead; }
            catch (Exception e) { Warn("IsDead", e); return false; }
        }

        internal static long Ptr(Soldier s)
        {
            try { return s == null ? 0L : s.Pointer.ToInt64(); }
            catch { return 0L; }
        }

        internal static long Ptr(Creature c)
        {
            try { return c == null ? 0L : c.Pointer.ToInt64(); }
            catch { return 0L; }
        }

        /// <summary>True when the pointer belongs to the soldier the local player controls.</summary>
        internal static bool IsLocalPlayerPtr(long ptr)
        {
            if (ptr == 0L)
                return false;
            try
            {
                var controlled = Soldier.CurrentControlledSoldierOrNull();
                return controlled != null && controlled.Pointer.ToInt64() == ptr;
            }
            catch (Exception e) { Warn("IsLocalPlayerPtr", e); return false; }
        }

        internal static void Warn(string tag, Exception e)
        {
            if (_warnings++ > 10)
                return;
            CksfPlugin.Logger?.LogWarning("[CKSF] " + tag + ": " + e.Message);
        }
    }

    /// <summary>
    /// THE gate. Only real soldier hits, by the local player, against an enemy.
    /// All other hooks can only modify a batch opened here.
    /// </summary>
    [HarmonyPatch(typeof(DamageFeedback), "OnSoldierHit")]
    internal static class Patch_OnSoldierHit
    {
        [HarmonyPostfix]
        private static void Postfix(Soldier shooter, Soldier target)
        {
            try
            {
                bool local = Hooks.IsLocalPlayer(shooter);
                if (!local)
                {
                    // This is the #1 reason a sound does not play, so make it visible.
                    if (Diag.Enabled)
                        Diag.Signal("IGNORED hit: shooter is not the local player (AI vs AI / enemy fire)");
                    return;
                }

                bool enemy = Hooks.IsEnemy(shooter, target);
                bool dead = Hooks.IsDead(target);
                long ptr = Hooks.Ptr(target);
                if (!enemy)
                {
                    if (Diag.Enabled)
                        Diag.Signal("IGNORED hit: target is not an enemy (friendly fire / own unit)");
                    return;
                }

                SoundEvents.OnLocalSoldierHit(ptr, dead);
            }
            catch (Exception e) { Hooks.Warn("OnSoldierHit", e); }
        }
    }

    /// <summary>
    /// Confirmed soldier death. Records the unit pointer as kill evidence.
    /// It can never create a sound by itself; SoundEvents matches the pointer
    /// against the current batch's targets.
    /// </summary>
    internal static class KillHooks
    {
        internal static void OnKill(Soldier unit)
        {
            try
            {
                if (!Hooks.IsEnemyUnit(unit))
                    return;
                long ptr = Hooks.Ptr(unit);
                if (ptr == 0L)
                    return;
                SoundEvents.OnEnemyDeath(ptr);
            }
            catch (Exception e) { Hooks.Warn("OnKill", e); }
        }
    }

    [HarmonyPatch(typeof(Soldier), "Kill")]
    internal static class Patch_SoldierKill
    {
        [HarmonyPostfix]
        private static void Postfix(Soldier __instance) { KillHooks.OnKill(__instance); }
    }

    [HarmonyPatch(typeof(Soldier), "KillSynched")]
    internal static class Patch_SoldierKillSynched
    {
        [HarmonyPostfix]
        private static void Postfix(Soldier __instance) { KillHooks.OnKill(__instance); }
    }

    /// <summary>Body-part damage, unit-attributed. Pointer matching happens at resolve.</summary>
    [HarmonyPatch(typeof(Soldier), "DamageWithAnimation")]
    internal static class Patch_DamageWithAnimation
    {
        [HarmonyPrefix]
        private static void Prefix(Soldier __instance, BodyPartType bPart)
        {
            try
            {
                if (bPart != BodyPartType.head)
                    return;
                long ptr = Hooks.Ptr(__instance);
                if (ptr == 0L || Hooks.IsLocalPlayerPtr(ptr))
                    return;
                if (Diag.Enabled)
                    Diag.Log("HEAD source=Soldier.DamageWithAnimation ptr=" + ptr);
                SoundEvents.OnHeadHit(ptr);
            }
            catch (Exception e) { Hooks.Warn("DamageWithAnimation", e); }
        }
    }

    /// <summary>Bare head hit, attributed through BodyPart.unit.</summary>
    [HarmonyPatch(typeof(BodyPart), "HitPart")]
    internal static class Patch_HitPart
    {
        [HarmonyPrefix]
        private static void Prefix(BodyPart __instance, string fromFaction)
        {
            try
            {
                if (__instance == null || __instance.bodyPart != BodyPartType.head)
                    return;
                long ptr = Hooks.Ptr(__instance.unit);
                if (ptr == 0L || Hooks.IsLocalPlayerPtr(ptr))
                    return;
                if (Diag.Enabled)
                    Diag.Log("HEAD source=BodyPart.HitPart ptr=" + ptr + " fromFaction=" + fromFaction);
                SoundEvents.OnHeadHit(ptr);
            }
            catch (Exception e) { Hooks.Warn("HitPart", e); }
        }
    }

    /// <summary>
    /// Helmet hits use their own damage path. ItemHelmet.User gives us the
    /// wearer, so this is now an attributed signal instead of a weak one.
    /// </summary>
    [HarmonyPatch(typeof(ItemHelmet), "HitPart")]
    internal static class Patch_ItemHelmetHitPart
    {
        [HarmonyPrefix]
        private static void Prefix(ItemHelmet __instance, string fromFaction)
        {
            try
            {
                if (__instance == null)
                    return;
                long ptr = Hooks.Ptr(__instance.User);
                if (ptr == 0L || Hooks.IsLocalPlayerPtr(ptr))
                    return;
                if (Diag.Enabled)
                    Diag.Log("HEAD source=ItemHelmet.HitPart ptr=" + ptr + " fromFaction=" + fromFaction);
                SoundEvents.OnHeadHit(ptr);
            }
            catch (Exception e) { Hooks.Warn("ItemHelmetHitPart", e); }
        }
    }

    /// <summary>
    /// Helmet headshot statistic: no unit reference. Kept as a very short weak
    /// signal to cover helmeted one-shot headshots that skip the attributed
    /// paths; it can never trigger a sound on its own.
    /// </summary>
    [HarmonyPatch(typeof(GameStatistics), "OnHeadshotHelmet")]
    internal static class Patch_OnHeadshotHelmet
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            if (Diag.Enabled)
                Diag.Log("HEAD source=GameStatistics.OnHeadshotHelmet (no unit pointer)");
            SoundEvents.OnWeakHeadHit();
        }
    }

    /// <summary>
    /// Player kill statistic. Upgrades an open batch, or pairs with a confirmed
    /// enemy soldier death for crew killed inside a destroyed vehicle.
    /// </summary>
    [HarmonyPatch(typeof(GameStatistics), "OnPlayerGetKill")]
    internal static class Patch_OnPlayerGetKill
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            SoundEvents.OnKillSignal();
        }
    }

    /// <summary>Read the cfg once when a battle loads; reset transient state.</summary>
    [HarmonyPatch(typeof(BattleManager), "Start")]
    internal static class Patch_BattleManagerStart
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            try
            {
                SoundEvents.Reset(); // clears per-battle state and the debug line counter
                CksfPlugin.Instance?.ReloadConfigFile(); // re-reads cfg incl. debug switch
                if (Diag.Enabled)
                    Diag.Log("Legend: HIT/IGNORED = who was hit | HEAD/DEATH/KILL = evidence | " +
                             "DECISION = sound chosen | PLAY = actual playback. " +
                             "One volley = one sound (headshot > kill > hit). " +
                             "Repeated signals are capped; max " + Diag.MaxLinesPerBattle +
                             " lines per battle.");
            }
            catch (Exception e) { Hooks.Warn("BattleStart", e); }
        }
    }

    [HarmonyPatch(typeof(PlayerController), "Update")]
    internal static class Patch_PlayerControllerUpdate
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            SoundEvents.Tick();
        }
    }
}
