using UnityEngine;
using System.Collections;

// Attach this script to a secondary camera
public class PSFill2DAnd3DTexture : MonoBehaviour {
    public int size = 128;
    public GameObject psCube; // uses the 3D texture we write to in the PS
    public GameObject psPlane; // uses the 2D texture we write to in the PS
    public Material matWriteTextureOnPS; // shader that writes to both 3D and 2D UAVs
    public RenderTexture rtVol; // 3D UAV
    public RenderTexture rtPlane; // 2D UAV
    
    private RenderTexture rtCam; // camera renders to this RT (PS uses discard though, so nothing is actually rendered)
    
	// Use this for initialization
	void Start () {
        CreateResources();

        // Don't do unnecessary work on the camera. We don't use rtCam.
        this.camera.clearFlags = CameraClearFlags.Nothing;
        this.camera.cullingMask = 0;
    }
	
	// Update is called once per frame
	void Update () {
        
	}

    // All we care about is scheduling PS work to write into the 3D and 2D UAVs.
    // rtCam's size is chosen based on the amount of work that needs to be scheduled.
    // Graphics.Blit submits a full-screen quad for work.
    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        Graphics.SetRenderTarget(rtCam);
        Graphics.ClearRandomWriteTargets();

        Graphics.SetRandomWriteTarget(1, rtVol);
        Graphics.SetRandomWriteTarget(2 , rtPlane);

        matWriteTextureOnPS.SetFloat("_volDepth", size);
        matWriteTextureOnPS.SetFloat("_time", Time.timeSinceLevelLoad);
        Graphics.Blit(src, src, matWriteTextureOnPS);

        Graphics.ClearRandomWriteTargets();               
    }

    void CreateResources()
    {
        if (!rtVol)
        {
            rtVol = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32);
            rtVol.useMipMap = false;
            rtVol.volumeDepth = size;
            rtVol.isVolume = true;
            rtVol.enableRandomWrite = true;
            rtVol.Create();
            if (rtVol.IsCreated())
                psCube.renderer.material.SetTexture("_Volume", rtVol);
        }

        if (!rtPlane)
        {
            rtPlane = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32);
            rtPlane.useMipMap = false;
            rtPlane.isVolume = false;
            rtPlane.enableRandomWrite = true;
            rtPlane.Create();
            if (rtPlane.IsCreated())
                psPlane.renderer.material.SetTexture("_Plane", rtPlane);
        }

        if (!rtCam)
        {
            rtCam = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32);
            rtCam.useMipMap = false;
            rtCam.isVolume = false;
            rtCam.enableRandomWrite = false;
            rtCam.Create();
            if (rtCam.IsCreated())
                camera.targetTexture = rtCam;
        }
    }
}
