using UnityEngine;
using System.Collections;

public class CameraScript : MonoBehaviour {
    public Light[] mLights;

    private bool moveLight;
    private bool drawMetavoxelGrid;

    // Use this for initialization
	void Start () {
        moveLight = false;
        drawMetavoxelGrid = false;
    }
	
	// Update is called once per frame
	void Update () {
        ProcessInput();        
	}

    void OnPostRender()
    {
        if (drawMetavoxelGrid)
        {
            foreach (Light l in mLights)
            {
                l.GetComponent<MetavoxelGridGenerator>().DrawMetavoxelGrid();
            }
        }
    }

    void OnGUI()
    {
        drawMetavoxelGrid = GUI.Toggle(new Rect(25, 25, 100, 30), drawMetavoxelGrid, "Show mv grid");
    }

    // ---- private methods ----------------
    void ProcessInput()
    {
        if (Input.GetKeyDown(KeyCode.L))
        {
            moveLight = true;
        }
        else if (Input.GetKeyUp(KeyCode.L))
        {
            moveLight = false;
        }
    }
}
