using UnityEngine;
using System.Collections;

public class CubeMotion : MonoBehaviour {
    public float halfT = 5.0f;   //seconds  
    public float speed = 5.0f;
    private float timeSinceStart = 0f;
	// Use this for initialization
	void Start () {
	}
	
	// Update is called once per frame
	void Update () {
       timeSinceStart += Time.deltaTime;
       Vector3 delta = transform.right * Time.deltaTime * speed;

       if (timeSinceStart > halfT)
       {
            transform.position += delta;

            if (timeSinceStart > 2*halfT)
                timeSinceStart = 0f;            
       }
       else
       {
           transform.position -= delta;
       }

        
	    
	}
}
