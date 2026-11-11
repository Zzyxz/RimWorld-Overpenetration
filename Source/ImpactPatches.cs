using System;
using System.Collections.Generic;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace CEOverpenetration;

internal sealed class ImpactContext
{
    public BulletCE projectile;
    public Thing target;
    public Pawn pawn;
    public Thing obstacle;
    public float hitPointsFraction;
    public Map map;
    public Bounds targetBounds;
    public Vector3 position;
    public Vector3 lastPos;
    public float speed;
    public float damage;
    public int passes;
    public BodyPartRecord hitPart;
    public float penetration;
    public float entryArmor;
    public float exitArmor;
    public bool blocked;
}

internal sealed class ImpactState
{
    public ImpactContext context;
    public ImpactContext previous;
    public ProjectilePropertiesCE swappedProps;
    public List<SecondaryDamage> secondaryDamage;
    public List<ExtraDamage> extraDamages;
}

[HarmonyPatch(typeof(BulletCE), nameof(BulletCE.Impact))]
internal static class Patch_BulletCE_Impact
{
    internal static ImpactContext Current;

    private static readonly List<SecondaryDamage> NoSecondaryDamage = new List<SecondaryDamage>();
    private static readonly List<ExtraDamage> NoExtraDamages = new List<ExtraDamage>();

    private static void Prefix(BulletCE __instance, Thing hitThing, out ImpactState __state)
    {
        __state = null;
        var followUp = PenetrationTracker.Get(__instance);
        if (followUp != null && __instance.def.projectile is ProjectilePropertiesCE props)
        {
            __state = new ImpactState
            {
                swappedProps = props,
                secondaryDamage = props.secondaryDamage,
                extraDamages = props.extraDamages
            };
            props.secondaryDamage = NoSecondaryDamage;
            props.extraDamages = NoExtraDamages;
        }

        var settings = OverpenetrationMod.Settings;
        if (settings == null || !settings.enabled || hitThing == null || !hitThing.Spawned)
        {
            return;
        }
        var pawn = hitThing as Pawn;
        var obstacle = pawn == null && settings.walls && ObstacleMaterial.IsObstacle(hitThing) ? hitThing : null;
        if (pawn == null && obstacle == null)
        {
            return;
        }
        var passes = followUp?.passes ?? 0;
        if (passes >= settings.maxPasses || !CanPassThrough(__instance, settings))
        {
            return;
        }

        __state ??= new ImpactState();
        __state.previous = Current;
        __state.context = new ImpactContext
        {
            projectile = __instance,
            target = hitThing,
            pawn = pawn,
            obstacle = obstacle,
            hitPointsFraction = obstacle != null && obstacle.def.useHitPoints && obstacle.MaxHitPoints > 0 ? (float)obstacle.HitPoints / obstacle.MaxHitPoints : 1f,
            penetration = obstacle != null ? __instance.PenetrationAmount : 0f,
            map = __instance.Map,
            targetBounds = CE_Utility.GetBoundsFor(hitThing),
            position = __instance.ExactPosition,
            lastPos = __instance.LastPos,
            speed = __instance.shotSpeed,
            damage = __instance.DamageAmount,
            passes = passes
        };
        Current = __state.context;
    }

    private static Exception Finalizer(Exception __exception, BulletCE __instance, ImpactState __state)
    {
        if (__state != null)
        {
            if (__state.swappedProps != null)
            {
                __state.swappedProps.secondaryDamage = __state.secondaryDamage;
                __state.swappedProps.extraDamages = __state.extraDamages;
            }
            if (__state.context != null)
            {
                Current = __state.previous;
                if (__exception == null)
                {
                    try
                    {
                        FollowUpLauncher.TryLaunch(__state.context);
                    }
                    catch (Exception e)
                    {
                        Log.ErrorOnce("[Overpenetration] Could not launch follow-up projectile: " + e, 0x5A4F5001);
                    }
                }
            }
        }
        PenetrationTracker.Remove(__instance);
        return __exception;
    }

    private static bool CanPassThrough(BulletCE projectile, OverpenetrationSettings settings)
    {
        if (projectile.GetType() != typeof(BulletCE) || projectile.launcher == null || projectile.launcher is AmmoThing)
        {
            return false;
        }
        var fragment = projectile.equipment == projectile;
        if (fragment ? !settings.fragments || projectile.DamageDef?.defName != "Fragment" : projectile.DamageDef != DamageDefOf.Bullet)
        {
            return false;
        }
        var props = projectile.def.projectile;
        if (props == null || props.flyOverhead || props.explosionRadius > 0f || projectile.traitExplosion != null
            || props.preExplosionSpawnThingDef != null || props.postExplosionSpawnThingDef != null)
        {
            return false;
        }
        return projectile.def.GetCompProperties<CompProperties_ExplosiveCE>() == null
            && projectile.def.GetCompProperties<CompProperties_Fragments>() == null;
    }
}

[HarmonyPatch(typeof(ArmorUtilityCE), nameof(ArmorUtilityCE.GetAfterArmorDamage))]
internal static class Patch_GetAfterArmorDamage
{
    private static int depth;

    private static void Prefix(DamageInfo originalDinfo, Pawn pawn, BodyPartRecord hitPart, out bool __state)
    {
        depth++;
        var context = Patch_BulletCE_Impact.Current;
        __state = context != null && depth == 1 && context.pawn != null && pawn == context.pawn && originalDinfo.Def == context.projectile.DamageDef;
        if (__state && context.hitPart == null && hitPart != null)
        {
            context.hitPart = hitPart;
            context.penetration = originalDinfo.ArmorPenetrationInt;
            FollowUpLauncher.MeasureArmor(context);
        }
    }

    private static void Postfix(DamageInfo originalDinfo, ref bool armorDeflected, ref bool shieldAbsorbed, DamageInfo __result, bool __state)
    {
        if (!__state)
        {
            return;
        }
        if (armorDeflected || shieldAbsorbed || __result.Def != originalDinfo.Def || __result.Amount <= 0f)
        {
            Patch_BulletCE_Impact.Current.blocked = true;
        }
    }

    private static Exception Finalizer(Exception __exception)
    {
        depth--;
        return __exception;
    }
}

[HarmonyPatch(typeof(ProjectileCE), "CanCollideWith")]
internal static class Patch_ProjectileCE_CanCollideWith
{
    private static bool Prefix(ProjectileCE __instance, Thing thing, ref float dist, ref bool __result)
    {
        var state = PenetrationTracker.Get(__instance);
        if (state == null || state.passedThing == null || thing != state.passedThing)
        {
            return true;
        }
        dist = -1f;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(ProjectileCE), nameof(ProjectileCE.PenetrationAmount), MethodType.Getter)]
internal static class Patch_ProjectileCE_PenetrationAmount
{
    [HarmonyPriority(Priority.First)]
    private static void Postfix(ProjectileCE __instance, ref float __result)
    {
        var state = PenetrationTracker.Get(__instance);
        if (state != null)
        {
            __result = state.penetration * __instance.RemainingSpeedPct;
        }
    }
}
