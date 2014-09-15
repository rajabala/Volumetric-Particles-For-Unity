using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public struct MetaVoxel
{
    public Vector3 mPos;
    public Quaternion mRot;
    public bool mCovered;
    public List<GameObject> mParticlesCovered;
}

public class MetavoxelGridGenerator : MonoBehaviour {
    public int mvSizeX, mvSizeY, mvSizeZ; // world units
    public int numMetavoxelsX, numMetavoxelsY, numMetavoxelsZ; // # metavoxels in the grid along x, y & z
    public int numVoxelsInMetavoxel; // affects the size of the 3D texture used to fill a metavoxel
    public GameObject[] particles;
    public Material matFillVolume;
    public GameObject testCube;
    public GameObject testPlane;

    public Material mvLineColor;
 
    private MetaVoxel[,,] mvGrid;
    public  RenderTexture rtCam; // bind this as RT for this cam. we don't sample/use it though..
    private RenderTexture[, ,] mvFillTextures; 
    public RenderTexture lightPropogationUAV;

	// Use this for initialization
	void Start () {        
        //float near = Camera.main.nearClipPlane, far = Camera.main.farClipPlane;
        //mvGridZ = (int)((far - near) / mvSizeZ);
               
        CreateResources();

        // Don't do unnecessary work on the camera. We don't use rtCam.
        this.camera.clearFlags = CameraClearFlags.Nothing;
        this.camera.cullingMask = 0;
	}
	

    void CreateResources()
    {
        if (!rtCam)
        {
            rtCam = new RenderTexture(numVoxelsInMetavoxel, numVoxelsInMetavoxel, 0, RenderTextureFormat.ARGB32);
            rtCam.useMipMap = false;
            rtCam.isVolume = false;
            rtCam.enableRandomWrite = false;
            rtCam.Create();
            if (rtCam.IsCreated())
                camera.targetTexture = rtCam;
        }

        CreateMetavoxelGrid();
        CreateLightPropogationUAV();  
    }


	// Update is called once per frame
	void Update () {
        //FillMetavoxels();

        //RenderMetavoxels(); // [todo]
	}


    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        FillMetavoxels(src, dst);
    }


    // ----------------------  private methods --------------------------------
    // Create mv info & associated fill texture for every mv in the grid
    void CreateMetavoxelGrid()
    {
        mvGrid = new MetaVoxel[numMetavoxelsZ, numMetavoxelsY, numMetavoxelsX]; // note index order -- prefer locality in X,Y
        mvFillTextures = new RenderTexture[numMetavoxelsZ, numMetavoxelsY, numMetavoxelsX];

        for (int zz = 0; zz < numMetavoxelsZ; zz++)
        {
            for (int yy = 0; yy < numMetavoxelsY; yy++)
            {
                for (int xx = 0; xx < numMetavoxelsX; xx++)
                {
                    mvGrid[zz, yy, xx].mParticlesCovered = new List<GameObject>();
                    UpdateMetavoxel(xx, yy, zz);
                    CreateFillTexture(xx, yy, zz);
                }
            }
        }
    }


    void CreateFillTexture(int xx, int yy, int zz)
    {
        // Note that constructing a RenderTexture object does not create the hardware representation immediately. The actual render texture is created upon first use or when Create is called manually
        // file:///C:/Program%20Files%20(x86)/Unity/Editor/Data/Documentation/html/en/ScriptReference/RenderTexture-ctor.html
        mvFillTextures[zz, yy, xx] = new RenderTexture(numVoxelsInMetavoxel, numVoxelsInMetavoxel, 0 /* no need depth surface, just color*/, RenderTextureFormat.ARGB32);
        mvFillTextures[zz, yy, xx].isVolume = true;
        mvFillTextures[zz, yy, xx].volumeDepth = numVoxelsInMetavoxel;
        mvFillTextures[zz, yy, xx].generateMips = false;
        mvFillTextures[zz, yy, xx].enableRandomWrite = true; // use as UAV
    }

    void CreateLightPropogationUAV()
    {
        lightPropogationUAV = new RenderTexture(numMetavoxelsX * numVoxelsInMetavoxel, numMetavoxelsY * numVoxelsInMetavoxel, 0 /* no need depth surface, just color*/, RenderTextureFormat.ARGB32);
        lightPropogationUAV.generateMips = false;
        lightPropogationUAV.enableRandomWrite = true;
        lightPropogationUAV.Create();

        Graphics.SetRenderTarget(lightPropogationUAV);
        GL.Clear(false, true, Color.blue);
    }

    void UpdateMetavoxel(int xx, int yy, int zz)
    {
        /* assumptions:
            i) grid is centered at the world origin
         * ii) effort isn't made to ensure the grid covers the frustum visible from the camera.
         * 
         * the grid "looks" towards the directional light source (i.e., the entire grid is simply rotated by the inverse of the directional light's view matrix)
        */
        Vector3 wsPosBeforeRotation = new Vector3((xx - numMetavoxelsX / 2) * mvSizeX,
                                                  (yy - numMetavoxelsY / 2) * mvSizeY,
                                                  (zz - numMetavoxelsZ / 2) * mvSizeZ);


        Quaternion q = new Quaternion();
        q.SetLookRotation(-transform.forward, transform.up);

        Vector3 wsPosAfterRotation = q * wsPosBeforeRotation;
        mvGrid[zz, yy, xx].mPos = wsPosAfterRotation;
        mvGrid[zz, yy, xx].mRot = q;

        mvGrid[zz, yy, xx].mParticlesCovered.Clear();

        foreach (GameObject p in particles)
        {
            // sphere - cube intersection test
            float dSphereCubeSq = (p.transform.position - wsPosAfterRotation).sqrMagnitude;
            float dMinSq = Mathf.Pow(mvSizeX/2f + p.transform.localScale.x, 2f); // [todo: assuming mv is a cube -- its not.. sides can be diff lengths based on scale]
            float dMaxSq = Mathf.Pow(mvSizeX/Mathf.Sqrt(2) + p.transform.localScale.x, 2f);

            if (dSphereCubeSq <= dMinSq)
            {               
                mvGrid[zz, yy, xx].mParticlesCovered.Add(p);
            }
            else if (dSphereCubeSq > dMaxSq)
            {
                continue; // definitely does not intersect
            }
            else
            {
                // [todo: sphere-cube test]
            }

        }

        if (mvGrid[zz, yy, xx].mParticlesCovered.Count == 0)
            mvGrid[zz, yy, xx].mCovered = false;
        else
            mvGrid[zz, yy, xx].mCovered = true;

    }

 
    // if light moves, update grid orientations
    void UpdateMetavoxelGrid()
    {
        for (int zz = 0; zz < numMetavoxelsZ; zz++)
        {
            for (int yy = 0; yy < numMetavoxelsY; yy++)
            {
                for (int xx = 0; xx < numMetavoxelsX; xx++)
                {
                    UpdateMetavoxel(xx, yy, zz);
                }
            }
        }
    }


    void FillMetavoxels(RenderTexture src, RenderTexture dst)
    {   
        // process the metavoxels in order of Z-slice closest to light to farthest
        for (int zz = 0; zz < numMetavoxelsZ; zz++)
        {
            for (int yy = 0; yy < numMetavoxelsY; yy++)
            {
                for (int xx = 0; xx < numMetavoxelsX; xx++)
                {
                    if (mvGrid[zz, yy, xx].mCovered)
                        FillMetavoxel1(xx, yy, zz, src, dst);
                }
            }
        }

    }


    void FillMetavoxel1(int xx, int yy, int zz, RenderTexture src, RenderTexture dst)
    {
        Graphics.ClearRandomWriteTargets();

        // Note: Calling Create lets you create it up front. Create does nothing if the texture is already created.
        // file:///C:/Program%20Files%20(x86)/Unity/Editor/Data/Documentation/html/en/ScriptReference/RenderTexture.Create.html
        if (!mvFillTextures[zz, yy, xx].IsCreated())    
            mvFillTextures[zz, yy, xx].Create();
        
        // don't need to clear the mv fill texture since we write to every pixel on it (on every depth slice)
        // regardless of whether that pixel is covered or not.
        
        Graphics.SetRenderTarget(rtCam);
        Graphics.SetRandomWriteTarget(1, mvFillTextures[zz, yy, xx]);
        Graphics.SetRandomWriteTarget(2, lightPropogationUAV);

        // Set material state
        matFillVolume.SetPass(0);
        matFillVolume.SetFloat("_NumVoxels", numVoxelsInMetavoxel);
        matFillVolume.SetVector("_MetavoxelIndex", new Vector4(xx, yy, zz, 1.0f));

        Graphics.Blit(src, src, matFillVolume);

        testCube.renderer.material.SetTexture("_Volume", mvFillTextures[zz, yy, xx]);
        testPlane.renderer.material.SetTexture("_Plane", lightPropogationUAV);     

        Graphics.ClearRandomWriteTargets();
    }

    // The metavoxel is filled from the pov of the light. 
    // [todo] use orthographic proj for the light's view matrix
    void FillMetavoxel(int xx, int yy, int zz)
    {
        Graphics.ClearRandomWriteTargets();
        
        // Note: Calling Create lets you create it up front. Create does nothing if the texture is already created.
        // file:///C:/Program%20Files%20(x86)/Unity/Editor/Data/Documentation/html/en/ScriptReference/RenderTexture.Create.html
        mvFillTextures[zz, yy, xx].Create();
        
        Graphics.SetRandomWriteTarget(1, mvFillTextures[zz, yy, xx]);
        Graphics.SetRandomWriteTarget(2, lightPropogationUAV);

        Graphics.Blit(lightPropogationUAV, matFillVolume);

        matFillVolume.SetPass(0); // activate first pass in shader associated with this material
        //matFillVolume.SetTexture("Displaced sphere", )

        Matrix4x4 mvToWorld = new Matrix4x4();
        mvToWorld.SetTRS(mvGrid[zz, yy, xx].mPos, 
                         mvGrid[zz, yy, xx].mRot,
                         new Vector3(mvSizeX, mvSizeY, mvSizeZ));

        matFillVolume.SetMatrix("_ModelToWorld", mvToWorld);
        matFillVolume.SetMatrix("_WorldToLight", transform.worldToLocalMatrix);
        matFillVolume.SetFloat("_NumVoxels", numVoxelsInMetavoxel);
        matFillVolume.SetVector("_ScreenRes", new Vector4(Screen.width, Screen.height, 1.0f, 1.0f));


        //Vector3[] vertices = new Vector3[]{new Vector3(-1, -1, 0),
        //                                   new Vector3(-1,  1, 0),
        //                                   new Vector3( 1,  1, 0),
        //                                   new Vector3(-1,  1, 0)};

        float xClipForQuad = numVoxelsInMetavoxel/Screen.width,
              yClipForQuad = numVoxelsInMetavoxel/Screen.height;

        Vector3[] vertices = new Vector3[]{new Vector3(0, 0, 0),
                                           new Vector3(xClipForQuad, 0, 0),
                                           new Vector3(xClipForQuad, yClipForQuad, 0),
                                           new Vector3(0, yClipForQuad, 0)};

        int[] triangleIndices = new int[] {0, 1, 2, 0, 2, 3};
        
        Mesh quad = new Mesh();
        quad.vertices = vertices;
        quad.triangles = triangleIndices;

        Graphics.DrawMesh(quad, Vector3.zero, Quaternion.identity);

        Destroy(quad);

        Debug.Log("Done filling " + xx + ", " + yy + ", " + zz);

        testCube.renderer.material.SetTexture("_Volume", mvFillTextures[zz, yy, xx]);
        testPlane.renderer.material.SetTexture("_Plane", lightPropogationUAV);     

//        SaveFillTexture(xx, yy, zz);
    }

    void SaveFillTexture(int xx, int yy, int zz)
    {
       // Color[] pixels = mvFillTextures[zz, yy, xx].get
        

        //for (int slice = 0; slice < numVoxelsInMetavoxel; slice ++) {
        //    Texture2D tmp = new Texture2D(numVoxelsInMetavoxel, numVoxelsInMetavoxel, TextureFormat.ARGB32, false);


        //}

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = lightPropogationUAV;

        Texture2D tmp = new Texture2D(numMetavoxelsX * numVoxelsInMetavoxel, numMetavoxelsY * numVoxelsInMetavoxel, TextureFormat.ARGB32, false);
        tmp.ReadPixels(new Rect(0, 0, numMetavoxelsX * numVoxelsInMetavoxel, numMetavoxelsY * numVoxelsInMetavoxel), 0, 0);

        byte[] bytes = tmp.EncodeToPNG();
        System.IO.File.WriteAllBytes("Assets/TestUAVWrite/lightUav" + xx + " _" + yy + "_" + zz + ".png", bytes);

        Destroy(tmp);

        RenderTexture.active = prev;
    }

    void RenderMetavoxels()
    {

    }


    // called by MainCamera's OnPostRender()
    public void DrawMetavoxelGrid()
    {
        for (int zz = 0; zz < numMetavoxelsZ; zz++)
        {
            for (int yy = 0; yy < numMetavoxelsY; yy++)
            {
                for (int xx = 0; xx < numMetavoxelsX; xx++)
                {
                    DrawMetavoxel(xx, yy, zz);
                }
            }
        }
    }

    void DrawMetavoxel(int xx, int yy, int zz)
    {
        List<Vector3> points = new List<Vector3>();

        mvLineColor.SetPass(0);
        Vector3 mvPos = mvGrid[zz, yy, xx].mPos;

        Quaternion q = new Quaternion();
        q.SetLookRotation(-transform.forward, transform.up);

        // back, front --> Z ; top, bot --> Y ; left, right --> X
        Vector3 offBotLeftBack = q * new Vector3(-mvSizeX * 0.5f, -mvSizeY * 0.5f, -mvSizeZ * 0.5f),
                offBotLeftFront = q * new Vector3(-mvSizeX * 0.5f, -mvSizeY * 0.5f, mvSizeZ * 0.5f),
                offTopLeftBack = q * new Vector3(-mvSizeX * 0.5f, mvSizeY * 0.5f, -mvSizeZ * 0.5f),
                offTopLeftFront = q * new Vector3(-mvSizeX * 0.5f, mvSizeY * 0.5f, mvSizeZ * 0.5f),
                offBotRightBack = q * new Vector3(mvSizeX * 0.5f, -mvSizeY * 0.5f, -mvSizeZ * 0.5f),
                offBotRightFront = q * new Vector3(mvSizeX * 0.5f, -mvSizeY * 0.5f, mvSizeZ * 0.5f),
                offTopRightBack = q * new Vector3(mvSizeX * 0.5f, mvSizeY * 0.5f, -mvSizeZ * 0.5f),
                offTopRightFront = q * new Vector3(mvSizeX * 0.5f, mvSizeY * 0.5f, mvSizeZ * 0.5f);

        // left 
        points.Add(mvPos + offBotLeftBack);
        points.Add(mvPos + offBotLeftFront);
        points.Add(mvPos + offBotLeftFront);
        points.Add(mvPos + offTopLeftFront);
        points.Add(mvPos + offTopLeftFront);
        points.Add(mvPos + offTopLeftBack);
        points.Add(mvPos + offTopLeftBack);
        points.Add(mvPos + offBotLeftBack);
        // right
        points.Add(mvPos + offBotRightBack);
        points.Add(mvPos + offBotRightFront);
        points.Add(mvPos + offBotRightFront);
        points.Add(mvPos + offTopRightFront);
        points.Add(mvPos + offTopRightFront);
        points.Add(mvPos + offTopRightBack);
        points.Add(mvPos + offTopRightBack);
        points.Add(mvPos + offBotRightBack);
        // join left and right
        points.Add(mvPos + offTopLeftBack);
        points.Add(mvPos + offTopRightBack);
        points.Add(mvPos + offTopLeftFront);
        points.Add(mvPos + offTopRightFront);

        points.Add(mvPos + offBotLeftBack);
        points.Add(mvPos + offBotRightBack);
        points.Add(mvPos + offBotLeftFront);
        points.Add(mvPos + offBotRightFront);

        GL.Begin(GL.LINES);
        foreach (Vector3 v in points)
        {
            GL.Vertex3(v.x, v.y, v.z);
        }
        GL.End();


        //GameObject mv = GameObject.CreatePrimitive(PrimitiveType.Cube);
        //mv.transform.position = mvGrid[zz, yy, xx].mPos;
        //mv.transform.rotation = mvGrid[zz, yy, xx].mRot;
        //mv.transform.localScale = new Vector3(mvSizeX, mvSizeY, mvSizeZ);
    }



}
