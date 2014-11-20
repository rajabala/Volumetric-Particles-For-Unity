using UnityEngine;
using System.Collections;

public class CameraScript : MonoBehaviour {
    public Light[] mLights;

    // Camera look/move state
    public float lookSpeed, moveSpeed;
    private float rotationX, rotationY;
    private bool moveCamera, moveLight;
    private Vector3 startPos; private Quaternion startRot;

    // Render targets & material to blend them
    public Material matBlendParticles;
    public RenderTexture mainSceneRT;
    public RenderTexture particlesRT;
 
    private bool drawMetavoxelGrid;
    private bool rayMarchVoxels;
    private MetavoxelManager[] mvMgrs;

    // Use this for initialization
	void Start () {
        rotationX = rotationY = 0.0f;
        startPos = transform.position;
        startRot = transform.rotation;
        moveCamera = false; moveLight = false;

        drawMetavoxelGrid = true;
        rayMarchVoxels = true;

        mvMgrs = new MetavoxelManager[mLights.Length];
        int ii = 0;
        foreach (Light l in mLights) {
            mvMgrs[ii++] = l.GetComponentInChildren<MetavoxelManager>();
        }

        CreateResources();

        camera.depthTextureMode = DepthTextureMode.Depth; // this makes the depth buffer available for all the shaders as _CameraDepthTexture
        // [perf threat] Unity is going to do a Z-prepass simply because of this line. 
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
        // Since we don't directly render to the back buffer, we need to clear the render targets used every frame.
        RenderTexture.active = particlesRT;
        GL.Clear(false, true, new Color(0f,0f,0f,0f));

        RenderTexture.active = mainSceneRT;
        GL.Clear(true, true, Color.black);
        camera.targetTexture = mainSceneRT;
    }

    //// OnPostRender is called after a camera has finished rendering the scene.
    void OnPostRender()
    {
        // Use the camera's existing depth buffer to depth-test the particles, while
        // writing the ray marched volume into a separate color buffer that's blended
        // with the main scene in OnRenderImage(..)
        Graphics.SetRenderTarget(particlesRT.colorBuffer, mainSceneRT.depthBuffer);

        if (rayMarchVoxels)
        {
            // fill particlesRT with the ray marched volume (the loop is per directional light source)
            foreach (MetavoxelManager mgr in mvMgrs)
                mgr.RenderMetavoxels();
        }

        // blend the particles onto the main (opaque) scene. [todo] what happens to billboarded particles on the main scene? when're they rendered?
        Graphics.Blit(particlesRT, mainSceneRT, matBlendParticles);

        if (drawMetavoxelGrid)
        {
            foreach (MetavoxelManager mgr in mvMgrs)
                mgr.DrawMetavoxelGrid();
        }

        // need to set the targetTexture to null, else the Blit doesn't work
        camera.targetTexture = null;
        Graphics.Blit(mainSceneRT, null as RenderTexture); // copy to back buffer
    }

    void OnGUI()
    {
        drawMetavoxelGrid = GUI.Toggle(new Rect(25, 25, 100, 30), drawMetavoxelGrid, "Show mv grid");
        rayMarchVoxels = GUI.Toggle(new Rect(25, 75, 150, 30), rayMarchVoxels, "Ray march voxels");
    }

    // ---- private methods ----------------
    void CreateResources()
    {
        if (!particlesRT)
        {
            particlesRT = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
            particlesRT.useMipMap = false;
            particlesRT.isVolume = false;
            particlesRT.enableRandomWrite = false;
            particlesRT.Create();
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
        if (moveCamera)
        {
            rotationX += Input.GetAxis("Mouse X") * lookSpeed;
            rotationY += Input.GetAxis("Mouse Y") * lookSpeed;
            rotationY = Mathf.Clamp(rotationY, -90, 90);

            transform.localRotation = Quaternion.AngleAxis(rotationX, -Vector3.up);
            transform.localRotation *= Quaternion.AngleAxis(rotationY, Vector3.right);

            transform.position += transform.forward * Input.GetAxis("Vertical") * moveSpeed;
            transform.position += transform.right * Input.GetAxis("Horizontal") * moveSpeed;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            transform.position = startPos;
            transform.rotation = startRot;
            rotationX = rotationY = 0.0f;
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            moveCamera = !moveCamera;
        }


    }
}
