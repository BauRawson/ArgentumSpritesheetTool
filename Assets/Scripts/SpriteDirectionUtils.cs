using UnityEngine;
using System;

public static class SpriteDirectionUtils
{
    public static readonly SpriteDirection[] StandardOrder =
    {
        SpriteDirection.N,
        SpriteDirection.NE,
        SpriteDirection.E,
        SpriteDirection.SE,
        SpriteDirection.S,
        SpriteDirection.SW,
        SpriteDirection.W,
        SpriteDirection.NW
    };

    public static float ToAngle(SpriteDirection dir)
    {
        return dir switch
        {
            SpriteDirection.N  => 0f,
            SpriteDirection.NE => 45f,
            SpriteDirection.E  => 90f,
            SpriteDirection.SE => 135f,
            SpriteDirection.S  => 180f,
            SpriteDirection.SW => 225f,
            SpriteDirection.W  => 270f,
            SpriteDirection.NW => 315f,
            _ => 0f
        };
    }
}
