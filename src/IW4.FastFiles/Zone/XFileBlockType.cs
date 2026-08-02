namespace IW4.FastFiles.Zone;

public enum XFileBlockType : int
{
    TEMP	 = 0,
    PHYSICAL = 1,
    RUNTIME	 = 2,
    VIRTUAL	 = 3,
    LARGE	 = 4,
    CALLBACK = 5,
    VERTEX	 = 6,
    COUNT    = 7
}