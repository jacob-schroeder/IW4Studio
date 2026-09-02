This default_mp.self is a modified version of TU 1.14.

Features:
* includes native gsc parsing, compilation and linking for TU 1.14.
* resolves a bug where PSN accounts created after or changed after 2018 were not syncing stats with the Activision DemonWare server.
* resolves a RCE exploit with MSG_ReadBitsCompressed (both host and client are protected)
* supports mod loader (see below)

TO USE:
Simply replace default_mp.self in your game update directory (usdir) with this supplied executable.

FOR CUSTOM MAPS:
Simply place PS3 compatible custom maps in /usdir/mods/ in your game update folder.
Please utilize this template patch_mp.ff to dynamically load maps in.

RCE Exploit Fix:
The stock decoder had no destination-size parameter, so malicious compressed input could make it continue writing beyond the receiving buffer.
The patch redirects both network callers to a bounded decoder placed at 0x006ED380:
- Host path limit: 0x800 bytes.
- Client path limit: 0x10000 bytes.
- Capacity is checked before every decoded-byte write.
- Input reads stay within the declared compressed data.
- Invalid Huffman nodes, excessive tree depth, or oversized output return 0, causing the message to be discarded.
- Both client-to-host and host-to-client attack directions are covered.
Valid packets retain the normal decoding behavior. The original unsafe decoder is no longer called from either network path.


Enjoy!