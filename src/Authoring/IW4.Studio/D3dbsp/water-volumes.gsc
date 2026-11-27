// Generated maps own this overlay and the level-priority reverb slot.
// Water rendering remains on the GPU; this samples only each player's eye.
main()
{
    if (isDefined(level.iw4r_water_volumes))
        return;
    precacheShader("white");
    level.iw4r_water_volumes = [];
__VOLUMES__
    level thread connections();
    if (isDefined(level.players))
        foreach (player in level.players)
            player start();
}

connections()
{
    for (;;)
    {
        level waittill("connected", player);
        player start();
    }
}

start()
{
    if (isDefined(self.iw4r_water_monitor))
        return;
    self.iw4r_water_monitor = true;
    self thread monitor();
}

monitor()
{
    self endon("disconnect");
    overlay = newClientHudElem(self);
    overlay.x = 0;
    overlay.y = 0;
    overlay.alignX = "left";
    overlay.alignY = "top";
    overlay.horzAlign = "fullscreen";
    overlay.vertAlign = "fullscreen";
    overlay setShader("white", 640, 480);
    overlay.sort = -20;
    overlay.foreground = false;
    overlay.archived = false;
    overlay.hideWhenDead = true;
    overlay.hideWhenInMenu = true;
    overlay.alpha = 0;
    self thread releaseOverlay(overlay);
    submerged = false;
    activeVolume = -1;
    for (;;)
    {
        inside = false;
        volume = undefined;
        if (isAlive(self) && self.sessionstate == "playing" && !isDefined(self.usingRemote) &&
            !(isDefined(level.gameEnded) && level.gameEnded))
        {
            margin = -__INSET__;
            if (submerged)
                margin = __INSET__;
            volume = volumeAtEye(self getEye(), margin);
            inside = isDefined(volume);
        }
        if (inside && activeVolume != volume.index)
        {
            overlay.color = volume.tint;
            activeVolume = volume.index;
        }
        if (inside != submerged)
        {
            overlay fadeOverTime(__FADE__);
            if (inside)
            {
                overlay.alpha = __OPACITY__;
                self setReverb("snd_enveffectsprio_level", "underwater", 0.35, 0.65, __FADE__);
            }
            else
            {
                overlay.alpha = 0;
                self deactivateReverb("snd_enveffectsprio_level", __FADE__);
            }
            submerged = inside;
        }
        if (!inside)
            activeVolume = -1;
        wait 0.1;
    }
}

releaseOverlay(overlay)
{
    self waittill("disconnect");
    if (isDefined(overlay))
        overlay destroy();
}

volumeAtEye(eye, margin)
{
    foreach (volume in level.iw4r_water_volumes)
    {
        if (eye[0] < volume.minimum[0] - margin || eye[0] > volume.maximum[0] + margin ||
            eye[1] < volume.minimum[1] - margin || eye[1] > volume.maximum[1] + margin ||
            eye[2] < volume.minimum[2] - margin || eye[2] > volume.maximum[2] + margin)
            continue;
        inside = true;
        for (i = 0; i < volume.normals.size; i++)
            if (vectorDot(eye, volume.normals[i]) > volume.distances[i] + margin)
            {
                inside = false;
                break;
            }
        if (inside)
            return volume;
    }
    return undefined;
}
