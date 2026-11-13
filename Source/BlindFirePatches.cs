using System.Collections.Generic;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace CEOverpenetration;

[HarmonyPatch(typeof(JobGiver_AIFightEnemy), "TryGiveJob")]
internal static class Patch_JobGiver_AIFightEnemy_TryGiveJob
{
    private const int MemoryTicks = 600;
    private const int CooldownTicks = 2500;
    private const float MinShareLeft = 0.3f;
    private const int MaxTracked = 2000;

    private static readonly Dictionary<int, int> RolledAt = new Dictionary<int, int>();
    private static readonly Dictionary<int, int> CooldownUntil = new Dictionary<int, int>();
    private static readonly Dictionary<int, Pawn> BlindTargets = new Dictionary<int, Pawn>();

    internal static float? BlindFireDepth(Pawn shooter, Thing obstacle)
    {
        if (shooter == null || shooter.CurJob == null || shooter.CurJob.def != JobDefOf.AttackStatic || shooter.CurJob.targetA.Thing != obstacle
            || !BlindTargets.TryGetValue(shooter.thingIDNumber, out var target) || !target.Spawned || target.Map != shooter.Map)
        {
            return null;
        }
        return UnityEngine.Mathf.Max(1f, obstacle.Position.DistanceTo(target.Position));
    }

    private static void Prefix(Pawn pawn, out Pawn __state)
    {
        __state = pawn?.mindState?.enemyTarget as Pawn;
    }

    private static void Postfix(Pawn pawn, Pawn __state, ref Job __result)
    {
        var settings = OverpenetrationMod.Settings;
        if (__state == null || settings == null || !settings.enabled || !settings.walls || settings.blindFireChance <= 0f)
        {
            return;
        }
        if (__result != null && __result.def != JobDefOf.Goto && __result.def != JobDefOf.Wait_Combat)
        {
            return;
        }
        var mind = pawn.mindState;
        var now = Find.TickManager.TicksGame;
        if (now - mind.lastEngageTargetTick > MemoryTicks || !ShooterAllowed(pawn) || !TargetAllowed(pawn, __state))
        {
            return;
        }
        var current = mind.enemyTarget;
        if (current != null && current != __state)
        {
            return;
        }
        if (CooldownUntil.TryGetValue(pawn.thingIDNumber, out var until) && now < until)
        {
            return;
        }
        if (RolledAt.TryGetValue(pawn.thingIDNumber, out var rolledTick) && rolledTick == mind.lastEngageTargetTick)
        {
            return;
        }
        if (!(pawn.TryGetAttackVerb(__state, !pawn.IsColonist) is Verb_LaunchProjectileCE verb) || verb.verbProps.IsMeleeAttack
            || !HasLoadedAmmo(verb) || verb.CanHitTarget(__state))
        {
            return;
        }
        var obstacle = FindObstacle(pawn, __state, verb, settings);
        if (obstacle == null)
        {
            return;
        }
        if (RolledAt.Count > MaxTracked)
        {
            RolledAt.Clear();
            BlindTargets.Clear();
            CooldownUntil.RemoveAll(entry => entry.Value <= now);
        }
        RolledAt[pawn.thingIDNumber] = mind.lastEngageTargetTick;
        if (!Rand.Chance(settings.blindFireChance))
        {
            return;
        }
        CooldownUntil[pawn.thingIDNumber] = now + CooldownTicks;
        BlindTargets[pawn.thingIDNumber] = __state;
        var job = JobMaker.MakeJob(JobDefOf.AttackStatic, obstacle);
        job.maxNumStaticAttacks = 1;
        job.endIfCantShootTargetFromCurPos = true;
        job.expiryInterval = 600;
        __result = job;
        if (settings.debugLog)
        {
            Log.Message($"[Overpenetration] {pawn.LabelShort} fires blind at {obstacle.LabelShort} toward {__state.LabelShort}");
        }
    }

    private static bool HasLoadedAmmo(Verb_LaunchProjectileCE verb)
    {
        var ammo = verb.CompAmmo;
        return ammo == null || !ammo.HasMagazine || ammo.CurMagCount > 0;
    }

    private static bool ShooterAllowed(Pawn pawn)
    {
        if (pawn.Faction == Faction.OfPlayer || pawn.IsColonist || pawn.Downed || pawn.InMentalState || pawn.Map == null)
        {
            return false;
        }
        var suppressable = pawn.TryGetComp<CompSuppressable>();
        return suppressable == null || !suppressable.isSuppressed;
    }

    private static bool TargetAllowed(Pawn pawn, Pawn target)
    {
        return target.Spawned && target.Map == pawn.Map && !target.Dead && !target.Downed && target.HostileTo(pawn)
            && !target.IsPsychologicallyInvisible();
    }

    private static Thing FindObstacle(Pawn pawn, Pawn target, Verb_LaunchProjectileCE verb, OverpenetrationSettings settings)
    {
        var map = pawn.Map;
        if (pawn.Position.DistanceTo(target.Position) > verb.EffectiveRange)
        {
            return null;
        }
        var projectile = verb.Projectile;
        if (projectile == null || projectile.thingClass != typeof(BulletCE) || !(projectile.projectile is ProjectilePropertiesCE props)
            || props.flyOverhead || props.explosionRadius > 0f || props.damageDef != DamageDefOf.Bullet)
        {
            return null;
        }
        var penetration = props.armorPenetrationSharp;
        var material = 0f;
        var count = 0;
        Thing first = null;
        foreach (var cell in GenSight.PointsOnLineOfSight(pawn.Position, target.Position))
        {
            if (cell == pawn.Position || cell == target.Position)
            {
                continue;
            }
            if (!cell.InBounds(map))
            {
                return null;
            }
            var things = cell.GetThingList(map);
            for (var i = 0; i < things.Count; i++)
            {
                if (things[i] is Pawn other && other != target && !other.HostileTo(pawn))
                {
                    return null;
                }
            }
            if (cell.CanBeSeenOverFast(map))
            {
                continue;
            }
            var edifice = cell.GetEdifice(map);
            if (edifice == null || !ObstacleMaterial.IsObstacle(edifice) || !(edifice is Building_Door || ObstacleMaterial.IsThin(edifice)))
            {
                return null;
            }
            var hitPoints = edifice.def.useHitPoints && edifice.MaxHitPoints > 0 ? (float)edifice.HitPoints / edifice.MaxHitPoints : 1f;
            var value = ObstacleMaterial.Value(edifice, hitPoints, settings);
            if (value < 0f || ++count > settings.maxPasses)
            {
                return null;
            }
            material += value;
            first ??= edifice;
        }
        if (first == null || penetration - material < settings.minResidual || penetration - material < penetration * MinShareLeft)
        {
            return null;
        }
        return verb.CanHitTarget(first) ? first : null;
    }
}
