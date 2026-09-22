#!/usr/bin/env ruby
# Deterministic generator for explicit Weapon shared-Properties rows.

default_root = File.expand_path('../..', __dir__)
root = File.expand_path(ARGV.fetch(0, default_root))
weapon_dir = File.join(root, 'src/IW4.Assets/Assets/Weapon')
output = File.join(root, 'apps/IW4Studio/Editors/Weapon/WeaponInspectorProjection.Generated.cs')
Property = Struct.new(:type, :name)
classes = {}
Dir[File.join(weapon_dir, 'Weapon*.cs')].sort.each do |path|
  text = File.read(path)
  match = text.match(/public sealed class (Weapon\w+)/)
  next unless match
  props = text.scan(/^\s*public\s+(.+?)\s+(\w+)\s*\{\s*get;\s*init;\s*\}/).map { |type, name| Property.new(type.strip, name) }
  classes[match[1]] = props unless props.empty?
end

def list_type(type); type[/IReadOnlyList<(.+)>/, 1]; end
def semantic?(prop, classes)
  return false if prop.name == 'Offset' || prop.name == 'Definition' || prop.name == 'InternalName'
  return false if prop.name.include?('Pointer') || prop.name.include?('Padding')
  return false if %w[
    AiVsAiAccuracyGraphKnotCount
    AiVsPlayerAccuracyGraphKnotCount
    OriginalAiVsAiGraphKnotCount
    OriginalAiVsPlayerGraphKnotCount
  ].include?(prop.name)
  return false if classes.key?(prop.type) || list_type(prop.type)
  true
end
def cs_type(type)
  type.gsub('Material.MaterialAsset', 'MaterialAsset').gsub('Math.Vec2', 'Vec2').gsub('Math.Vec3', 'Vec3')
end
def label(name)
  value = name.gsub(/([a-z0-9])([A-Z])/, '\\1 \\2')
      .gsub(/([A-Z]+)([A-Z][a-z])/, '\\1 \\2')
      .split
      .map { |word| { 'Ads' => 'ADS', 'Ai' => 'AI', 'Emp' => 'EMP', 'Hud' => 'HUD', 'Dof' => 'DOF', 'Fov' => 'FOV', 'Vs' => 'vs.' }.fetch(word, word) }
      .join(' ')
  value
end
def property_label(class_name, name)
  overrides = {
    ['WeaponFlashEffectFields', 'View'] => 'View Flash Effect',
    ['WeaponFlashEffectFields', 'World'] => 'World Flash Effect',
    ['WeaponShellEjectEffectFields', 'View'] => 'View Shell Eject Effect',
    ['WeaponShellEjectEffectFields', 'World'] => 'World Shell Eject Effect',
    ['WeaponShellEjectEffectFields', 'ViewLastShot'] => 'View Last-Shot Shell Eject Effect',
    ['WeaponShellEjectEffectFields', 'WorldLastShot'] => 'World Last-Shot Shell Eject Effect',
    ['WeaponReticleFields', 'CenterMaterial'] => 'Center Reticle Material',
    ['WeaponReticleFields', 'SideMaterial'] => 'Side Reticle Material',
    ['WeaponOverlayFields', 'Material'] => 'Overlay Material',
    ['WeaponOverlayFields', 'MaterialLowRes'] => 'Low-Resolution Overlay Material',
    ['WeaponOverlayFields', 'MaterialEmp'] => 'EMP Overlay Material',
    ['WeaponOverlayFields', 'MaterialEmpLowRes'] => 'Low-Resolution EMP Overlay Material'
  }
  overrides.fetch([class_name, name], label(name))
end
def lower_camel(name)
  name.sub(/\A([A-Z]+)(?=[A-Z][a-z]|\z)/) { Regexp.last_match(1).downcase }
      .sub(/\A./) { |value| value.downcase }
end
def path_name(parts); parts.map { |part| lower_camel(part) }.join('.') end
def add_call(type, vm, rows, label_text, field_path, current, setter)
  type = cs_type(type)
  common = "#{vm}, #{rows}, \"#{label_text}\", \"#{field_path}\", #{current}"
  return "AddString(#{common}, #{setter})" if type == 'string?'
  return "AddFloat(#{common}, #{setter})" if type == 'float'
  return "AddInteger(#{common}, #{setter})" if type == 'int'
  return "AddUnsigned(#{common}, #{setter})" if type == 'uint'
  return "AddUShort(#{common}, #{setter})" if type == 'ushort'
  return "AddByteFlag(#{common}, #{setter})" if type == 'byte'
  return "AddBoolean(#{common}, #{setter})" if type == 'bool'
  return "AddVec2(#{common}, #{setter})" if type == 'Vec2'
  return "AddVec3(#{common}, #{setter})" if type == 'Vec3'
  assets = {
    'XModelAsset?' => 'XModel', 'MaterialAsset?' => 'Material',
    'FxEffectDefAsset?' => 'Fx', 'PhysCollmapAsset?' => 'PhysCollmap',
    'TracerDefAsset?' => 'Tracer'
  }
  return "AddAsset<#{type.delete_suffix('?')}>(#{common}, XAssetType.#{assets[type]}, #{setter})" if assets.key?(type)
  "AddEnum<#{type}>(#{common}, #{setter})"
end

group_categories = {
  'WeaponFlashEffectFields' => 'EffectsAndMaterials',
  'WeaponShellEjectEffectFields' => 'EffectsAndMaterials',
  'WeaponPrimarySoundFields' => 'SoundsAndBounce',
  'WeaponReticleFields' => 'ClassificationAndReticle',
  'WeaponViewMovementFields' => 'ViewAndPositionalMovement',
  'WeaponPositionalMovementFields' => 'ViewAndPositionalMovement',
  'WeaponAmmoFields' => 'HudIconsAndAmmo',
  'WeaponIconPointers' => 'HudIconsAndAmmo',
  'WeaponTimingFields' => 'Timing',
  'WeaponAimMovementTuningFields' => 'AimAndMovementTuning',
  'WeaponOverlayFields' => 'OverlayAdsAndSpread',
  'WeaponAdsViewAndSpreadFields' => 'OverlayAdsAndSpread',
  'WeaponPhysicsFields' => 'PhysicsAndProjectile',
  'WeaponProjectileFields' => 'PhysicsAndProjectile',
  'WeaponGunKickAndDistanceFields' => 'KickRecoilAndAccuracy',
  'WeaponAccuracyFields' => 'KickRecoilAndAccuracy',
  'WeaponTurnSpeedAndRangeFields' => 'DamageRangeAndAiTuning',
  'WeaponHintFields' => 'HintsAndRumble',
  'WeaponRumbleFields' => 'HintsAndRumble',
  'WeaponTurretFields' => 'TurretAndMissile',
  'WeaponMissileConeSoundFields' => 'TurretAndMissile',
  'WeaponTailFlags' => 'TailAndPreservedStorage'
}

top_definition = {
  'Overview' => %w[ModeName ScriptName],
  'ClassificationAndReticle' => %w[PlayerAnimType WeaponType WeaponClass PenetrateType InventoryType FireType OffhandClass Stance],
  'PhysicsAndProjectile' => %w[PhysCollmap PhysCollmapName Tracer],
  'DamageRangeAndAiTuning' => %w[AdsTransitionInRate AdsTransitionOutRate MinDamage MinPlayerDamage MaxDamageRange MinDamageRange DestabilizationRateTime DestabilizationCurvatureMax DestabilizeDistance],
  'TurretAndMissile' => %w[TurretScopeZoomRate TurretScopeZoomMin TurretScopeZoomMax TurretOverheatUpRate TurretOverheatDownRate TurretOverheatPenalty]
}
top_variant = {
  'Overview' => %w[DisplayName AdsZoomFov AdsTransitionInTime AdsTransitionOutTime ClipSize ImpactType FireTime PenetrateMultiplier AdsViewKickCenterSpeed HipViewKickCenterSpeed AlternateWeaponName AlternateWeaponIndex AlternateRaiseTime FireAnimLength FirstRaiseTime AmmoDropStockMax AdsDofStart AdsDofEnd],
  'HudIconsAndAmmo' => %w[DpadIconRatio KillIcon DpadIcon],
  'TailAndPreservedStorage' => %w[MotionTracker Enhanced DpadIconShowsAmmo]
}

paths = {}
walk = lambda do |class_name, prefix, expr|
  paths[class_name] ||= []
  paths[class_name] << [prefix, expr]
  classes.fetch(class_name).each do |prop|
    walk.call(prop.type, prefix + [prop.name], "#{expr}.#{prop.name}") if classes.key?(prop.type)
  end
end
walk.call('WeaponDef', ['Definition'], 'definition')

lines = []
lines << '// <auto-generated by tools/WeaponGeneration/generate_weapon_inspector_projection.rb />'
lines << '#nullable enable'
lines << 'using System.Globalization;'
lines << 'using IW4.Assets.Assets;'
lines << 'using IW4.Assets.Assets.Fx;'
lines << 'using IW4.Assets.Assets.Material;'
lines << 'using IW4.Assets.Assets.Physics;'
lines << 'using IW4.Assets.Assets.Tracer;'
lines << 'using IW4.Assets.Assets.Weapon;'
lines << 'using IW4.Assets.Assets.XModel;'
lines << 'using IW4.FastFiles.Strings;'
lines << 'using IW4.FastFiles.Zone;'
lines << 'using IW4.Studio.Desktop.Editors.AssetReferences;'
lines << 'using IW4.Studio.Desktop.Editors.Inspector;'
lines << 'using IW4.Studio.Desktop.ViewModels;'
lines << 'using IW4.Studio.Documents;'
lines << 'using Vec2 = IW4.Assets.Math.Vec2;'
lines << 'using Vec3 = IW4.Assets.Math.Vec3;'
lines << ''
lines << 'namespace IW4.Studio.Desktop.Editors.Weapon;'
lines << ''
lines << 'internal static class WeaponInspectorProjection'
lines << '{'
lines << '    internal static InspectorSelectionViewModel Create(WeaponEditorViewModel vm)'
lines << '    {'
lines << '        WeaponDraft draft = vm.WorkingDraft;'
lines << '        if (draft.Definition is not { } definition)'
lines << '            return new InspectorSelectionViewModel(vm.Name, "WEAPON", [new InspectorSectionViewModel("Definition unavailable", [ReadOnly("Name", "weapon.variant.internalName", vm.Name), ReadOnly("Access", "weapon.access", vm.AccessText), ReadOnly("Reason", "weapon.variant.definition", "The serialized variant does not reference a WeaponDef.")])]);'
lines << '        var rows = new List<InspectorPropertyRowViewModel>();'
lines << '        switch (vm.SelectedCategory.Id)'
lines << '        {'

(top_variant.keys | top_definition.keys | group_categories.values).uniq.each do |category|
  lines << "            case WeaponPropertyCategory.#{category}:"
  Array(top_variant[category]).each do |name|
    prop = classes['WeaponVariantDef'].find { |value| value.name == name }
    lines << '                ' + add_call(prop.type, 'vm', 'rows', label(name), "weapon.variant.#{lower_camel(name)}", "draft.Variant.#{name}", "vm.IsEditable ? value => vm.Mutate(() => draft.SetVariant#{name}(value)) : null") + ';'
  end
  Array(top_definition[category]).each do |name|
    next if name == 'PhysCollmapName'
    prop = classes['WeaponDef'].find { |value| value.name == name }
    setter = if name == 'PhysCollmap'
      'vm.IsEditable ? value => vm.Mutate(() => { draft.SetDefinitionPhysCollmap(value); draft.SetDefinitionPhysCollmapName(value?.SerializedAssetName); }) : null'
    else
      "vm.IsEditable ? value => vm.Mutate(() => draft.SetDefinition#{name}(value)) : null"
    end
    call = add_call(prop.type, 'vm', 'rows', label(name), "weapon.definition.#{lower_camel(name)}", "definition.#{name}", setter)
    call = call.sub(/\)\z/, ', definition.PhysCollmapName)') if name == 'PhysCollmap'
    lines << '                ' + call + ';'
  end
  group_categories.select { |_, value| value == category }.each_key do |class_name|
    Array(paths[class_name]).each do |prefix, expr|
      next unless prefix.first == 'Definition'
      lines << "                Add#{class_name}Rows(vm, rows, #{expr});"
    end
  end
  if category == 'Overview'
    lines << '                rows.Add(ReadOnly("Variant identity", "weapon.variant.internalName", draft.Variant.InternalName ?? "Unavailable"));'
    lines << '                rows.Add(ReadOnly("Definition identity", "weapon.definition.internalName", definition.InternalName ?? "Unavailable"));'
  elsif category == 'KickRecoilAndAccuracy'
    lines << '                rows.Add(ReadOnly("AI vs. AI current knot count", "weapon.variant.aiVsAiAccuracyGraphKnotCount", draft.Variant.AiVsAiAccuracyGraphKnotCount.ToString(CultureInfo.InvariantCulture)));'
    lines << '                rows.Add(ReadOnly("AI vs. player current knot count", "weapon.variant.aiVsPlayerAccuracyGraphKnotCount", draft.Variant.AiVsPlayerAccuracyGraphKnotCount.ToString(CultureInfo.InvariantCulture)));'
    lines << '                rows.Add(ReadOnly("AI vs. AI original knot count", "weapon.definition.accuracy.originalAiVsAiGraphKnotCount", definition.Accuracy.OriginalAiVsAiGraphKnotCount.ToString(CultureInfo.InvariantCulture)));'
    lines << '                rows.Add(ReadOnly("AI vs. player original knot count", "weapon.definition.accuracy.originalAiVsPlayerGraphKnotCount", definition.Accuracy.OriginalAiVsPlayerGraphKnotCount.ToString(CultureInfo.InvariantCulture)));'
  elsif category == 'TailAndPreservedStorage'
    lines << '                rows.Add(ReadOnly("Variant offset", "weapon.variant.offset", draft.Variant.Offset.ToString(CultureInfo.InvariantCulture)));'
    lines << '                rows.Add(ReadOnly("Definition offset", "weapon.definition.offset", definition.Offset.ToString(CultureInfo.InvariantCulture)));'
    lines << '                rows.Add(ReadOnly("Variant padding 0x73", "weapon.preserved.variant.padding73", draft.Variant.Padding73.ToString(CultureInfo.InvariantCulture)));'
    lines << '                rows.Add(ReadOnly("Reserved padding", "weapon.preserved.definition.tailFlags.reservedPadding", definition.TailFlags.ReservedPadding.ToString(CultureInfo.InvariantCulture)));'
  end
  lines << '                break;'
end
lines << '        }'
lines << '        if (vm.SelectedIndexedRow is { } selected) AddIndexedRows(vm, draft, definition, selected, rows);'
lines << '        if (rows.Count == 0) rows.Add(ReadOnly("Selection", "weapon.selection", "No editable semantic values are present for this selection."));'
lines << '        return new InspectorSelectionViewModel(vm.SelectedIndexedRow?.Title ?? vm.SelectedCategory.Title, "WEAPON", [new InspectorSectionViewModel(vm.SelectedCategory.Title, rows)], "The selected Weapon values are edited here; storage provenance remains read-only.");'
lines << '    }'
lines << ''

group_categories.each_key do |class_name|
  paths.fetch(class_name, []).each do |prefix, expr|
    next unless prefix.first == 'Definition'
    method_prefix = prefix.join
    lines << "    private static void Add#{class_name}Rows(WeaponEditorViewModel vm, List<InspectorPropertyRowViewModel> rows, #{class_name} current)"
    lines << '    {'
    classes.fetch(class_name).each do |prop|
      field_path = 'weapon.' + path_name(prefix + [prop.name])
      setter = "vm.IsEditable ? value => vm.Mutate(() => vm.WorkingDraft.Set#{method_prefix}#{prop.name}(value)) : null"
      sound_name = {
        'WeaponProjectileFields' => %w[ExplosionSound DudSound IgnitionSound],
        'WeaponTurretFields' => %w[OverheatSound BarrelSpinMaxSound],
        'WeaponMissileConeSoundFields' => %w[Alias AliasAtBase]
      }.fetch(class_name, []).include?(prop.name)
      if class_name == 'WeaponPrimarySoundFields' && prop.type == 'WeaponSoundAliasField'
        lines << "        AddNameAsset(vm, rows, \"#{property_label(class_name, prop.name)}\", \"#{field_path}.name\", current.#{prop.name}.Name, XAssetType.Sound, vm.IsEditable ? value => vm.Mutate(() => vm.WorkingDraft.Set#{method_prefix}#{prop.name}Name(value)) : null);"
      elsif !semantic?(prop, classes)
        next
      elsif class_name == 'WeaponProjectileFields' && prop.name == 'Model'
        next
      elsif sound_name
        lines << "        AddNameAsset(vm, rows, \"#{property_label(class_name, prop.name)}\", \"#{field_path}\", current.#{prop.name}, XAssetType.Sound, #{setter});"
      else
        lines << '        ' + add_call(prop.type, 'vm', 'rows', property_label(class_name, prop.name), field_path, "current.#{prop.name}", setter) + ';'
      end
    end
    lines << '    }'
    lines << ''
    break
  end
end

lines << File.read(File.join(__dir__, 'weapon_inspector_projection_tail.csfrag'))
lines << '}'
File.write(output, lines.join("\n") + "\n")
puts "generated #{output}: #{lines.length} source lines"
