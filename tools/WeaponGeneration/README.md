# Weapon generated code

The types under `src/IW4.Assets/Assets/Weapon` describe the fixed, recovered IW4 Weapon engine layout. These scripts are optional developer maintenance and provenance tooling for reproducing the checked-in editor boilerplate from those types; inspector generation also appends `weapon_inspector_projection_tail.csfrag`.

IW4Studio does not invoke these scripts during normal builds, application runtime, or weapon editing, so application users do not need Ruby. Developers who intentionally regenerate the checked-in outputs need a `ruby` interpreter (verified with Ruby 2.6.10; no gems are required).

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
