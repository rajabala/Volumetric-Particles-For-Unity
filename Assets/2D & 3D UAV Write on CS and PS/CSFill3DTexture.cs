using UnityEngine;
using System.Collections;

// This is mostly based on Aras's DX11 UAV CS write sample
// Attach this to any game object with the 
public class CSFill3DTexture : MonoBehaviour {

    public ComputeShader cs;
    public RenderTexture rt;
    public int size = 32;

	// Use this for initialization
	void Start () {
        CreateResources();
	}
	
	// Update is called once per frame
	void Update () {
        if (!SystemInfo.supportsComputeShaders)
            return;

        CreateResources();

        cs.SetVector("g_Params", new Vector4(Time.timeSinceLevelLoad, size, 1/size, 1.0f));
        cs.SetTexture(0, "Result", rt);
        cs.Dispatch(0, size, size, size);
	}

    void CreateResources()
    {
        if (!rt)
        {
            rt = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32);
            rt.useMipMap = false;
            rt.volumeDepth = size;
            rt.isVolume = true;
            rt.enableRandomWrite = true;
            rt.Create();
            renderer.material.SetTexture("_Volume", rt);
        }
    }
}
