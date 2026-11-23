using MapExpansions;
using UnityEngine;

namespace Based.Content;

// A wall the game cannot hang anything on gets a wall surface of its own: an unseen box that reaches a few centimetres
// into the wall and stands a few millimetres in front of its face, so the build's ray meets it before the wall. Each
// stretch stops at the wall's doors and windows; across a door the box would close the opening. The ids are saved
// with every item that hangs on them, so renaming one drops what hangs there.
internal static class WallSurfaces
{
    private const float Proud = .005f, Depth = .05f;

    // plane: the wall face's x (Left, Right) or z (Front, Back). from, to: the stretch along the wall. The face names
    // the direction into the room: Front +z, Back -z, Left -x, Right +x.
    public static void Add(PropertyExtensionDefinition extension, string id, PropertySurfaceFace face, float plane,
        float from, float to, float bottom, float top)
    {
        float sign = face is PropertySurfaceFace.Front or PropertySurfaceFace.Right ? 1f : -1f;
        float near = plane + sign * Proud, far = plane - sign * Depth;
        bool alongZ = face is PropertySurfaceFace.Left or PropertySurfaceFace.Right;
        var min = alongZ
            ? new Vector3(Mathf.Min(near, far), bottom, Mathf.Min(from, to))
            : new Vector3(Mathf.Min(from, to), bottom, Mathf.Min(near, far));
        var max = alongZ
            ? new Vector3(Mathf.Max(near, far), top, Mathf.Max(from, to))
            : new Vector3(Mathf.Max(from, to), top, Mathf.Max(near, far));
        var bounds = new Bounds();
        bounds.SetMinMax(min, max);
        var surface = new PropertySurfaceDefinition { Id = id, Bounds = bounds, SurfaceType = PropertySurfaceKind.Wall };
        surface.ValidFaces.Clear();
        surface.ValidFaces.Add(face);
        extension.Surfaces.Add(surface);
    }
}
