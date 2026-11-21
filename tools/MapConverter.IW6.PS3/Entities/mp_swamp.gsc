main()
{
    explosion = loadFx( "explosions/grenadeexp_concrete_1" );
    maps\mp\mp_swamp_fx::main();
    maps\mp\_load::main();
    maps\mp\mp_swamp_fog::main();
    game["allies"] = "us_army";
    game["axis"] = "opforce_airborne";
    game["attackers"] = "allies";
    game["defenders"] = "axis";

    precacheModel( "mp_body_iw6_mmyers" );
    precacheModel( "vh_iw6_mmyers" );
    precacheItem( "iw6_axe_mp" );
    level.swampStartGameType = level.onStartGameType;
    level.onStartGameType = ::startGameType;

    trigger = getEnt( "swamp_breach_use", "targetname" );
    if ( !isDefined( trigger ) )
        return;

    trigger setCursorHint( "HINT_ACTIVATE" );
    trigger setHintString( &"PLATFORM_DETONATE" );
    trigger thread breach( explosion );
}

startGameType()
{
    // _teams has now assigned the faction and environment models for this round.
    game["axis_model"]["ASSAULT"] = ::myers;
    game["axis_model"]["SMG"] = ::myers;
    game["axis_model"]["LMG"] = ::myers;
    game["axis_model"]["SHOTGUN"] = ::myers;
    game["axis_model"]["SNIPER"] = ::myers;
    game["axis_model"]["GHILLIE"] = ::myers;
    game["axis_model"]["RIOT"] = ::myersRiot;
    [[level.swampStartGameType]]();
}

myers()
{
    self setModel( "mp_body_iw6_mmyers" );
    self.headModel = undefined;
    self setViewmodel( "vh_iw6_mmyers" );
    self.voice = "russian";
}

myersRiot()
{
    self myers();
    self.voice = "seal";
}

breach( explosion )
{
    self waittill( "trigger" );

    props = getEntArray( "swamp_breach_prop", "targetname" );
    for ( i = 0; i < props.size; i++ )
        props[i] delete();

    blockers = getEntArray( "swamp_breach_blocker", "targetname" );
    for ( i = 0; i < blockers.size; i++ )
        blockers[i] delete();

    markers = getEntArray( "swamp_breach_effect", "targetname" );
    for ( i = 0; i < markers.size; i++ )
    {
        playFx( explosion, markers[i].origin, anglesToForward( markers[i].angles ) );
        markers[i] playSound( "barrel_mtl_explode_boom" );
    }

    self delete();
}
