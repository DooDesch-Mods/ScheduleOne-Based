using MapExpansions;
using UnityEngine;

namespace Based.Content.Bungalow;

// A map tree is an instanced mesh plus a "Tree rustle sound" object at its trunk. That object holds the mesh the NavMesh
// bake cut around, the trunk collider and the sound trigger. All of it goes with the tree, so no invisible trunk stays.
internal static class MapTrees
{
    public static void Remove(WorldStateDefinition state, string id, float x, float z)
    {
        var area = new Bounds();
        area.SetMinMax(new Vector3(x - .8f, -20f, z - .8f), new Vector3(x + .8f, 40f, z + .8f));
        state.Rules.Add(new ObjectRule
        {
            Id = id, Name = "InstanceWorldObject", Parent = "InstancedMapTrees",
            Side = WorldSide.Main, Effect = ObjectEffect.Remove, Area = area
        });
        state.Rules.Add(new ObjectRule
        {
            Id = id + "-trunk", Name = "Tree rustle sound", Parent = "Tree rustle sounds",
            Side = WorldSide.Main, Effect = ObjectEffect.Remove, Area = area
        });
    }
}
