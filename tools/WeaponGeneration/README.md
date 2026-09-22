# Weapon generated code

The scripts in this directory are the canonical source for the generated Weapon editor code. They require a `ruby` interpreter (verified with Ruby 2.6.10; no gems are required) and read the checked-in `src/IW4.Assets/Assets/Weapon/Weapon*.cs` model files in sorted path order. Inspector generation also appends `weapon_inspector_projection_tail.csfrag`.

From the repository root, regenerate both outputs with:

```sh
ruby tools/WeaponGeneration/generate_weapon_code.rb
```

The command writes:

- `src/IW4.Studio/Documents/WeaponEditing/WeaponDraft.GeneratedMutations.cs`
- `apps/IW4Studio/Editors/Weapon/WeaponInspectorProjection.Generated.cs`

The driver and either individual generator accept an optional repository-root argument for callers outside this checkout:

```sh
ruby /path/to/IW4Studio/tools/WeaponGeneration/generate_weapon_code.rb /path/to/IW4Studio
```

Re-running the command without changing its inputs must leave both generated files unchanged.
