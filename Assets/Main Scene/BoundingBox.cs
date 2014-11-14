using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections.Generic;

public class BoundingBox : MonoBehaviour {
    public Bounds aabb; // box is aligned to main camera's axes
    private List<Vector3> lastPos;

    // Use this for initialization
	void Start () {

	}
	
	// Update is called once per frame
	void Update () {
        // [todo] check if any of the bodies have moved to update the aabb
        FindAABB();
	}

    void FindAABB()
    {
        Transform allChildren = gameObject.GetComponentInChildren<Transform>();
        Matrix4x4 worldToCamera = Camera.main.worldToCameraMatrix;

        Vector3 max = new Vector3(), min = new Vector3();
        bool firstTime = true;

        foreach (Transform child in allChildren)
        {

            if (child.gameObject.GetComponent<MeshRenderer>() != null)
            {
                // xform to camera space
                Vector3 csPos = worldToCamera.MultiplyPoint3x4(child.position);
                float radius = child.localScale.x * 0.5f;
                Vector3 csMaxPos = csPos + new Vector3(radius, radius, -radius); // camera space looks down -Z
                Vector3 csMinPos = csPos - new Vector3(radius, radius, -radius);

                if (firstTime)
                {
                    max = csMaxPos;
                    min = csMinPos;
                    firstTime = false;
                }
                else
                {
                    max = new Vector3(Mathf.Max(csMaxPos.x, max.x), Mathf.Max(csMaxPos.y, max.y), Mathf.Min(csMaxPos.z, max.z));
                    min = new Vector3(Mathf.Min(csMinPos.x, min.x), Mathf.Min(csMinPos.y, min.y), Mathf.Max(csMinPos.z, min.z)); 
                }
            }
        }

        aabb = new Bounds((max + min) / 2.0f,
                           max - min);
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.matrix = Camera.main.cameraToWorldMatrix;

        Gizmos.DrawWireCube(aabb.center,
                            aabb.size);

        Gizmos.matrix = Matrix4x4.identity;
    }
#endif
}
