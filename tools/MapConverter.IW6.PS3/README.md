# PS3 IW6 to PS3 IW4 map conversion

The supported input is the PS3 retail edition of Ghosts’ Fog (mp_swamp, fastfile version 558). The IW6 readers verify the inflated map fingerprint before using revision-specific record layouts. The Python readers decode the source assets. F# rules translate gameplay entities, bind shared model materials, select material passes and native terrain layouts, map collision and material surface types, and adapt the descending spider effect. The normal converter command applies those rules before D3dbspLinker packages the IW4 assets. Supporting another map requires a source reader profile for its fastfile.

Build the target linker from the repository root. The converter also needs Python 3.10 or newer, the .NET SDK, the original DLC fastfiles and image package, and an exported native IW4 PS3 raw asset library. The base Ghosts PS3 directory must contain the image packages, `common_mp.ff`, and `common_core_mp.ff`.

```sh
dotnet build tools/D3dbspLinker/D3dbspLinker.csproj
python3 -B tools/MapConverter.IW6.PS3/convert.py \
  /path/to/ghosts-dlc/USRDIR/mp_swamp.ff \
  --base-image-packages /path/to/ghosts/PS3_GAME/USRDIR \
  --iw4-library /path/to/MW2/raw \
  --output /path/to/mods/maps/mp_swamp
```

Supply a new output folder. Keep mp_swamp_load.ff and imagefile64.pak alongside the input map. Pass `--work /path/to/new-directory` to retain the decoded zone, BSP, native assets, and conversion inventories. Use `--linker` to select a separately built D3dbspLinker executable.

The output folder contains the gameplay, loading, and UI fastfiles, the named texture package, and `mp_swamp_snd.pak` for streamed audio. Keep all five files together in the PS3 IW4 custom-map loader’s `mods/maps/mp_swamp` directory. The UI uses the original loading artwork for its preview.

Fog owns and precaches Michael Myers' axe as `iw6_axe_mp`. Players use IW4's melee control to swing it. The first-person swing, audio, 300 damage, 50 ms hit delay, and 800 ms attack time come from Ghosts. Third-person movement uses IW4's player animations. A gamemode in `patch_mp.ff` can grant the axe inside its spawn function after assigning the normal loadout.

```gsc
if ( getDvar( "mapname" ) == "mp_swamp" )
{
    self giveWeapon( "iw6_axe_mp", 0, false );
    self setSpawnWeapon( "iw6_axe_mp" );
}
```

For a player who has already spawned, use `self switchToWeapon( "iw6_axe_mp" );` after granting it.

The map carries Free-for-All, Team Deathmatch, Domination, and Search and Destroy spawn data. Objective scripts use IW4’s stock bomb and flag models. The cave breach uses the original planks, cellar door, and interaction volume. Activating the charge opens both ends with IW4 explosion effects and sound. Care packages and helicopter paths use the original map locations. The 60 breakable glass panes retain their geometry and collision bindings. The 120 movable clutter props retain their source placements as scenery. The two plastic chairs and the folding chair by the campsite trailer have player-only clip brushes.

The conversion retains the source lightmaps, outdoor lookup, and six authored reflection probes. Static props carry their Ghosts lighting origin, primary light, reflection probe, and per-placement draw distance. The port carries Fog’s 48 render cells, 200 directed portals, and per-cell scenery trees into IW4. Moving objects use the authored reflection-probe list for their current cell. The converter reads the original light-grid octree, evaluates the directional lighting at IW4’s 56 sample directions, and keeps each sample’s primary light. IW4 supports one continuous vertical span per column. For a gap inside that span, the converter extends the nearest authored sample and enables sight tracing. Metal grating uses IW4’s metal contact type.

The material converter matches Ghosts technique families, sampler states, and material constants to native IW4 passes. Painted terrain uses native IW4 multilayer geometry with its original UVs, paint weights, and packed normal transforms. Compatible passes retain the Ghosts reveal and height shading. Other terrain passes use the corresponding IW4 layer blend. The converter also retains compatible Ghosts fragments for dust, candle tint, and the sky’s elevation fog.

Vegetation uses the original wind programs where the IW4 vertex declaration and lighting pass can supply their inputs. Tall grass retains its painted blade masks and procedural wind. Its reactive displacement is zero because IW4 does not supply Ghosts’ motion centers. The converter reads the mounted deer’s eye material and particle images for fire, candle glow, smoke, blood, and sparks from the shared multiplayer fastfiles.

Other Ghosts-specific scripted events, Blitz, and the Ghosts navigation graph are outside the supported profile.
