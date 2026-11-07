using CombatExtended;
using RimWorld;
using UnityEngine;
using Verse;

namespace CEOverpenetration;

internal static class FollowUpLauncher
{
    private const float ExitOffset = 0.05f;

    public static void TryLaunch(ImpactContext context)
    {
        var settings = OverpenetrationMod.Settings;
        var source = context.projectile;
        float residual;
        string numbers;
        if (context.obstacle != null)
        {
            if (context.penetration <= 0f || context.map == null || source.launcher == null)
            {
                DebugLog(context, "stopped: no penetration or no shooter");
                return;
            }
            var material = ObstacleMaterial.Value(context.obstacle, context.hitPointsFraction, settings);
            if (material < 0f)
            {
                DebugLog(context, !settings.debugLog ? null : $"stopped: cannot be penetrated ({ObstacleMaterial.Describe(context.obstacle, context.hitPointsFraction)})");
                return;
            }
            residual = context.penetration - material;
            numbers = !settings.debugLog ? null : $"{ObstacleMaterial.Describe(context.obstacle, context.hitPointsFraction)}, pen {context.penetration:0.00}, material {material:0.00}, rest {residual:0.00}";
        }
        else
        {
            if (context.blocked)
            {
                DebugLog(context, "stopped: deflected, absorbed by a shield or no damage");
                return;
            }
            if (context.hitPart == null || context.penetration <= 0f || context.map == null || source.launcher == null)
            {
                DebugLog(context, "stopped: no armor check (shield belt or no damage), no penetration or no shooter");
                return;
            }
            residual = ResidualPenetration(context, settings, out var density, out var thickness);
            numbers = !settings.debugLog ? null : $"part {context.hitPart.LabelShort}, pen {context.penetration:0.00}, armor in {context.entryArmor:0.00}, armor out {(settings.exitArmor ? context.exitArmor : 0f):0.00}, body {density:0.00}+{thickness:0.00}, rest {residual:0.00}";
        }
        if (residual < settings.minResidual)
        {
            DebugLog(context, !settings.debugLog ? null : $"stopped inside: {numbers}, needed {settings.minResidual:0.0}");
            return;
        }

        var direction = context.position - context.lastPos;
        if (direction.sqrMagnitude < 1e-6f)
        {
            DebugLog(context, !settings.debugLog ? null : $"stopped: no flight direction, {numbers}");
            return;
        }
        direction.Normalize();

        var bounds = context.targetBounds;
        if (context.pawn != null && context.pawn.Spawned)
        {
            bounds.Encapsulate(CE_Utility.GetBoundsFor(context.pawn));
        }
        var exit = ExitPoint(bounds, context.position, direction);
        var cell = exit.ToIntVec3();
        if (exit.y <= 0f || !cell.InBounds(context.map) || exit.y >= CollisionVertical.WallCollisionHeight && cell.Roofed(context.map))
        {
            DebugLog(context, !settings.debugLog ? null : $"stopped: exit below ground, inside a roof or off the map, {numbers}");
            return;
        }

        var damage = context.damage * residual / context.penetration;
        DebugLog(context, !settings.debugLog ? null : $"passed through: {numbers}, exit height {exit.y:0.00}, damage {context.damage:0.0} -> {damage:0.0}, pass {context.passes + 1}/{settings.maxPasses}");
        var shotAngle = Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude);
        var shotRotation = (-90f + Mathf.Rad2Deg * Mathf.Atan2(direction.z, direction.x)) % 360f;

        var projectile = (ProjectileCE)ThingMaker.MakeThing(source.def);
        GenSpawn.Spawn(projectile, cell, context.map);
        projectile.canTargetSelf = false;
        projectile.minCollisionDistance = 0f;
        projectile.logMisses = false;
        projectile.AccuracyFactor = source.AccuracyFactor;
        projectile.damageDefOverride = source.damageDefOverride;
        projectile.mount = source.mount;
        projectile.Launch(source.launcher, new Vector2(exit.x, exit.z), shotAngle, shotRotation, exit.y, context.speed, source.equipment);
        projectile.ticksToTruePosition = 0;
        projectile.DamageAmount = damage;
        PenetrationTracker.Set(projectile, new FollowUpState
        {
            penetration = residual,
            damage = damage,
            passes = context.passes + 1,
            passedThing = context.target
        });
    }

    public static void MeasureArmor(ImpactContext context)
    {
        var pawn = context.pawn;
        var outer = ArmorUtilityCE.GetFirstOuterPart(context.hitPart);
        var armorStat = StatDefOf.ArmorRating_Sharp;

        var apparelArmor = 0f;
        var shieldArmor = 0f;
        var worn = pawn.apparel?.WornApparel;
        if (worn != null)
        {
            for (var i = 0; i < worn.Count; i++)
            {
                var apparel = worn[i];
                if (apparel is Apparel_Shield shield)
                {
                    if (ShieldBlocks(pawn, shield, outer))
                    {
                        shieldArmor += shield.GetStatValue(armorStat);
                    }
                    continue;
                }
                if (apparel.def.apparel.CoversBodyPart(outer))
                {
                    apparelArmor += apparel.PartialStat(armorStat, outer);
                }
            }
        }

        var naturalArmor = outer.IsInGroup(CE_BodyPartGroupDefOf.CoveredByNaturalArmor) ? pawn.PartialStat(armorStat, outer) : 0f;
        context.exitArmor = apparelArmor + naturalArmor;
        context.entryArmor = context.exitArmor + shieldArmor;
    }

    private static bool ShieldBlocks(Pawn pawn, Apparel_Shield shield, BodyPartRecord part)
    {
        var extension = shield.def.GetModExtension<ShieldDefExtension>();
        if (extension == null || !extension.PartIsCoveredByShield(part, pawn.IsCrouching()))
        {
            return false;
        }
        return !((pawn.stances?.curStance as Stance_Busy)?.verb != null && part.IsInGroup(CE_BodyPartGroupDefOf.RightArm));
    }

    private static void DebugLog(ImpactContext context, string text)
    {
        if (OverpenetrationMod.Settings.debugLog)
        {
            Log.Message($"[Overpenetration] {context.projectile.def.defName} hit {context.target.LabelShort}: {text}");
        }
    }

    private static float ResidualPenetration(ImpactContext context, OverpenetrationSettings settings, out float density, out float thickness)
    {
        var outer = ArmorUtilityCE.GetFirstOuterPart(context.hitPart);
        density = context.pawn.GetStatValue(CE_StatDefOf.BodyPartSharpArmor);
        thickness = density * settings.thickness * outer.def.hitPoints / 10f;

        var residual = context.penetration - context.entryArmor - density - thickness;
        if (settings.exitArmor)
        {
            residual -= context.exitArmor;
        }
        return residual;
    }

    private static Vector3 ExitPoint(Bounds bounds, Vector3 position, Vector3 direction)
    {
        var reach = bounds.size.magnitude + 1f;
        var far = position + direction * reach;
        if (bounds.IntersectRay(new Ray(far, -direction), out var distance))
        {
            return far - direction * distance + direction * ExitOffset;
        }
        return position + direction * ExitOffset;
    }
}
