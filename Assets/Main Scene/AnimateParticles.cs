using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class AnimateParticles : MonoBehaviour {
    public float loopTime;
    public float speed;

    Transform[] particles;
    Vector3[] velocities;
    Vector3[] startPos;
    float curTime;
    

	// Use this for initialization
	void Start () {
        // traverse the hierarchy exactly once
        particles = new Transform[transform.childCount];
        velocities = new Vector3[transform.childCount];
        startPos = new Vector3[transform.childCount];

        Transform children = transform.GetComponentInChildren<Transform>();
        //Debug.Log(children.Length);
        Vector3 smokeDirection = new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(0.6f, 1), Random.Range(0.1f, 0.3f));
        int ii = 0;
        foreach (Transform t in children) {
            particles[ii] = t;
            velocities[ii] = smokeDirection;
            startPos[ii] = t.position;

            ii++;
        }

        curTime = 0.0f;
	}
	
	// Update is called once per frame
	void Update () {
        curTime += Time.deltaTime;
        if (curTime > loopTime)
        {
            curTime = 0.0f;
            int ii = 0;
            foreach (Transform p in particles)
            {
                p.position = startPos[ii++];
            }
        }
        else
        {
            int ii = 0;
            foreach (Transform p in particles)
            {
                p.Translate(velocities[ii++] * Time.deltaTime * speed);
            }
        }
       

	}


    /* private methods */
    Vector3 RandomUnitVec3()
    {
        Vector3 v = new Vector3(Random.Range(-1, 1), Random.Range(-1, 1), Random.Range(-1, 1));
        v.Normalize();

        return v;
    }
}
