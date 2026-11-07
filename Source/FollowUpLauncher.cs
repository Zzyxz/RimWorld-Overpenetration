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
        if (context.blocked)
        {
            return;
        }
        if (context.hitPart == null || context.penetration <= 0f || context.map == null || source.launcher == null)
        {
            return;
        }

        var residual = ResidualPenetration(context, settings, out var density, out var thickness);
        if (residual < settings.minResidual)
        {
            return;
        }

        var direction = context.position - context.lastPos;
        if (direction.sqrMagnitude < 1e-6f)
        {
            return;
        }
        direction.Normalize();

        var bounds = context.pawnBounds;
        if (context.pawn.Spawned)
        {
            bounds.Encapsulate(CE_Utility.GetBoundsFor(context.pawn));
        }
        var exit = ExitPoint(bounds, context.position, direction);
        var cell = exit.ToIntVec3();
        if (exit.y <= 0f || !cell.InBounds(context.map))
        {
            return;
        }

        var damage = context.damage * residual / context.penetration;
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
            passedPawn = context.pawn
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
