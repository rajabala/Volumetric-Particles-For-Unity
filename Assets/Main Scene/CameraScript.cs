using UnityEngine;
using System.Collections;

public class CameraScript : MonoBehaviour {
    public Light[] mLights;
    public float cameraSpeed;

    private bool moveLight;
    private bool drawMetavoxelGrid;
    private MetavoxelManager[] mvMgrs;

    // Use this for initialization
	void Start () {
        moveLight = false;
        drawMetavoxelGrid = false;

        mvMgrs = new MetavoxelManager[mLights.Length];
        int ii = 0;
        foreach (Light l in mLights) {
            mvMgrs[ii++] = l.GetComponentInChildren<MetavoxelManager>();
        }           
    }
	
	// Update is called once per frame
	void Update () {
        ProcessInput();        
	}

    void OnPostRender()
    {
        if (drawMetavoxelGrid)
        {
            foreach (MetavoxelManager mgr in mvMgrs)
                mgr.DrawMetavoxelGrid();
        }
    }

    void OnGUI()
    {
        drawMetavoxelGrid = GUI.Toggle(new Rect(25, 25, 100, 30), drawMetavoxelGrid, "Show mv grid");
    }

    // ---- private methods ----------------
    void ProcessInput()
    {
        if (Input.GetKey(KeyCode.A))
        {
            transform.RotateAround(Vector3.zero, -Vector3.up, Time.deltaTime * cameraSpeed);
        }
        else if (Input.GetKey(KeyCode.D))
        {
            transform.RotateAround(Vector3.zero, Vector3.up, Time.deltaTime * cameraSpeed);
        }
        if (Input.GetKey(KeyCode.W))
        {
            transform.RotateAround(Vector3.zero, -Vector3.right, Time.deltaTime * cameraSpeed);
        }
        else if (Input.GetKey(KeyCode.S))
        {
            transform.RotateAround(Vector3.zero, Vector3.right, Time.deltaTime * cameraSpeed);
        }

    }
}
