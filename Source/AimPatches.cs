using CombatExtended;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace CEOverpenetration;

[HarmonyPatch(typeof(Verb_LaunchProjectileCE), nameof(Verb_LaunchProjectileCE.GetTargetHeight))]
internal static class Patch_Verb_LaunchProjectileCE_GetTargetHeight
{
    private const float EdgeMargin = 0.1f;
    private const float LowestAim = 0.3f;
    private const float HighestAim = 1f;
    private const float PreferredAim = 0.7f;
    private const float MinAssumedDepth = 2f;
    private const float MaxAssumedDepth = 8f;

    private static void Postfix(Verb_LaunchProjectileCE __instance, Thing cover, bool roofed, Vector3 targetLoc, ref float __result)
    {
        var settings = OverpenetrationMod.Settings;
        if (settings == null || !settings.enabled || !settings.walls)
        {
            return;
        }
        var props = __instance.projectilePropsCE;
        var target = __instance.CurrentTarget.Thing;
        if (props == null || props.flyOverhead || target == null || !target.Spawned || !ObstacleMaterial.IsObstacle(target)
            || ObstacleMaterial.Value(target, 1f, settings) < 0f)
        {
            return;
        }
        var range = new CollisionVertical(target).HeightRange;
        if (cover != null && cover != target)
        {
            range.min = Mathf.Max(range.min, new CollisionVertical(cover).HeightRange.max);
        }
        var margin = (range.max - range.min) * EdgeMargin;
        var low = Mathf.Max(range.min + margin, LowestAim);
        var high = Mathf.Min(range.max - margin, HighestAim);
        if (high - low <= 0.05f)
        {
            return;
        }
        var chest = Triangular(low, high, Mathf.Clamp(PreferredAim, low, high));
        var shooter = __instance.caster;
        var height = chest;
        if (shooter != null)
        {
            var origin = shooter.TrueCenter();
            var wallDistance = new Vector2(targetLoc.x - origin.x, targetLoc.z - origin.z).magnitude;
            var depth = Patch_JobGiver_AIFightEnemy_TryGiveJob.BlindFireDepth(shooter as Pawn, target) ?? Rand.Range(MinAssumedDepth, MaxAssumedDepth);
            var shotHeight = __instance.ShotHeight;
            height = shotHeight + (chest - shotHeight) * wallDistance / (wallDistance + depth);
            height = Mathf.Clamp(height, range.min + margin, range.max - margin);
        }
        height += props.aimHeightOffset;
        if (height > CollisionVertical.WallCollisionHeight && roofed)
        {
            height = CollisionVertical.WallCollisionHeight;
        }
        __result = height;
    }

    private static float Triangular(float low, float high, float peak)
    {
        var roll = Rand.Value;
        var width = high - low;
        var split = (peak - low) / width;
        return roll < split
            ? low + Mathf.Sqrt(roll * width * (peak - low))
            : high - Mathf.Sqrt((1f - roll) * width * (high - peak));
    }
}
