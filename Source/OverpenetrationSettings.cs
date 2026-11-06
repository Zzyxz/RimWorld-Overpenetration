using HarmonyLib;
using UnityEngine;
using Verse;

namespace CEOverpenetration;

public class OverpenetrationSettings : ModSettings
{
    public const int DefaultMaxPasses = 2;
    public const float DefaultMinResidual = 1f;
    public const float DefaultThickness = 2f;

    public bool enabled = true;
    public int maxPasses = DefaultMaxPasses;
    public float minResidual = DefaultMinResidual;
    public float thickness = DefaultThickness;
    public bool exitArmor = true;
    public bool fragments;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref enabled, "enabled", true);
        Scribe_Values.Look(ref maxPasses, "maxPasses", DefaultMaxPasses);
        Scribe_Values.Look(ref minResidual, "minResidual", DefaultMinResidual);
        Scribe_Values.Look(ref thickness, "thickness", DefaultThickness);
        Scribe_Values.Look(ref exitArmor, "exitArmor", true);
        Scribe_Values.Look(ref fragments, "fragments", false);
    }

    public void Reset()
    {
        enabled = true;
        maxPasses = DefaultMaxPasses;
        minResidual = DefaultMinResidual;
        thickness = DefaultThickness;
        exitArmor = true;
        fragments = false;
    }
}

public class OverpenetrationMod : Mod
{
    public static OverpenetrationSettings Settings;

    public OverpenetrationMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<OverpenetrationSettings>();
        new Harmony("Zzyxzz.CEOverpenetration").PatchAll();
    }

    public override string SettingsCategory() => Content.Name;

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var s = Settings;
        var list = new Listing_Standard();
        list.Begin(inRect.LeftPart(0.6f));
        list.CheckboxLabeled("ZZOP_Enabled".Translate(), ref s.enabled);
        list.Gap();
        list.Label("ZZOP_MaxPasses".Translate(s.maxPasses));
        s.maxPasses = Mathf.RoundToInt(list.Slider(s.maxPasses, 1f, 5f));
        list.Label("ZZOP_MinResidual".Translate(s.minResidual.ToString("0.0")), tooltip: "ZZOP_MinResidualDesc".Translate());
        s.minResidual = Mathf.Round(list.Slider(s.minResidual, 0.1f, 10f) * 10f) / 10f;
        list.Label("ZZOP_Thickness".Translate(s.thickness.ToString("0.0")), tooltip: "ZZOP_ThicknessDesc".Translate());
        s.thickness = Mathf.Round(list.Slider(s.thickness, 0f, 10f) * 10f) / 10f;
        list.Gap();
        list.CheckboxLabeled("ZZOP_ExitArmor".Translate(), ref s.exitArmor, "ZZOP_ExitArmorDesc".Translate());
        list.CheckboxLabeled("ZZOP_Fragments".Translate(), ref s.fragments);
        list.Gap();
        if (list.ButtonText("ZZOP_Reset".Translate()))
        {
            s.Reset();
        }
        list.End();
    }
}
