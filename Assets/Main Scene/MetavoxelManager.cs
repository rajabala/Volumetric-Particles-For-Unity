using UnityEngine;
#if UNITY_EDITOR 
using UnityEditor;
#endif
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

/*
 * Attach this script to a (dummy) camera on a directional light source.
 * 
 * 
 * 
 * 
 * 
 */




// The world is divided into metavoxels, each of which is made up of voxels.
// This metavoxel grid is oriented to face the light and each metavoxel has a list of
// the particles it covers
public struct MetaVoxel
{
    public Vector3 mPos;
    public Quaternion mRot;
    public List<ParticleSystem.Particle> mParticlesCovered;
}

public struct TmpParticle
{
    public Vector3 pos;
    public float radius;
}

// When "filling a metavoxel" using the "Fill Volume" shader, we send per-particle-info
// for use in the voxel-particle coverage test
public struct DisplacedParticle
{
    public Matrix4x4 mWorldToLocal;
    public Vector3 mWorldPos;
    public float mRadius; // world units
    public float mOpacity;
}


// Compare used for sorting metavoxels of a z-slice (w.r.t the light) from farthest-to-nearest from the camera
public class MvDistComparer : IComparer<Vector3>
{
    public int Compare(Vector3 x, Vector3 y)
    {
        if (x.z > y.z)
            return 1;
       
        return -1;
    }
}


[RequireComponent(typeof(Camera))] // needs to be attached to a game object that has a camera component

// Class that creates, updates, fills and renders a sparse metavoxel grid
// Attach this to the camera child of a directional light source
public class MetavoxelManager : MonoBehaviour {
    public Vector3 mvScale;// world units
    public int numMetavoxelsX, numMetavoxelsY, numMetavoxelsZ; // # metavoxels in the grid along x, y & z
    public int numVoxelsInMetavoxel; // affects the size of the 3D texture used to fill a metavoxel
    public int updateInterval;
    public int rayMarchSteps;
    public int numBorderVoxels; // per end (i.e. a value of 1 means 2 voxels per dimension are border voxels)
    public float opacityFactor;
    public Vector3 ambientColor;
    public bool fadeOutParticles;
    public int volumeTextureAnisoLevel; 

    public Material matFillVolume;
    public Material matRayMarchOver;
	public Material matRayMarchUnder;

    public GameObject theParticleSystem;
    public GameObject testLightPlane;

    public Material mvLineColor;
    
    // MV Grid state
    private MetaVoxel[,,] mvGrid;
    private Vector3 mvScaleWithBorder;

    // Resources
    private  RenderTexture rtCam; // bind this as RT for this cam. we don't sample/use it though..
    private RenderTexture[, ,] mvFillTextures; // 3D textures that hold metavoxel fill data
    private RenderTexture lightPropogationUAV; // Light propogation texture used while filling metavoxels

    private AABBForParticles pBounds;
    private ParticleSystem ps;
    private int numParticlesEmitted;

    // Light movement detection
    private Quaternion lastLightRot;
    private Light dirLight;

    // GUI controls
    private bool showMetavoxelCoverage;
    private float displacementScale;

    // temp prototyping stuff
    private Mesh cubeMesh;
    private Mesh mesh;
    public Vector3[] cubeVertices;
    public Vector2[] cubeUVs;

	
	void Start () {
        CreateResources();

        mvScaleWithBorder = mvScale * numVoxelsInMetavoxel / (numVoxelsInMetavoxel - 2 * numBorderVoxels);
        pBounds = theParticleSystem.GetComponent<AABBForParticles>();
        ps = theParticleSystem.GetComponent<ParticleSystem>();
        

        // Don't do unnecessary work on the camera. We don't use rtCam.
        this.camera.clearFlags = CameraClearFlags.Nothing;
        this.camera.cullingMask = 0;

        CreateTempResources();
        showMetavoxelCoverage = false;
        displacementScale = 1.0f;
        fadeOutParticles = true;

        lastLightRot = transform.rotation;
        dirLight = transform.parent.GetComponent<Light>();
        if (dirLight == null)
            Debug.LogError("script must be attached a to camera that has a dir light parent");
	}
	



	// Update is called once per frame
	void Update () {

	}


    // OnRenderImage is called after all rendering is complete to render image.
    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (Time.frameCount % updateInterval == 0)
        {
            if (transform.rotation != lastLightRot)
            {
                UpdateMetavoxelPositions();
                lastLightRot = transform.rotation;
            }

            UpdateMetavoxelParticleCoverage();
            FillMetavoxels(src, dst);
        }
        
    }


    void OnGUI()
    {
        showMetavoxelCoverage = GUI.Toggle(new Rect(25, 50, 200, 30), showMetavoxelCoverage, "Show metavoxel coverage");
        GUI.Label(new Rect(25, 100, 150, 50), "Displacement Scale [" + displacementScale + "]");
        displacementScale = GUI.HorizontalSlider(new Rect(175, 105, 100, 30), displacementScale, 0.0f, 1.0f);
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

        cubeMesh = new Mesh();
        cubeMesh.vertices = cubeVertices;
        cubeMesh.uv = cubeUVs;
        cubeMesh.triangles = new int[] {// back
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

        GenerateBoxMesh();

    }


    void GenerateBoxMesh()
    {
        mesh = new Mesh();

        float length = 1f;
        float width = 1f;
        float height = 1f;

        #region Vertices
        Vector3 p0 = new Vector3(-length * .5f, -width * .5f, height * .5f);
        Vector3 p1 = new Vector3(length * .5f, -width * .5f, height * .5f);
        Vector3 p2 = new Vector3(length * .5f, -width * .5f, -height * .5f);
        Vector3 p3 = new Vector3(-length * .5f, -width * .5f, -height * .5f);

        Vector3 p4 = new Vector3(-length * .5f, width * .5f, height * .5f);
        Vector3 p5 = new Vector3(length * .5f, width * .5f, height * .5f);
        Vector3 p6 = new Vector3(length * .5f, width * .5f, -height * .5f);
        Vector3 p7 = new Vector3(-length * .5f, width * .5f, -height * .5f);

        Vector3[] vertices = new Vector3[]
{
	// Bottom
	p0, p1, p2, p3,
 
	// Left
	p7, p4, p0, p3,
 
	// Front
	p4, p5, p1, p0,
 
	// Back
	p6, p7, p3, p2,
 
	// Right
	p5, p6, p2, p1,
 
	// Top
	p7, p6, p5, p4
};
        #endregion

        #region Normales
        Vector3 up = Vector3.up;
        Vector3 down = Vector3.down;
        Vector3 front = Vector3.forward;
        Vector3 back = Vector3.back;
        Vector3 left = Vector3.left;
        Vector3 right = Vector3.right;

        Vector3[] normales = new Vector3[]
{
	// Bottom
	down, down, down, down,
 
	// Left
	left, left, left, left,
 
	// Front
	front, front, front, front,
 
	// Back
	back, back, back, back,
 
	// Right
	right, right, right, right,
 
	// Top
	up, up, up, up
};
        #endregion

        #region UVs
        Vector2 _00 = new Vector2(0f, 0f);
        Vector2 _10 = new Vector2(1f, 0f);
        Vector2 _01 = new Vector2(0f, 1f);
        Vector2 _11 = new Vector2(1f, 1f);

        Vector2[] uvs = new Vector2[]
{
	// Bottom
	_11, _01, _00, _10,
 
	// Left
	_11, _01, _00, _10,
 
	// Front
	_11, _01, _00, _10,
 
	// Back
	_11, _01, _00, _10,
 
	// Right
	_11, _01, _00, _10,
 
	// Top
	_11, _01, _00, _10,
};
        #endregion

        #region Triangles
        int[] triangles = new int[]
{
	// Bottom
	3, 1, 0,
	3, 2, 1,			
 
	// Left
	3 + 4 * 1, 1 + 4 * 1, 0 + 4 * 1,
	3 + 4 * 1, 2 + 4 * 1, 1 + 4 * 1,
 
	// Front
	3 + 4 * 2, 1 + 4 * 2, 0 + 4 * 2,
	3 + 4 * 2, 2 + 4 * 2, 1 + 4 * 2,
 
	// Back
	3 + 4 * 3, 1 + 4 * 3, 0 + 4 * 3,
	3 + 4 * 3, 2 + 4 * 3, 1 + 4 * 3,
 
	// Right
	3 + 4 * 4, 1 + 4 * 4, 0 + 4 * 4,
	3 + 4 * 4, 2 + 4 * 4, 1 + 4 * 4,
 
	// Top
	3 + 4 * 5, 1 + 4 * 5, 0 + 4 * 5,
	3 + 4 * 5, 2 + 4 * 5, 1 + 4 * 5,
 
};
        #endregion

        mesh.vertices = vertices;
        mesh.normals = normales;
        mesh.uv = uvs;
        mesh.triangles = triangles;

        mesh.RecalculateBounds();
        mesh.Optimize();
    }

    // Create mv info & associated fill texture for every mv in the grid
    void CreateMetavoxelGrid()
    {
        mvGrid = new MetaVoxel[numMetavoxelsZ, numMetavoxelsY, numMetavoxelsX]; // note index order -- prefer locality in X,Y
        mvFillTextures = new RenderTexture[numMetavoxelsZ, numMetavoxelsY, numMetavoxelsX];

        // Init fill texture and particle list per metavoxel
        for (int zz = 0; zz < numMetavoxelsZ; zz++)
        {
            for (int yy = 0; yy < numMetavoxelsY; yy++)
            {
                for (int xx = 0; xx < numMetavoxelsX; xx++)
                {
                    mvGrid[zz, yy, xx].mParticlesCovered = new List<ParticleSystem.Particle>();
                    CreateFillTexture(xx, yy, zz);
                }
            }
        }

        // Find metavoxel position & orientation 
        UpdateMetavoxelPositions();
    }


    void CreateFillTexture(int xx, int yy, int zz)
    {
        // Note that constructing a RenderTexture object does not create the hardware representation immediately. The actual render texture is created upon first use or when Create is called manually
        // file:///C:/Program%20Files%20(x86)/Unity/Editor/Data/Documentation/html/en/ScriptReference/RenderTexture-ctor.html
        mvFillTextures[zz, yy, xx] = new RenderTexture(numVoxelsInMetavoxel, numVoxelsInMetavoxel, 0 /* no need depth surface, just color*/, RenderTextureFormat.ARGBFloat);
        mvFillTextures[zz, yy, xx].isVolume = true;
        mvFillTextures[zz, yy, xx].volumeDepth = numVoxelsInMetavoxel;
        mvFillTextures[zz, yy, xx].generateMips = false;
        mvFillTextures[zz, yy, xx].enableRandomWrite = true; // use as UAV
    }

    void CreateLightPropogationUAV()
    {
        lightPropogationUAV = new RenderTexture(numMetavoxelsX * numVoxelsInMetavoxel, numMetavoxelsY * numVoxelsInMetavoxel, 0 /* no need depth surface, just color*/, RenderTextureFormat.RFloat);
        lightPropogationUAV.generateMips = false;
        lightPropogationUAV.enableRandomWrite = true; // use as UAV
        lightPropogationUAV.Create();

        Graphics.SetRenderTarget(lightPropogationUAV);
        GL.Clear(false, true, Color.red);

        testLightPlane.renderer.material.SetTexture("_Plane", lightPropogationUAV);
    }


    void UpdateMetavoxelPositions()
    {
        /* assumptions:
          i) grid is centered at the world origin
       * ii) effort isn't made to ensure the grid covers the frustum visible from the camera.
       * 
       * the grid "looks" towards the directional light source (i.e., the entire grid is simply rotated by the inverse of the directional light's view matrix)
      */

        Vector3 lsWorldOrigin = transform.worldToLocalMatrix.MultiplyPoint3x4(Vector3.zero); // xform origin to light space

        for (int zz = 0; zz < numMetavoxelsZ; zz++)
        {
            for (int yy = 0; yy < numMetavoxelsY; yy++)
            {
                for (int xx = 0; xx < numMetavoxelsX; xx++)
                {
                    Vector3 lsOffset = Vector3.Scale(new Vector3(numMetavoxelsX / 2 - xx, numMetavoxelsY / 2 - yy, numMetavoxelsZ / 2 - zz), mvScale);
                    Vector3 wsMetavoxelPos = transform.localToWorldMatrix.MultiplyPoint3x4(lsWorldOrigin - lsOffset);
                    mvGrid[zz, yy, xx].mPos = wsMetavoxelPos;

                    Quaternion q = new Quaternion();
                    q.SetLookRotation(-transform.forward, transform.up);
                    mvGrid[zz, yy, xx].mRot = q;
                }
            }
        }
    }

    
    void UpdateMetavoxelParticleCoverage()
    {
        ParticleSystem.Particle[] parts = new ParticleSystem.Particle[ps.maxParticles];
        numParticlesEmitted = ps.GetParticles(parts);

        for (int zz = 0; zz < numMetavoxelsZ; zz++)
        {
            for (int yy = 0; yy < numMetavoxelsY; yy++)
            {
                for (int xx = 0; xx < numMetavoxelsX; xx++)
                {
                    mvGrid[zz, yy, xx].mParticlesCovered.Clear();
                    
                    Matrix4x4 worldToMetavoxelMatrix = Matrix4x4.TRS(   mvGrid[zz, yy, xx].mPos, 
                                                                        mvGrid[zz, yy, xx].mRot,
                                                                        mvScaleWithBorder).inverse; // Account for the border of the metavoxel while scaling

                    for (int pp = 0; pp < numParticlesEmitted; pp++)
                    {
                        // xform particle to mv space to make it a sphere-aabb intersection test
                        Vector3 wsParticlePos = theParticleSystem.transform.localToWorldMatrix.MultiplyPoint3x4(parts[pp].position);
                        Vector3 mvParticlePos = worldToMetavoxelMatrix.MultiplyPoint3x4(wsParticlePos);
                        float radius = (parts[pp].size / 2f) / mvScaleWithBorder.x;

                        bool particle_intersects_metavoxel = MathUtil.DoesBoxIntersectSphere(new Vector3(-0.5f, -0.5f, -0.5f),
                                                                                             new Vector3( 0.5f,  0.5f,  0.5f),
                                                                                             mvParticlePos,
                                                                                             radius);

                        if (particle_intersects_metavoxel)                        
                            mvGrid[zz, yy, xx].mParticlesCovered.Add(parts[pp]);                          
                    } // pp

                } // xx
            } // yy
        } // zz       
    }


    void FillMetavoxels(RenderTexture src, RenderTexture dst)
    {
        Graphics.SetRenderTarget(lightPropogationUAV);
        GL.Clear(false, true, Color.red);
        SetFillPassConstants();

        // process the metavoxels in order of Z-slice closest to light to farthest
        for (int zz = 0; zz < numMetavoxelsZ; zz++)
        {
            for (int yy = 0; yy < numMetavoxelsY; yy++)
            {
                for (int xx = 0; xx < numMetavoxelsX; xx++)
                {
                    if (mvGrid[zz, yy, xx].mParticlesCovered.Count != 0)
                        FillMetavoxel(xx, yy, zz, src, dst);
                }
            }
        }

    }


    void SetFillPassConstants()
    {
        matFillVolume.SetFloat("_NumVoxels", numVoxelsInMetavoxel);
        matFillVolume.SetFloat("_InitLightIntensity", 1.0f);
        matFillVolume.SetVector("_LightColor", dirLight.color);
        matFillVolume.SetVector("_MetavoxelGridDim", new Vector3(numMetavoxelsX, numMetavoxelsY, numMetavoxelsZ));
        matFillVolume.SetFloat("_OpacityFactor", opacityFactor);
        matFillVolume.SetFloat("_DisplacementScale", displacementScale);
        matFillVolume.SetVector("_AmbientColor", ambientColor);

        int fadeParticles = 0;
        if (fadeOutParticles)
            fadeParticles = 1;

        matFillVolume.SetInt("_FadeOutParticles", fadeParticles);
    }

    // Each metavoxel that has particles in it needs to be filled with volume info (opacity for now)
    // Since nothing is rendered to the screen while filling the metavoxel volume textures up, we have to resort to

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

        foreach (ParticleSystem.Particle p in mvGrid[zz, yy, xx].mParticlesCovered)
        {
            Vector3 wsPos = theParticleSystem.transform.localToWorldMatrix.MultiplyPoint3x4(p.position); 
            dpArray[index].mWorldToLocal = Matrix4x4.TRS(wsPos, Quaternion.identity, new Vector3(p.size, p.size, p.size)).inverse;
            dpArray[index].mWorldPos = wsPos;
            dpArray[index].mRadius = p.size / 2f;
            dpArray[index].mOpacity = p.lifetime / p.startLifetime; // [time-particle-will-remain-alive / particle-lifetime] use this to make particles less "dense" as they meet their end.
            index++;
        }

        ComputeBuffer dpBuffer = new ComputeBuffer(numParticles,
                                                   Marshal.SizeOf(dpArray[0]));
        dpBuffer.SetData(dpArray);

        // Set material state
        matFillVolume.SetPass(0);
        matFillVolume.SetMatrix("_MetavoxelToWorld", Matrix4x4.TRS( mvGrid[zz, yy, xx].mPos,
                                                                    mvGrid[zz, yy, xx].mRot,
                                                                    mvScaleWithBorder)); // need to fill the border voxels of this metavoxel too (so we need to make it "seem" bigger
        matFillVolume.SetVector("_MetavoxelIndex", new Vector3(xx, yy, zz));
        matFillVolume.SetInt("_NumParticles", numParticles);
        matFillVolume.SetBuffer("_Particles", dpBuffer);

        Graphics.Blit(src, src, matFillVolume);

        // cleanup
        dpBuffer.Release();
        Graphics.ClearRandomWriteTargets();
    }

    // The ray march step uses alpha blending and imposes order requirements 
    List<Vector3> SortMetavoxelSlicesFarToNearFromEye()
    {
        List<Vector3> mvPerSliceFarToNear = new List<Vector3>() ;
        Vector3 cameraPos = Camera.main.transform.position;

        for (int yy = 0; yy < numMetavoxelsY; yy++)
        {
            for (int xx = 0; xx < numMetavoxelsX; xx++)
            {
                Vector3 distFromEye = mvGrid[0, yy, xx].mPos - cameraPos;
                mvPerSliceFarToNear.Add(new Vector3(xx, yy, Vector3.Dot(distFromEye, distFromEye)));
            }
        }

        MvDistComparer mvdc = new MvDistComparer();
        mvPerSliceFarToNear.Sort(mvdc);

        return mvPerSliceFarToNear;
    }

    // Submit a cube from the perspective of the main camera
    // This function is called from CameraScript.cs
    public void RenderMetavoxels()
    {
        List<Vector3> mvPerSliceFarToNear = SortMetavoxelSlicesFarToNearFromEye();
		SetRaymarchPassConstants();

		Vector3 lsCameraPos = transform.worldToLocalMatrix.MultiplyPoint3x4 (Camera.main.transform.position);
        float lsFirstZSlice = transform.worldToLocalMatrix.MultiplyPoint3x4(mvGrid[0, 0, 0].mPos).z;
        int zBoundary = Mathf.Clamp( (int)(lsCameraPos.z - lsFirstZSlice), 0, numMetavoxelsZ - 1);

        matRayMarchOver.EnableKeyword("BLEND_UNDER");
        // Render metavoxel slices to the "left" of the camera in
        // (a) increasing order along the direction of the light
        // (b) farthest-to-nearest from the camera per slice
        for (int zz = 0; zz < zBoundary; zz++)
        {
            foreach (Vector3 vv in mvPerSliceFarToNear)
            {
                int xx = (int)vv.x, yy = (int)vv.y;

                if (mvGrid[zz, yy, xx].mParticlesCovered.Count != 0)
                {
                    RenderMetavoxel(xx, yy, zz, matRayMarchOver);
                }

            }
        }

        // Render metavoxel slices to the "right" of the camera in
        // (a) increasing order along the direction of the light
        // (b) nearest-to-farthest from the camera per slice

        mvPerSliceFarToNear.Reverse(); // make it nearest-to-farthest

        for (int zz = zBoundary; zz < numMetavoxelsZ; zz++)
        {
            foreach (Vector3 vv in mvPerSliceFarToNear)
            {
                int xx = (int)vv.x, yy = (int)vv.y;

                if (mvGrid[zz, yy, xx].mParticlesCovered.Count != 0)
                {
                    RenderMetavoxel(xx, yy, zz, matRayMarchUnder);
                }

            }
        }

    }


	void SetRaymarchPassConstants()
	{
		Material[] over_under = {matRayMarchOver, matRayMarchUnder};

		foreach (Material m in over_under) {
			// Resources
			m.SetTexture("_LightPropogationTexture", lightPropogationUAV);
			
			// Metavoxel grid uniforms
			m.SetFloat("_NumVoxels", numVoxelsInMetavoxel);
			//m.SetVector("_MetavoxelSize", mvScale);
            m.SetInt("_MetavoxelBorderSize", numBorderVoxels);
			
			// Camera uniforms
			m.SetVector("_CameraWorldPos", Camera.main.transform.position);
			// Unity sets the _CameraToWorld and _WorldToCamera constant buffers by default - but these would be on the metavoxel camera
			// that's attached to the directional light. We're interested in the main camera's matrices, not the pseudo-mv cam!
			m.SetMatrix("_CameraToWorldMatrix", Camera.main.cameraToWorldMatrix);
			m.SetMatrix("_WorldToCameraMatrix", Camera.main.worldToCameraMatrix);
			m.SetFloat("_Fov", Camera.main.fieldOfView);
			m.SetFloat("_Near", Camera.main.nearClipPlane);
			m.SetFloat("_Far", Camera.main.farClipPlane);
			m.SetVector("_ScreenRes", new Vector2(Screen.width, Screen.height));
			
			// Ray march uniforms
			m.SetInt("_NumSteps", rayMarchSteps);
			m.SetVector("_AABBMin", pBounds.aabb.min);
			m.SetVector("_AABBMax", pBounds.aabb.max);
			
			int showPrettyColors_i = 0;
			if (showMetavoxelCoverage)
				showPrettyColors_i = 1;
			
			m.SetInt("_ShowPrettyColors", showPrettyColors_i);
		}
	}

    void RenderMetavoxel(int xx, int yy, int zz, Material m)
    {
        bool setPass = m.SetPass(0); // [eureka] should be done for every drawmeshnow call apparently..!
        if (!setPass)
        {
            Debug.LogError("material set pass returned false;..");
        }

        //Debug.Log("rendering mv " + xx + "," + yy +"," + zz);
        mvFillTextures[zz, yy, xx].filterMode = FilterMode.Bilinear;
        mvFillTextures[zz, yy, xx].wrapMode = TextureWrapMode.Clamp;
        mvFillTextures[zz, yy, xx].anisoLevel = volumeTextureAnisoLevel;

        m.SetTexture("_VolumeTexture", mvFillTextures[zz, yy, xx]);
        Matrix4x4 mvToWorld = Matrix4x4.TRS(mvGrid[zz, yy, xx].mPos,
                                            mvGrid[zz, yy, xx].mRot,
                                            mvScale); // border should NOT be included here. we want to rasterize only the pixels covered by the metavoxel
        m.SetMatrix("_MetavoxelToWorld", mvToWorld);
        m.SetMatrix("_WorldToMetavoxel", mvToWorld.inverse);
        m.SetVector("_MetavoxelIndex", new Vector3(xx, yy, zz));
        m.SetFloat("_ParticleCoverageRatio", mvGrid[zz, yy, xx].mParticlesCovered.Count / (float)numParticlesEmitted);

        Graphics.DrawMeshNow(mesh, Vector3.zero, Quaternion.identity);
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
        Quaternion q = mvGrid[zz, yy, xx].mRot;
   

        float halfWidth = mvScale.x * 0.5f, halfHeight = mvScale.y * 0.5f, halfDepth = mvScale.z * 0.5f;
        // back, front --> Z ; top, bot --> Y ; left, right --> X
        Vector3 offBotLeftBack = q * new Vector3(-halfWidth,    -halfHeight, halfDepth),
                offBotLeftFront = q * new Vector3(-halfWidth,   -halfHeight, -halfDepth),
                offTopLeftBack = q * new Vector3(-halfWidth,    halfHeight, halfDepth),
                offTopLeftFront = q * new Vector3(-halfWidth,   halfHeight, -halfDepth),
                offBotRightBack = q * new Vector3(halfWidth,    -halfHeight, halfDepth),
                offBotRightFront = q * new Vector3(halfWidth,   -halfHeight, -halfDepth),
                offTopRightBack = q * new Vector3(halfWidth,    halfHeight, halfDepth),
                offTopRightFront = q * new Vector3(halfWidth,   halfHeight, -halfDepth);

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

    #if UNITY_EDITOR 
    void OnDrawGizmos()
    {
        Gizmos.color = Color.blue;

        for (int zz = 0; zz < numMetavoxelsZ; zz++)
        {
            for (int yy = 0; yy < numMetavoxelsY; yy++)
            {
                for (int xx = 0; xx < numMetavoxelsX; xx++)
                {
                    List<Vector3> points = new List<Vector3>();


                    Vector3 lsWorldOrigin = transform.worldToLocalMatrix.MultiplyPoint3x4(Vector3.zero); // xform origin to light space
                    Vector3 lsOffset = Vector3.Scale(new Vector3(numMetavoxelsX / 2 - xx, numMetavoxelsY / 2 - yy, numMetavoxelsZ / 2 - zz), new Vector3(mvScale.x, mvScale.y, mvScale.z));
                    Vector3 mvPos = transform.localToWorldMatrix.MultiplyPoint3x4(lsWorldOrigin - lsOffset);
                    Quaternion q = new Quaternion();
                    q.SetLookRotation(-transform.forward, transform.up);

                    float halfWidth = mvScale.x * 0.5f, halfHeight = mvScale.y * 0.5f, halfDepth = mvScale.z * 0.5f;
                    // back, front --> Z ; top, bot --> Y ; left, right --> X
                    Vector3 offBotLeftBack = q * new Vector3(-halfWidth, -halfHeight, halfDepth),
                            offBotLeftFront = q * new Vector3(-halfWidth, -halfHeight, -halfDepth),
                            offTopLeftBack = q * new Vector3(-halfWidth, halfHeight, halfDepth),
                            offTopLeftFront = q * new Vector3(-halfWidth, halfHeight, -halfDepth),
                            offBotRightBack = q * new Vector3(halfWidth, -halfHeight, halfDepth),
                            offBotRightFront = q * new Vector3(halfWidth, -halfHeight, -halfDepth),
                            offTopRightBack = q * new Vector3(halfWidth, halfHeight, halfDepth),
                            offTopRightFront = q * new Vector3(halfWidth, halfHeight, -halfDepth);

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

                    for (int ii = 0; ii < points.Count; ii += 2)
                    {
                        Gizmos.DrawLine(points[ii], points[ii + 1]);
                    }
                }
            }
        }
    }
#endif

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