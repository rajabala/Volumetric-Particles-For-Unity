using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using MetavoxelEngine;

/*
 * Attach this script to the main camera of your scene
 * 
 */

public class CameraScript : MonoBehaviour {       
    /*
     *  public variables to be edited in the inspector window
     */
    public MetavoxelManager mvMgr;
    public Light[] mLights;
    public float lookSpeed, moveSpeed;
    public GUIText controlsText, beingMovedText;
    public Material matBlendParticles; // material to blend the raymarched volume with the camera's RT

    /*
     * private data 
     */
    // Render targets
    private RenderTexture mainSceneRT; // camera draws the scene but for the particles into this
    private RenderTexture particlesRT; // result of raymarching the metavoxels is stored in this

    // Scene controls (GUI elements, toggle controls, movement)
    private bool moveCamera, moveLight;
    private bool drawMetavoxelGrid;    
    private float camRotationX, camRotationY, lightRotationX, lightRotationY;
    private Vector3 startPos; private Quaternion startRot;

    // Use this for initialization
	void Start () {
        camRotationX = camRotationY = 0.0f;
        startPos = transform.position;
        startRot = transform.rotation;
        moveCamera = moveLight = false;

        drawMetavoxelGrid = false;

        CreateResources();
        controlsText.pixelOffset = new Vector2(Screen.width / 3, 0);

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
        // Fill the metavoxels



        // Use the camera's existing depth buffer to depth-test the particles, while
        // writing the ray marched volume into a separate color buffer that's blended
        // with the main scene in OnRenderImage(..)
        Graphics.SetRenderTarget(particlesRT.colorBuffer, mainSceneRT.depthBuffer);

        // fill particlesRT with the ray marched volume (the loop is per directional light source)
        mvMgr.RenderMetavoxels();

        // blend the particles onto the main (opaque) scene. [todo] what happens to billboarded particles on the main scene? when're they rendered?
        Graphics.Blit(particlesRT, mainSceneRT, matBlendParticles);

        if (drawMetavoxelGrid)
        {
            mvMgr.DrawMetavoxelGrid();
        }

        // need to set the targetTexture to null, else the Blit doesn't work
        camera.targetTexture = null;
        Graphics.Blit(mainSceneRT, null as RenderTexture); // copy to back buffer
    }

    void OnGUI()
    {
        drawMetavoxelGrid   = GUI.Toggle(new Rect(25, 25, 100, 30), drawMetavoxelGrid, "Show mv grid");        
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
            // fps'ish free cam
            camRotationX += Input.GetAxis("Mouse X") * lookSpeed;
            camRotationY += Input.GetAxis("Mouse Y") * lookSpeed;
            camRotationY = Mathf.Clamp(camRotationY, -90, 90); // can't do a backflip of course.

            // Unity's screen space coordinate convention has the origin on the bottom left
            // Using the camera's up and right vector can lose one degree of freedom and cause the gimbal lock!
            transform.localRotation = Quaternion.AngleAxis(camRotationX,  Vector3.up);
            transform.localRotation *= Quaternion.AngleAxis(camRotationY, -Vector3.right);

            transform.position += transform.forward * Input.GetAxis("Vertical") * moveSpeed;
            transform.position += transform.right * Input.GetAxis("Horizontal") * moveSpeed;

            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (Input.GetKey(KeyCode.LeftShift))
                {
                    // move in -X in local camera space
                    transform.position -= moveSpeed * transform.up;
                }
                else
                    transform.position += moveSpeed * transform.up;
            }
        }
        else if (moveLight)
        {
            // fps'ish free cam
            lightRotationX += Input.GetAxis("Mouse X") * lookSpeed;
            lightRotationY += Input.GetAxis("Mouse Y") * lookSpeed;

            // Unity's screen space coordinate convention has the origin on the bottom left
            // Using the camera's up and right vector can lose one degree of freedom and cause the gimbal lock!
            mLights[0].transform.localRotation = Quaternion.AngleAxis(lightRotationX, Vector3.up);
            mLights[0].transform.localRotation *= Quaternion.AngleAxis(lightRotationY, -Vector3.right);
        }


        if (Input.GetKeyDown(KeyCode.R))
        {
            transform.position = startPos;
            transform.rotation = startRot;
            camRotationX = camRotationY = 0.0f;
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            moveCamera = !moveCamera;
            if (moveCamera)
            {
                moveLight = false;
                beingMovedText.text = "Moving camera..";
            }
        }

        if (Input.GetKeyDown(KeyCode.L))
        {
            moveLight = !moveLight;
            if (moveLight)
            {
                moveCamera = false;
                beingMovedText.text = "Moving light..";
            }
        }

        if (!moveCamera && !moveLight)
            beingMovedText.text = "";

        if (Input.GetKeyDown(KeyCode.H))
        {
            controlsText.gameObject.SetActive(!controlsText.gameObject.activeSelf);
        }


    }
}
