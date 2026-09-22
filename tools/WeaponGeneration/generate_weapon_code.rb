#!/usr/bin/env ruby
# Regenerates both checked-in Weapon editor outputs from the Weapon model sources.

require 'rbconfig'

default_root = File.expand_path('../..', __dir__)
root = File.expand_path(ARGV.fetch(0, default_root))
generators = %w[
  generate_weapon_draft_mutations.rb
  generate_weapon_inspector_projection.rb
]

generators.each do |generator|
  path = File.join(__dir__, generator)
  abort "generator failed: #{path}" unless system(RbConfig.ruby, path, root)
end
