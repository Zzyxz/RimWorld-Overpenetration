# Overpenetration for Combat Extended

Bullets with enough armor penetration left after hitting a pawn keep flying and can hit whoever stands behind. The rest is worked out from Combat Extended's own penetration, armor and body values.

## Requirements

- RimWorld 1.6 or 1.7
- Harmony
- Combat Extended

## For other mods

Walls, doors, cover and trees get their value from their material. Buildings without a material, indestructible buildings and natural rock stop every bullet. A mod can set its own value on any ThingDef:

```xml
<modExtensions>
  <li Class="CEOverpenetration.PenetrationExtension">
    <material>18</material>
  </li>
</modExtensions>
```

`material` is the value in mm RHA at full hit points. The hit point condition and the wall strength setting still apply. Use `<impenetrable>true</impenetrable>` instead to stop every bullet. Only buildings and trees are checked.

## Building

Run `Source/Build.ps1`. It builds the 1.7 and 1.6 assemblies and copies the mod files to `Mods/CEOverpenetration`.

## License

MIT
