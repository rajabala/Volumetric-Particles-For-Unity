using UnityEngine;
using System.Collections;
using System.Collections.Generic;


[RequireComponent(typeof(ParticleSystem))]

public class AABBForParticles : MonoBehaviour {    
    public Bounds aabb; // box is aligned to main camera's axes
    private ParticleSystem ps;
    private ParticleSystem.Particle[] particles;
    // Use this for initialization
    void Start()
    {
        ps = gameObject.GetComponent<ParticleSystem>();
        particles = new ParticleSystem.Particle[ps.maxParticles];
    }

    // Update is called once per frame
    void Update()
    {
        // [todo] check if any of the bodies have moved to update the aabb
        FindAABB();
    }

    void FindAABB()
    {
        int numParticles = ps.GetParticles(particles);

        Matrix4x4 worldToCamera = Camera.main.worldToCameraMatrix;

        Vector3 max = new Vector3(), min = new Vector3();
        bool firstTime = true;

        for (int ii = 0; ii < numParticles; ii++)
        {
            Vector3 pWorldPos = transform.localToWorldMatrix.MultiplyPoint3x4(particles[ii].position);

            // xform to camera space
            Vector3 csPos = worldToCamera.MultiplyPoint3x4(pWorldPos);
            //Debug.Log("particle " + ii + "is at  " + particles[ii].position);
            float radius = particles[ii].size * 0.5f;
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
