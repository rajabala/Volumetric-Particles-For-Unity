using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// The world is divided into metavoxels, each of which is made up of voxels.
// This metavoxel grid is oriented to face the light and each metavoxel has a list of
// the particles it covers
public struct MetaVoxel
{
    public Vector3 mPos;
    public Quaternion mRot;
    public bool mCovered;
    public List<Transform> mParticlesCovered;
}

// When "filling a metavoxel" using the "Fill Volume" shader, we send per-particle-info
// for use in the voxel-particle coverage test
public struct DisplacedParticle
{
    public Matrix4x4 mWorldToLocal;
    public Vector3 mWorldPos;
    public float mRadius;
    public int mIndex;
}

[RequireComponent(typeof(Camera))] // needs to be attached to a game object that has a camera component

// Class that creates, updates, fills and renders a sparse metavoxel grid
// Attach this to the camera child of a directional light source
public class MetavoxelManager : MonoBehaviour {
    public int mvSizeX, mvSizeY, mvSizeZ; // world units
    public int numMetavoxelsX, numMetavoxelsY, numMetavoxelsZ; // # metavoxels in the grid along x, y & z
    public int numVoxelsInMetavoxel; // affects the size of the 3D texture used to fill a metavoxel
    public int updateInterval;

    public Material matFillVolume;
    public Material matRayMarch;
    
    public GameObject testLightPlane;
    public GameObject testQuad;

    public Material mvLineColor;
    
    // State
    private MetaVoxel[,,] mvGrid;
    private Transform[] particles;

    // Resources
    private RenderTexture lightPropogationUAV;
    private  RenderTexture rtCam; // bind this as RT for this cam. we don't sample/use it though..
    private RenderTexture[, ,] mvFillTextures; 

    // temp prototyping stuff
    private Vector3 mvScale;
    private List<GameObject> mvQuads;
    private GameObject quadDaddy;
    private bool renderQuads;
    private Mesh cubeMesh;
    public Vector3[] cubeVertices;
    public Vector2[] cubeUVs;

	
	void Start () {        
        //float near = Camera.main.nearClipPlane, far = Camera.main.farClipPlane;
        //mvGridZ = (int)((far - near) / mvSizeZ);

        GameObject particleParent = GameObject.Find("Particles");
        particles = new Transform[particleParent.transform.childCount];

        for (int ii = 0; ii < particleParent.transform.childCount; ii++) {
            Transform child = particleParent.transform.GetChild(ii);
            particles[ii] = particleParent.transform.GetChild(ii);
            particles[ii].gameObject.SetActive(false);
        }
        
        CreateResources();

        // Don't do unnecessary work on the camera. We don't use rtCam.
        this.camera.clearFlags = CameraClearFlags.Nothing;
        this.camera.cullingMask = 0;

        CreateTempResources();
        renderQuads = false;

        mvScale = new Vector3(mvSizeX, mvSizeY, mvSizeZ);
	}
	



	// Update is called once per frame
	void Update () {

	}


    // OnRenderImage is called after all rendering is complete to render image.
    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (Time.frameCount % updateInterval == 0)
        {
            UpdateMetavoxelGrid();
            FillMetavoxels(src, dst);

            if (renderQuads)
                CreateMetavoxelQuads();
            else
                DestroyMetavoxelQuads();
        }
        
    }


    void OnGUI()
    {
        renderQuads = GUI.Toggle(new Rect(25, 50, 200, 30), renderQuads, "Render metavoxels as quads");
    }


    // ----------------------  private methods --------------------------------
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


    void CreateTempResources()
    {
        mvQuads = new List<GameObject>();
        quadDaddy = new GameObject("quadDaddy");
        quadDaddy.transform.position = Vector3.zero;
        quadDaddy.transform.rotation = Quaternion.identity;

        cubeMesh = new Mesh();
        cubeMesh.vertices = cubeVertices;
        cubeMesh.uv = cubeUVs;
        cubeMesh.triangles = new int[] {// back (make it ccw?)
                                        3, 2, 1,
                                        3, 1, 0,
                                        // front,
                                        4, 5, 6,
                                        4, 6, 7,
                                        // left
                                        0, 1, 5,
                                        0, 5, 4,
                                        // right
                                        7, 6, 2,
                                        7, 2, 3,
                                        // top
                                        5, 1, 2,
                                        5, 2, 6,
                                        // bot
                                        0, 4, 7, 
                                        0, 7, 3};
    }


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
                    mvGrid[zz, yy, xx].mParticlesCovered = new List<Transform>();
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
        lightPropogationUAV = new RenderTexture(numMetavoxelsX * numVoxelsInMetavoxel, numMetavoxelsY * numVoxelsInMetavoxel, 0 /* no need depth surface, just color*/, RenderTextureFormat.RFloat);
        lightPropogationUAV.generateMips = false;
        lightPropogationUAV.enableRandomWrite = true;
        lightPropogationUAV.Create();

        Graphics.SetRenderTarget(lightPropogationUAV);
        GL.Clear(false, true, Color.red);

        testLightPlane.renderer.material.SetTexture("_Plane", lightPropogationUAV);
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

        foreach (Transform p in particles)
        {
            // sphere - cube intersection test
            float dSphereCubeSq = (p.position - wsPosAfterRotation).sqrMagnitude;
            float dMinSq = Mathf.Pow(mvSizeX/2f + p.localScale.x, 2f); // [todo: assuming mv is a cube -- its not.. sides can be diff lengths based on scale]
            float dMaxSq = Mathf.Pow(mvSizeX/Mathf.Sqrt(2) + p.localScale.x, 2f);

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
                        FillMetavoxel(xx, yy, zz, src, dst);
                }
            }
        }

    }


    // Each metavoxel that has particles in it needs to be filled with volume info (opacity, color, density, temperature...) [todo]
    void FillMetavoxel(int xx, int yy, int zz, RenderTexture src, RenderTexture dst)
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

        // fill structured buffer
        int numParticles = mvGrid[zz, yy, xx].mParticlesCovered.Count;
        DisplacedParticle[] dpArray = new DisplacedParticle[numParticles];

        int index = 0;
        foreach (Transform p in mvGrid[zz, yy, xx].mParticlesCovered)
        {
            dpArray[index].mWorldToLocal = p.worldToLocalMatrix;
            dpArray[index].mWorldPos = p.position;
            dpArray[index].mRadius = p.localScale.x / 2.0f; // sphere, so any dim will do          
            string pi = p.name[1].ToString();
            dpArray[index].mIndex = int.Parse(pi);
            index++;
        }

        //Debug.Log("MV " + xx + yy + zz + " : Particles covered = " + mvGrid[zz, yy, xx].mParticlesCovered.Count);

        ComputeBuffer dpBuffer = new ComputeBuffer(numParticles,
                                                   Marshal.SizeOf(dpArray[0]));
        dpBuffer.SetData(dpArray);

        // Set material state
        matFillVolume.SetPass(0); 
        matFillVolume.SetFloat("_NumVoxels", numVoxelsInMetavoxel);
        matFillVolume.SetInt("_NumParticles", numParticles);
        matFillVolume.SetVector("_MetavoxelIndex", new Vector3(xx, yy, zz));
        matFillVolume.SetVector("_MetavoxelGridDim", new Vector3(numMetavoxelsX, numMetavoxelsY, numMetavoxelsZ));
        matFillVolume.SetBuffer("_Particles", dpBuffer);
        matFillVolume.SetMatrix("_MetavoxelToWorld", Matrix4x4.TRS( mvGrid[zz, yy, xx].mPos, 
                                                                    mvGrid[zz, yy, xx].mRot,
                                                                    mvScale));
        // end test--
        Graphics.Blit(src, src, matFillVolume);

        
        // cleanup
        dpBuffer.Release();
        Graphics.ClearRandomWriteTargets();
    }

    // The ray march step uses alpha blending and imposes order requirements
    void SortMetavoxelSlicesFromEye()
    {

    }

    // Submit a cube from the perspective of the main camera
    // This function is called 
    public void RenderMetavoxels()
    {

       
        for (int zz = 0; zz < numMetavoxelsZ; zz++)
        {
            for (int yy = 0; yy < numMetavoxelsY; yy++)
            {
                for (int xx = 0; xx < numMetavoxelsX; xx++)
                {
                    if (mvGrid[zz, yy, xx].mCovered)
                    {
                        bool setPass = matRayMarch.SetPass(0); // [eureka] should be done for every drawmeshnow call apparently..!
                        if (!setPass)
                        {
                            Debug.LogError("material set pass returned false;..");
                        }

                        matRayMarch.SetTexture("_LightPropogationTexture", lightPropogationUAV);
                        matRayMarch.SetFloat("_NumVoxels", numVoxelsInMetavoxel);

                        //Debug.Log("rendering mv " + xx + "," + yy +"," + zz);
                        matRayMarch.SetTexture("_VolumeTexture", mvFillTextures[zz, yy, xx]);
                        matRayMarch.SetMatrix("_MetavoxelToWorld", Matrix4x4.TRS(mvGrid[zz, yy, xx].mPos, 
                                                                             mvGrid[zz, yy, xx].mRot,
                                                                             mvScale ));

                        matRayMarch.SetMatrix("_WorldToMainCamera", Camera.main.worldToCameraMatrix);           
                        matRayMarch.SetMatrix("_Projection", Camera.main.projectionMatrix);
                        matRayMarch.SetVector("_MetavoxelIndex", new Vector3(xx, yy, zz));
                        
                        Graphics.DrawMeshNow(cubeMesh, Vector3.zero, Quaternion.identity);
                    }

                }
            }
        }
    }

    // Called from Maincamera.OnRenderImage()
    // Cheap hack to check if fill volume is working. It doesn't "render" the quads as such.
    // Only instantiates them. Since this camera doesn't "see" anything (via cullmask), it
    // ends up being rendered by the main camera.
    void CreateMetavoxelQuads()
    {
        DestroyMetavoxelQuads();

        // Draw a bunch of alpha blended quads that're textured with the voxel's 3D fill info
        for (int zz = 0; zz < numMetavoxelsZ; zz++)
        {
            for (int yy = 0; yy < numMetavoxelsY; yy++)
            {
                for (int xx = 0; xx < numMetavoxelsX; xx++)
                {
                    if (mvGrid[zz, yy, xx].mCovered)
                    {
                        for (int slice = 0; slice < numVoxelsInMetavoxel; slice ++)
                        {
                            GameObject quad = GameObject.Instantiate(testQuad) as GameObject;
                            quad.name = "MV_" + xx + yy + zz + "_s" + slice;
                            quad.transform.parent = quadDaddy.transform;
                            quad.renderer.material.SetTexture("_Volume", mvFillTextures[zz, yy, xx]);
                            quad.renderer.material.SetInt("_Slice", slice);
                            quad.renderer.material.SetInt("_NumVoxels", numVoxelsInMetavoxel);

                            quad.transform.rotation = mvGrid[zz, yy, xx].mRot;
                            Vector3 sliceOffset = quad.transform.forward * (slice - numVoxelsInMetavoxel/2)/numVoxelsInMetavoxel;
                            quad.transform.position = mvGrid[zz, yy, xx].mPos + sliceOffset * mvSizeX;
                            quad.transform.localScale = new Vector3(mvSizeX, mvSizeY, 1);
                            mvQuads.Add(quad);
                        }
                    }
                       
                }
            }
        }
    }

    void DestroyMetavoxelQuads()
    {
        foreach (GameObject g in mvQuads)
        {
            Destroy(g);
        }
        mvQuads.Clear();
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

}
