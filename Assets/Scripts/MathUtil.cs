//
// Permission is granted to use, copy, distribute and prepare derivative works of this
// software for any purpose and without fee, provided, that the above copyright notice
// and this statement appear in all copies.  Intel makes no representations about the
// suitability of this software for any purpose.  THIS SOFTWARE IS PROVIDED "AS IS."
// INTEL SPECIFICALLY DISCLAIMS ALL WARRANTIES, EXPRESS OR IMPLIED, AND ALL LIABILITY,
// INCLUDING CONSEQUENTIAL AND OTHER INDIRECT DAMAGES, FOR THE USE OF THIS SOFTWARE,
// INCLUDING LIABILITY FOR INFRINGEMENT OF ANY PROPRIETARY RIGHTS, AND INCLUDING THE
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE.  Intel does not
// assume any responsibility for any errors which may appear in this software nor any
// responsibility to update it.
//--------------------------------------------------------------------------------------

using UnityEngine;

public static class MathUtil {
    /*
        * Intersection test b/w an axis-aligned box and a sphere
        * [!! Ensure that the sphere's position is in the space as the AAB
        * c1, c2 -- opposite corners of the box
        * s -- center of the sphere
        * r -- radius of the sphere
        */
    public static bool DoesBoxIntersectSphere(Vector3 c1, Vector3 c2, Vector3 s, float r)
    {
        float r2 = r * r;

        if (s.x < c1.x) r2 -= squared(s.x - c1.x);
        else if (s.x > c2.x) r2 -= squared(s.x - c2.x);

        if (s.y < c1.y) r2 -= squared(s.y - c1.y);
        else if (s.y > c2.y) r2 -= squared(s.y - c2.y);

        if (s.z < c1.z) r2 -= squared(s.z - c1.z);
        else if (s.z > c2.z) r2 -= squared(s.z - c2.z);

        return r2 > 0;
    }

    public static float squared(float d) { return d * d; }

}
