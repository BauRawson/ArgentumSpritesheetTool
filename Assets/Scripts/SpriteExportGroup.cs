using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class SpriteExportGroup
{
    public string groupName; // Hair, Torso, Weapon, etc
    public List<GameObject> variants;
}
