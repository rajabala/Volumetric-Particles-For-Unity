using UnityEngine;
using System.Collections;

public class CameraScript : MonoBehaviour {
    public Light[] mLights;
    public float cameraSpeed;
    public Material matDepthBlend;
    public RenderTexture mainSceneRT;
    public RenderTexture rayMarchRT;
    public Camera theCam;
    public float camMoveSpeed;

    private bool moveLight;
    private bool drawMetavoxelGrid;
    private bool rayMarchVoxels;
    private MetavoxelManager[] mvMgrs;

    // Use this for initialization
	void Start () {
        moveLight = false;
        drawMetavoxelGrid = true;
        rayMarchVoxels = true;

        mvMgrs = new MetavoxelManager[mLights.Length];
        int ii = 0;
        foreach (Light l in mLights) {
            mvMgrs[ii++] = l.GetComponentInChildren<MetavoxelManager>();
        }

        CreateResources();
        camera.depthTextureMode = DepthTextureMode.Depth;
    }
	
	// Update is called once per frame
	void Update () {
        ProcessInput();        
	}

    //Order of event calls in unity: file:///C:/Program%20Files%20(x86)/Unity/Editor/Data/Documentation/html/en/Manual/ExecutionOrder.html
    //[Render objects in scene] -> [OnRenderObject] -> [OnPostRender] -> [OnRenderImage]
     
    //OnRenderObject is called after camera has rendered the scene.
    //This can be used to render your own objects using Graphics.DrawMeshNow or other functions.
    //This function is similar to OnPostRender, except OnRenderObject is called on any object that has a script with the function; no matter if it's attached to a Camera or not.
    //void OnRenderObject()
    //{

    //}

    void OnPreRender()
    {
        RenderTexture.active = rayMarchRT;
        GL.Clear(false, true, new Color(0f,0f,0f,0f));

        //RenderTexture.active = mainSceneRT;
        //GL.Clear(true, true, Color.black);
        camera.targetTexture = mainSceneRT;
    }

    //// OnPostRender is called after a camera has finished rendering the scene.
    void OnPostRender()
    {
        // Use the camera's existing depth buffer to depth-test the particles, while
        // writing the ray marched volume into a separate color buffer that's blended
        // with the main scene in OnRenderImage(..)

        Graphics.SetRenderTarget(rayMarchRT.colorBuffer, mainSceneRT.depthBuffer);

        if (rayMarchVoxels)
        {
            foreach (MetavoxelManager mgr in mvMgrs)
                mgr.RenderMetavoxels();
        }

        Graphics.Blit(rayMarchRT, mainSceneRT, matDepthBlend);

        if (drawMetavoxelGrid)
        {
            foreach (MetavoxelManager mgr in mvMgrs)
                mgr.DrawMetavoxelGrid();
        }

        camera.targetTexture = null;
        Graphics.Blit(mainSceneRT, null as RenderTexture);
    }

    //void OnRenderImage(RenderTexture src, RenderTexture dst)
    //{

    //}

    void OnGUI()
    {
        drawMetavoxelGrid = GUI.Toggle(new Rect(25, 25, 100, 30), drawMetavoxelGrid, "Show mv grid");
        rayMarchVoxels = GUI.Toggle(new Rect(25, 75, 150, 30), rayMarchVoxels, "Ray march voxels");
    }

    // ---- private methods ----------------
    void CreateResources()
    {
        if (!rayMarchRT)
        {
            rayMarchRT = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
            rayMarchRT.useMipMap = false;
            rayMarchRT.isVolume = false;
            rayMarchRT.enableRandomWrite = false;
            rayMarchRT.Create();
        }

        if (!mainSceneRT)
        {
            mainSceneRT = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
            mainSceneRT.useMipMap = false;
            mainSceneRT.isVolume = false;
            mainSceneRT.enableRandomWrite = false;
            mainSceneRT.Create();
            camera.targetTexture = mainSceneRT;
        }
    }



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

        //if (Input.GetMouseButtonDown(0))
        //{
        //    transform.Translate(transform.forward * Time.deltaTime * camMoveSpeed);
        //}
        //else if (Input.GetMouseButtonDown(1))
        //{
        //    transform.Translate(-transform.forward * Time.deltaTime * camMoveSpeed);
        //}
     

    }
}
