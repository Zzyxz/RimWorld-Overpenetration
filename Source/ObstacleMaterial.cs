using RimWorld;
using UnityEngine;
using Verse;

namespace CEOverpenetration;

internal static class ObstacleMaterial
{
    private const float WoodyBase = 5f;
    private const float MetallicBase = 12f;
    private const float StonyBase = 15f;
    private const float BagValue = 20f;
    private const float TreeValue = 15f;
    private const float MinTreeGrowth = 0.2f;
    private const float DoorFactor = 0.5f;
    private const float WornFloor = 0.3f;

    public static bool IsObstacle(Thing thing)
    {
        return thing is Building || thing is Plant plant && plant.def.plant != null && plant.def.plant.IsTree;
    }

    public static float Value(Thing thing, float hitPointsFraction, OverpenetrationSettings settings)
    {
        float value;
        if (thing is Plant plant)
        {
            value = TreeValue * Mathf.Max(plant.Growth, MinTreeGrowth);
        }
        else
        {
            if (thing is Mineable)
            {
                return -1f;
            }
            var stuff = thing.Stuff;
            var categories = stuff?.stuffProps?.categories;
            if (categories == null)
            {
                return -1f;
            }
            if (categories.Contains(StuffCategoryDefOf.Fabric) || categories.Contains(StuffCategoryDefOf.Leathery))
            {
                value = BagValue;
            }
            else
            {
                var baseValue = categories.Contains(StuffCategoryDefOf.Stony) ? StonyBase
                    : categories.Contains(StuffCategoryDefOf.Metallic) ? MetallicBase
                    : categories.Contains(StuffCategoryDefOf.Woody) ? WoodyBase
                    : -1f;
                if (baseValue < 0f)
                {
                    return -1f;
                }
                value = baseValue * stuff.stuffProps.statFactors.GetStatFactorFromList(StatDefOf.MaxHitPoints);
            }
            if (thing is Building_Door)
            {
                value *= DoorFactor;
            }
        }

        if (settings.damagedWalls)
        {
            value *= Mathf.Lerp(WornFloor, 1f, hitPointsFraction);
        }
        return value * settings.wallStrength;
    }

    public static string Describe(Thing thing, float hitPointsFraction)
    {
        if (thing is Plant plant)
        {
            return $"tree, growth {plant.Growth:P0}, condition {hitPointsFraction:P0}";
        }
        if (thing is Mineable)
        {
            return "rock";
        }
        return thing.Stuff == null ? "no material" : $"{thing.Stuff.defName}, condition {hitPointsFraction:P0}";
    }
}
