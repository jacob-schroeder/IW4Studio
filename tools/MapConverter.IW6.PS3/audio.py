"""Stage PS3 IW4 sound assets for the Fog conversion."""

from copy import deepcopy
import json
import re
from shutil import copyfile
import struct


INLINE = 0xffffffff
GHOSTS_ALIAS_SIZE = 136
ALIAS_FLOAT_FIELDS = {
    "volumeMin": 28, "volumeMax": 32, "pitchMin": 40, "pitchMax": 44,
    "distanceMin": 48, "distanceMax": 52, "velocityMin": 56,
    "slavePercentage": 72, "probability": 76, "lfePercentage": 80,
    "centerPercentage": 84, "envelopMin": 104, "envelopMax": 108,
    "envelopPercentage": 112,
}

# These aliases are already provided by the stock IW4 PS3 common_mp fastfile.
RESIDENT_SOUND_ALIASES = (
    "gear_rattle_land", "gear_rattle_plr_land", "rocket_explode_layer", "rocket_explode_layer_2",
)

# These exact LoadedSound payloads are also owned by common_mp.
_RESIDENT_LOADED_SOUNDS = {
    *(f"weapons/grenade/weap_expl_flash_bang{index}" for index in (1, 2, 3)),
    *(f"misc/mrk_breathing_better{index}" for index in (10, 11, 12)),
    "fire/fire_woodtrash_small1v2res",
}

# Retail IW4 mp_terminal owns these aliases, which the PS3 linker bootstrap supplies.
RUNTIME_SOUND_ALIASES = (
    "ui_pulse_text_type", "ui_pulse_text_delete",
    *("rocket_explode_" + surface for surface in (
        "default", "bark", "brick", "carpet", "cloth", "concrete", "dirt", "flesh", "foliage",
        "glass", "grass", "gravel", "ice", "metal", "mud", "paper", "plaster", "rock", "sand",
        "snow", "water", "wood", "asphalt", "ceramic", "plastic", "rubber", "cushion", "fruit",
        "paintedmetal", "riotshield", "slush",
    )),
    "rocket_explode_brick_layer", "rocket_explode_concrete_layer", "rocket_explode_sand_layer",
    "bullet_small_snow", "bullet_large_snow", "bulletspray_small_snow",
    *(f"step_{gait}_plr_{surface}" for gait in ("run", "walk") for surface in ("dirt", "metal", "sand", "snow")),
    "step_sprint_plr_metal", "step_sprint_plr_snow",
    *(f"Land{player}_{surface}" for player in ("", "_plr") for surface in ("metal", "sand", "snow")),
    "phy_cargocontainer_default",
)


def _reference_loaded_sound(sound_file, name):
    sound_file.update(
        loadedSoundName="," + name, physicalDataByteCount=0, frameCount=0,
        channelCount=0, sampleRate=0, pad0E=0, pad10=0, seekTableCount=0,
        audioPath=None, seekTablePath=None, audioStatus="unavailable", seekTableStatus="unavailable",
    )


def stage_stock_sounds(raw, library):
    names = ("barrel_mtl_explode_boom", "barrel_mtl_explode_detail", "breathing_better")
    (raw / "soundaliases").mkdir(parents=True, exist_ok=True)
    for name in names:
        alias_path = f"soundaliases/{name}.json"
        doc = json.loads((library / alias_path).read_bytes())
        payload_paths = set()
        for alias in doc["aliases"]:
            for sound_file in alias["soundFiles"]:
                sample = sound_file["loadedSoundName"].removeprefix(",")
                if sample in _RESIDENT_LOADED_SOUNDS:
                    _reference_loaded_sound(sound_file, sample)
                else:
                    payload_paths.update((sound_file["audioPath"], sound_file["seekTablePath"]))
        for path in sorted(payload_paths):
            target = raw / path
            target.parent.mkdir(parents=True, exist_ok=True)
            copyfile(library / path, target)
        (raw / alias_path).write_text(json.dumps(doc, separators=(",", ":")))
    return names


def _alias_values(zone, pos, alias, name):
    alias.update({key: struct.unpack_from(">f", zone, pos + off)[0]
                  for key, off in ALIAS_FLOAT_FIELDS.items()})
    alias["aliasName"] = name
    alias["startDelay"] = struct.unpack_from(">i", zone, pos + 88)[0]


def _loaded_sound_header(zone, pos, name):
    if zone[pos:pos + 8] != b"\x01\x01\0\0\xff\xff\xff\xff":
        raise ValueError(f"Unsupported sound file for {name}")
    root = pos + 32
    header = struct.unpack_from(">IIIHH4xH2xII", zone, root)
    end = zone.index(0, root + 32)
    return zone[root + 32:end].decode("ascii"), header, end + 1


def _owned_mpeg(zone, sound_name, header, start, sound_file):
    name_ptr, length, duration, channels, rate, count, seek_ptr, data_ptr = header
    if name_ptr != INLINE or seek_ptr != INLINE or data_ptr != INLINE:
        raise ValueError(f"Incomplete loaded sound for {sound_name}")
    audio_start = start + 4 * count
    audio = zone[audio_start:audio_start + length]
    if len(audio) != length or length < 2 or audio[0] != 255 or audio[1] & 0xe0 != 0xe0:
        raise ValueError(f"Invalid PS3 MPEG payload on {sound_name}")
    sound_file.update(loadedSoundName=sound_name, physicalDataByteCount=length, frameCount=duration,
                      channelCount=channels, sampleRate=rate, seekTableCount=count)
    return audio, zone[start:audio_start], audio_start + length


def _stage_payload(raw, name, index, sound_file, audio, seek):
    base = f"soundaliases/{name}.payloads/{index:04d}-0000"
    for key, suffix, data in (("audioPath", ".audio.bin", audio), ("seekTablePath", ".seek.bin", seek)):
        sound_file[key] = base + suffix
        path = raw / sound_file[key]
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)


def stage_axe_sounds(zone, raw, library):
    # These lists use packed names from the shared string pool. The NPC
    # swing list points to the four SoundFiles owned by the player swing.
    layouts = (
        ("melee_axe_swing", 0x903987, 0x4000e68d, 4, "weap_m249saw_turret_fire_plr"),
        ("melee_axe_swing_npc", 0x909e48, 0x4000e69d, 4, "weap_c4detpack_safety_npc"),
        ("melee_ax_hit_body", 0x9919b8, 0x4000e3ae, 2, "weap_c4detpack_safety_npc"),
    )
    swing_files = (0x40092dfd, 0x40092e81, 0x40092f11, 0x40092f99)
    owned = {}
    target = raw / "soundaliases"
    target.mkdir(parents=True, exist_ok=True)
    for name, root, name_pointer, count, template in layouts:
        if struct.unpack_from(">III", zone, root) != (name_pointer, INLINE, count):
            raise ValueError(f"Unexpected Fog sound list for {name}")
        doc = json.loads((library / "soundaliases" / (template + ".json")).read_bytes())
        prototype = doc["aliases"][0]
        doc.update(name=name, count=count, aliases=[])
        cursor = root + 12 + count * GHOSTS_ALIAS_SIZE
        for index in range(count):
            row = root + 12 + index * GHOSTS_ALIAS_SIZE
            row_name, file_pointer = struct.unpack_from(">I16xI", zone, row)
            if row_name != name_pointer:
                raise ValueError(f"Unexpected Fog sound alias row for {name}")
            alias = deepcopy(prototype)
            _alias_values(zone, row, alias, name)
            if file_pointer == INLINE:
                sound = alias["soundFiles"][0]
                sound_name, header, start = _loaded_sound_header(zone, cursor, name)
                audio, seek, cursor = _owned_mpeg(zone, sound_name, header, start, sound)
                _stage_payload(raw, name, index, sound, audio, seek)
                if name == "melee_axe_swing":
                    owned[swing_files[index]] = sound
            else:
                alias["soundFiles"] = [owned[file_pointer]]
            doc["aliases"].append(alias)
        (target / (name + ".json")).write_text(json.dumps(doc, separators=(",", ":")))


def ambient_loops(zone, createfx, raw, library):
    names = sorted(set(re.findall(r'ent\.v\[ "soundalias" \] = "([^"]+)";', createfx)))
    src = library / "soundaliases"
    template = (src / "emt_airport_baggage_belt.json").read_bytes()
    target = raw / "soundaliases"
    target.mkdir(parents=True, exist_ok=True)
    owned = {}
    # SndAliasList name and array are streamed together in virtual memory.
    roots = {}
    for match in re.finditer(b"\xff{8}\x00\x00\x00\x01([a-zA-Z0-9_]{2,100})\x00", zone):
        root = match.end()
        pointer, file_ptr = struct.unpack_from(">I16xI", zone, root)
        name = match[1].decode("ascii")
        if pointer >> 28 != 4:
            continue
        roots[name] = root
        if file_ptr == INLINE:
            packed = pointer + (len(match[1]) + 4 & ~3) + GHOSTS_ALIAS_SIZE
            owned[packed] = root + GHOSTS_ALIAS_SIZE
    for name in names:
        doc = json.loads(template)
        doc["name"] = name
        alias = doc["aliases"][0]
        pos = roots[name]
        _alias_values(zone, pos, alias, name)
        pointer = struct.unpack_from(">I", zone, pos + 20)[0]
        file = pos + GHOSTS_ALIAS_SIZE if pointer == INLINE else owned[pointer]
        sound_name, header, start = _loaded_sound_header(zone, file, name)
        f = alias["soundFiles"][0]
        alias_path = target / (name + ".json")
        if sound_name.startswith(",") and sound_name[1:] in _RESIDENT_LOADED_SOUNDS:
            _reference_loaded_sound(f, sound_name[1:])
            alias_path.write_text(json.dumps(doc, separators=(",", ":")))
            continue
        if sound_name.startswith(","):
            stock = json.loads((src / "fire_barrel_small.json").read_bytes())
            f = next(s for a in stock["aliases"] for s in a["soundFiles"] if s["loadedSoundName"] == sound_name[1:])
            audio = (library / f["audioPath"]).read_bytes()
            seek = (library / f["seekTablePath"]).read_bytes()
            alias["soundFiles"] = [f]
        else:
            audio, seek, _ = _owned_mpeg(zone, sound_name, header, start, f)
        _stage_payload(raw, name, 0, f, audio, seek)
        alias_path.write_text(json.dumps(doc, separators=(",", ":")))
    return names
