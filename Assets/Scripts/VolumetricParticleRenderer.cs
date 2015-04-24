//
// Permission is granted to use, copy, distribute and prepare derivative works of this
// software for any purpose and without fee, provided, that the above copyright notice
// and this statement appear in all copies.  Intel makes no representations about the
// suitability of this software for any purpose.  THIS SOFTWARE IS PROVIDED "AS IS."
// INTEL SPECIFICALLY DISCLAIMS ALL WARRANTIES, EXPRESS OR IMPLIED, AND ALL LIABILITY,
// INCLUDING CONSEQUENTIAL AND OTHER INDIRECT DAMAGES, FOR THE USE OF THIS SOFTWARE,
// INCLUDING LIABILITY FOR INFRINGEMENT OF ANY PROPRIETARY RIGHTS, AND INCLUDING THE
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE.  Intel does not
// assume any responsibility for any errors which may appear in this software nor any
// responsibility to update it.
//--------------------------------------------------------------------------------------


/******************************************************
 * Attach this script to the main camera in the scene
 ******************************************************/

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MetavoxelEngine
{
    /*
     * Type definitions for metavoxel & particle data
     */

    // Each metavoxel has a list of the particles (and their respective particle systems) it covers
    struct MetaVoxel
    {
        public struct ParticleInfo
        {
            public ParticleSystem.Particle mParticle;
            public ParticleSystem mParent; // particle system corresponding to the particle above
        }
        public Vector3 mPos;
        public List<ParticleInfo> mParticlesCovered;
        public bool mCleared;
    }

    // When filling a metavoxel column using the "Fill Volume" shader, we send per-particle-info
    // for use in the voxel-particle coverage test
    struct SphericalParticle
    {
        public Matrix4x4 mWorldToLocal;
        public Vector3 mWorldPos;
        public float mRadius; // world units
        public float mPercLifeLeft;
    }

    // Helps sort metavoxels of a Z-Slice by distance from the eye
    public struct MetavoxelSortData : IComparable<MetavoxelSortData>
    {
        public int x, y;
        public float distance;

        public MetavoxelSortData(int xx, int yy, float dd)
        {
            x = xx; y = yy; distance = dd;
        }

        public void Print(int index)
        {
            Debug.Log("At index " + index + "(" + x + "," + y + ") at disance " + distance);
        }

        public int CompareTo(MetavoxelSortData other)
        {
            return this.distance.CompareTo(other.distance);
        }
    }


    [RequireComponent(typeof(Camera))]
    public class VolumetricParticleRenderer : MonoBehaviour
    {
        /*
         *  public variables to be edited in the inspector window
         */
        /********************* game objects/components that need to be set *************************/
        public Light dirLight;
        public LayerMask particlesLayer;       
        //public GameObject gridCenter; // control center of the metavoxel grid [FIXME] needs to update based on view frustum

        /********************** metavoxel layout/size **********************************************/
        public int numMetavoxelsX, numMetavoxelsY, numMetavoxelsZ; // # metavoxels in the grid along x, y & z
        public float mvScale;// size of a metavoxel in world units
        public int numVoxelsInMetavoxel; // affects the size of the 3D texture used to fill a metavoxel
        public int numBorderVoxels; // per end (i.e. a value of 1 means 2 voxels per dimension are border voxels)    

        /********************** rendering vars **********************************************/
        public int updateInterval;        
        public int rayMarchSteps;
        public Vector3 ambientColor;        
        public int volumeTextureAnisoLevel;

        /*********************** materials/shaders (should be auto set via prefab) ***********/
        public Material matFillVolume;      // material used to fill volume data into each covered metavoxel
        public Material matRayMarch;        // material to raymarch each covered metavoxel
        public Material matBlendParticles;  // material to blend the raymarched volume with the camera's RT    
        public Material matCopyDepthBufferToTexture;    // material to make a copy of the main camera's depth buffer for use as texture
        public Material mvLineColor;
        public Shader generateLightDepthMapShader;

            /*** gui controls ****/
        public float fDisplacementScale;
        public bool fadeOutParticles;
        private bool bShowMetavoxelGrid;
        private bool bShowRayMarchSamplesPerPixel;
        private bool bShowMetavoxelDrawOrder;
        private bool bShowRayMarchBlendFunc;
        public float opacityFactor;
        public int softParticleStepDistance;
        public float fParticleGreyscale;
        public bool bUsePerlinNoise;

        /*
        * private members 
        */
        // Metavoxel grid state
        private MetaVoxel[, ,] mvGrid;
        private Vector3 wsGridCenter;
        private float mvScaleWithBorder;
        //public Cubemap perlinDispTexture;

        private ParticleSystem[] pPSys;

        // Render target resources
        private RenderTexture mainSceneRT; // camera draws all the objects in the scene but for the particles into this
        public RenderTexture particlesRT; // result of raymarching the metavoxels is stored in this    
        private RenderTexture fillMetavoxelRT, fillMetavoxelRT1; // bind this as RT when filling a metavoxel. we don't sample/use it though..
        private RenderTexture[, ,] mvFillTextures; // 3D textures that hold metavoxel fill data
        public RenderTexture[,] lightPropogationUAVs; // Light propogation texture used while filling metavoxels
        public RenderTexture lightDepthMap;
        public RenderTexture tmpDepth;

        // scripted camera for mini-shadow map generation
        private GameObject lightCamera;

        // misc state
        //private AABBForParticles pBounds;
        private int numParticlesEmitted;
        private int numMetavoxelsCovered;
        private Quaternion lightOrientation; // Light movement detection
        private Matrix4x4 worldToLight;
        private Mesh cubeMesh;
        private Mesh quadMesh;
        private Camera mainCam;
        private Camera shadowCam;


        //-------------------------------  Unity callbacks ----------------------------------------
        void Start()
        {
            // Check inspector fields that need to be assigned.
            if (!dirLight)
            {
                Debug.Log("dirLight not assigned in inspector. Searching for object of type Light in scene.");

                dirLight = FindObjectOfType<Light>(); // search for light source in scene

                if (!dirLight)
                    Debug.LogError("Didn't find object of type light in scene");
            }

            if (particlesLayer == 0)
            {
                Debug.LogError("[VolumetricParticleRenderer] particlesLayer not assigned." + 
                                "Please create a layer to house volumetric particle systems in and have particlesLayer set to it.");
            }


            lightOrientation = dirLight.transform.rotation;
            worldToLight = dirLight.transform.worldToLocalMatrix;
            fadeOutParticles = false;
            volumeTextureAnisoLevel = 1; // The value range of this variable goes from 1 to 9, where 1 equals no filtering applied and 9 equals full filtering applied
            numMetavoxelsCovered = 0;
            wsGridCenter = Vector3.zero;
            mvScaleWithBorder = mvScale * numVoxelsInMetavoxel / (float)(numVoxelsInMetavoxel - 2 * numBorderVoxels);
            mainCam = GetComponent<Camera>();
            mainCam.cullingMask &= ~particlesLayer.value;

            CreateResources();
            CreateMeshes();         
            InitCameraAtLight();
            GetParticleSystems();
            //CreateNoiseCubemap();
            
            // [FIXME] The raymarch step requires the camera depth buffer for depth occlusion.
            // The scene needs to be shaded POST raymarching, t
            // [perf threat] Unity is going to do a Z-prepass simply because of the line below
            mainCam.depthTextureMode = DepthTextureMode.Depth; // this makes the depth buffer available for all the shaders as _CameraDepthTexture

            bShowRayMarchSamplesPerPixel = bShowMetavoxelGrid = bShowRayMarchSamplesPerPixel 
                                         = bShowMetavoxelDrawOrder = bShowRayMarchBlendFunc = false;        
            //pBounds = particleSys.GetComponent<AABBForParticles>();        
        }


        void Update()
        {
        }


        // Order of event calls in unity: file:///C:/Program%20Files%20(x86)/Unity/Editor/Data/Documentation/html/en/Manual/ExecutionOrder.html
        void OnPreRender()
        {
            // Since we don't directly render to the back buffer, we need to manually clear the render targets used every frame.
            RenderTexture.active = particlesRT;
            GL.Clear(false, true, new Color(0f, 0f, 0f, 0f));

            RenderTexture.active = mainSceneRT;
            GL.Clear(true, true, Color.black);
            mainCam.targetTexture = mainSceneRT;
        }


        // OnPostRender is called after a camera has finished rendering the scene.
        void OnPostRender()
        {
            // Generate the mini-depth map from the light's pov
            shadowCam.RenderWithShader(generateLightDepthMapShader, null as string);

            // Updating metavoxels is costly every frame, so allow for sparse updates
            if (Time.frameCount % updateInterval == 0)
            {
                // if light has moved (or) grid center has changed, update metavoxel positions and the shadowCam
                if (dirLight.transform.rotation != lightOrientation /* light direction has changed*/)
                    //|| wsGridCenter != gridCenter.transform.position)
                {
                    // update internal variables 
                    lightOrientation = dirLight.transform.rotation;
                    worldToLight = dirLight.transform.worldToLocalMatrix;
                    //wsGridCenter = gridCenter.transform.position;
                    UpdateMetavoxelPositions();
                    UpdatePositionOfCameraAtLight();
                }

                BinParticlesToMetavoxels();
                FillMetavoxels();
            }

            // fill particlesRT with the ray marched volume
            RaymarchMetavoxels();

            // Composite the particles onto the main scene
            Graphics.Blit(particlesRT, mainSceneRT, matBlendParticles);

            if (bShowMetavoxelGrid)
            {
                DrawMetavoxelGrid();
            }

            // need to set the targetTexture to null, else the Blit below doesn't work
            mainCam.targetTexture = null;
            Graphics.Blit(mainSceneRT, null as RenderTexture); // copy to back buffer
        }


        void OnDestroy()
        {
            ReleaseResources();
        }

        //-------------------------------  private fns --------------------------------------------
        // Create all the render targets/uavs needed. 
        // Note: None of them use RenderTexture.GetTemporary(); Don't think that's the better choice. 
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
                mainCam.targetTexture = mainSceneRT;
            }

            if (!fillMetavoxelRT)
            {
                fillMetavoxelRT = new RenderTexture(numVoxelsInMetavoxel, numVoxelsInMetavoxel, 0, RenderTextureFormat.R8);
                fillMetavoxelRT.useMipMap = false;
                fillMetavoxelRT.isVolume = false;
                fillMetavoxelRT.enableRandomWrite = false;
                fillMetavoxelRT.Create();
            }

            if (!fillMetavoxelRT1)
            {
                fillMetavoxelRT1 = new RenderTexture(numVoxelsInMetavoxel, numVoxelsInMetavoxel, 0, RenderTextureFormat.R8);
                fillMetavoxelRT1.useMipMap = false;
                fillMetavoxelRT1.isVolume = false;
                fillMetavoxelRT1.enableRandomWrite = false;
                fillMetavoxelRT1.Create();
            }


            //if (!lightPropogationUAVs)
            //{
            //    lightPropogationUAVs = new RenderTexture(numMetavoxelsX * numVoxelsInMetavoxel, numMetavoxelsY * numVoxelsInMetavoxel, 0 /* no need depth surface, just color*/, RenderTextureFormat.RFloat);
            //    lightPropogationUAVs.generateMips = false;
            //    lightPropogationUAVs.enableRandomWrite = true; // use as UAV
            //    lightPropogationUAVs.Create();
            //}

            if (!lightDepthMap)
            {
                lightDepthMap = new RenderTexture(numMetavoxelsX * numVoxelsInMetavoxel, numMetavoxelsY * numVoxelsInMetavoxel, 24, RenderTextureFormat.Depth);
                lightDepthMap.generateMips = false;
                lightDepthMap.enableRandomWrite = false;
                lightDepthMap.Create();
            }

            //**clean***//
            //tmpDepth = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.RFloat); // just color, no depth
            //tmpDepth.Create();

            CreateMetavoxelGrid(numMetavoxelsX, numMetavoxelsY, numMetavoxelsZ); // creates the fill texture per metavoxel
            CreateLightPropagationUAVs();
        }


        void ReleaseResources()
        {
            particlesRT.Release();
            mainSceneRT.Release();            
            lightDepthMap.Release(); 
            fillMetavoxelRT.Release();
            fillMetavoxelRT1.Release();

            ReleaseMetavoxelTextures();
            ReleaseLightPropagationUAVs();
        }

        // Create mv info & associated fill texture for every mv in the grid
        void CreateMetavoxelGrid(int xDim, int yDim, int zDim)
        {
            mvGrid = new MetaVoxel[zDim, yDim, xDim]; // note index order -- prefer locality in X,Y
            mvFillTextures = new RenderTexture[zDim, yDim, xDim];

            // Init fill texture and particle list per metavoxel
            for (int zz = 0; zz < zDim; zz++)
            {
                for (int yy = 0; yy < yDim; yy++)
                {
                    for (int xx = 0; xx < xDim; xx++)
                    {
                        mvGrid[zz, yy, xx].mParticlesCovered = new List<MetaVoxel.ParticleInfo>();
                        CreateMetavoxelTexture(xx, yy, zz);
                    }
                }
            }

            // Find metavoxel position & orientation 
            UpdateMetavoxelPositions();
        }


        void CreateMetavoxelTexture(int xx, int yy, int zz)
        {
            // Note that constructing a RenderTexture object does not create the hardware representation immediately. The actual render texture is created upon first use or when Create is called manually
            // file:///C:/Program%20Files%20(x86)/Unity/Editor/Data/Documentation/html/en/ScriptReference/RenderTexture-ctor.html
            mvFillTextures[zz, yy, xx] = new RenderTexture(numVoxelsInMetavoxel, numVoxelsInMetavoxel, 0 /* no need depth surface, just color*/, RenderTextureFormat.ARGBHalf);
            mvFillTextures[zz, yy, xx].isVolume = true;
            mvFillTextures[zz, yy, xx].volumeDepth = numVoxelsInMetavoxel;
            mvFillTextures[zz, yy, xx].generateMips = false;
            mvFillTextures[zz, yy, xx].enableRandomWrite = true; // use as UAV
        }
   

        void ReleaseMetavoxelTextures()
        {
            for (int zz = 0; zz < numMetavoxelsZ; zz++)
            {
                for (int yy = 0; yy < numMetavoxelsY; yy++)
                {
                    for (int xx = 0; xx < numMetavoxelsX; xx++)
                    {
                        mvFillTextures[zz, yy, xx].Release();
                    }
                }
            }
        }


        // Creating a single 2D texture that's read and written to by each metavoxel column in the FillMetavoxels() phase requires a flush between every two draw calls.
        // Technically, the dependency exists only between voxels in a metavoxel column. Splitting the LPT into several smaller pieces would 
        void CreateLightPropagationUAVs()
        {
            lightPropogationUAVs = new RenderTexture[numMetavoxelsX, numMetavoxelsY];

            for (int yy = 0; yy < numMetavoxelsY; yy++)
            {
                for (int xx = 0; xx < numMetavoxelsX; xx++)
                {
                    lightPropogationUAVs[xx, yy] = new RenderTexture(numVoxelsInMetavoxel, numVoxelsInMetavoxel, 0 /* no need depth surface, just color*/, RenderTextureFormat.RFloat);
                    lightPropogationUAVs[xx, yy].isVolume = false;
                    lightPropogationUAVs[xx, yy].generateMips = false;
                    lightPropogationUAVs[xx, yy].enableRandomWrite = true; // use as UAV
                    lightPropogationUAVs[xx, yy].Create();
                }
            }
        }

        void ReleaseLightPropagationUAVs()
        {
            for (int yy = 0; yy < numMetavoxelsY; yy++)
            {
                for (int xx = 0; xx < numMetavoxelsX; xx++)
                {
                    lightPropogationUAVs[xx, yy].Release();
                }
            }
        }
        

        //void CreateNoiseCubemap()
        //{
        //    int size = this.numVoxelsInMetavoxel * 4;

        //    perlinDispTexture = new Cubemap(size, TextureFormat.ARGB32, false); // [todo] find out best compression format for single channel textuure

        //    for (int face = 0; face < 6; face++)
        //    {
        //        Color[] colors = new Color[size * size];

        //        for(int ii = 0; ii < size * size; ii++)
        //        {
        //            int xx = face * size + (ii % size), 
        //                yy = face * size + (ii / size);

        //            float origin = face * size;
        //            float noise = Mathf.PerlinNoise(xx / (float) (6 * size) ,
        //                                            yy / (float) (6 * size));
        //            colors[ii] = new Color(noise, noise, noise, noise);
        //        }

        //        perlinDispTexture.SetPixels(colors, (CubemapFace) face);
        //        perlinDispTexture.Apply(false, false);

        //    }
        //}


        // Create a secondary camera that mimics the directional light orientation and looks at the metavoxel grid.
        // This is used to create a mini-shadow map, which is used during the fillmetavoxel phase to account for objects
        // occluding the volume. 
        // The ortho projection matrix is a tight fit to the metavoxel grid. 
        // [FIXME] This step isn't required if we can access the in-built shadow map. Unfortunately, I haven't figured out how to do that        
        void InitCameraAtLight()
        {
            lightCamera = new GameObject();
            lightCamera.name = "LightCamera";
            lightCamera.transform.parent = dirLight.transform;
            // make the shadow map camera look at the center of the metavoxel grid
            UpdatePositionOfCameraAtLight();

            lightCamera.gameObject.SetActive(false);
          
            shadowCam = lightCamera.AddComponent<Camera>() as Camera;

            if (shadowCam != null)
            {
                shadowCam.orthographic = true;

                // Set the camera extents to that of the metavoxel grid (vertically.. horizontal part is taken care of by aspect ratio)
                // setting orthographicSize wont work, since l,r are t,b scaled by aspect ratio, making it a rectangular view.
                // so we need to explicitly generate the projection matrix as we need it
                //c.orthographicSize = numMetavoxelsY * mvScale * 0.5f; // won't work

                float r = numMetavoxelsX * mvScale * 0.5f, t = numMetavoxelsY * mvScale * 0.5f;

                Matrix4x4 orthoProjectionMatrix = Matrix4x4.Ortho(-r, r, -t, t, 0.3f, 1000f);
                shadowCam.projectionMatrix = orthoProjectionMatrix; // todo: is this pixel correct? check http://docs.unity3d.com/ScriptReference/GL.LoadPixelMatrix.html

                shadowCam.targetTexture = lightDepthMap;
                shadowCam.cullingMask = 1 << LayerMask.NameToLayer("Default");
                shadowCam.clearFlags = CameraClearFlags.Depth | CameraClearFlags.Color;
                shadowCam.useOcclusionCulling = false;
            }
            else
            {
                Debug.LogError("Could not create camera at light");
            }
        }

        // The secondary camera looks at the metavoxel grid center from a fixed distance
        // [FIXME] This can mess up the shadows received by the volume (i.e., fixed distance isn't correct)
        void UpdatePositionOfCameraAtLight()
        {
            // ideally we want to set the near plane based on the bounding box of the scene. 
            // for now, keep it at 200 units from the metavoxel center
            lightCamera.transform.position = wsGridCenter - dirLight.transform.forward * 200f;
            lightCamera.transform.localRotation = Quaternion.identity;            
        }
    

        void UpdateMetavoxelPositions()
        {
          /* 
           * effort isn't made to ensure the grid covers the frustum visible from the camera.
           * 
           * the grid layout uses the directional light's view space (LHS). 
           * (xx=0,yy=0,zz=0) is the left-bottom-front of the grid. that takes care of the position of each metavoxel in the grid.           
          */

            Vector3 lsWorldOrigin = dirLight.transform.worldToLocalMatrix.MultiplyPoint3x4(wsGridCenter); // xform origin to light space

            for (int zz = 0; zz < numMetavoxelsZ; zz++)
            {
                for (int yy = 0; yy < numMetavoxelsY; yy++)
                {
                    for (int xx = 0; xx < numMetavoxelsX; xx++)
                    {
                        Vector3 lsOffset = new Vector3(numMetavoxelsX / 2 - xx, numMetavoxelsY / 2 - yy, numMetavoxelsZ / 2 - zz) * mvScale;
                        Vector3 wsMetavoxelPos = dirLight.transform.localToWorldMatrix.MultiplyPoint3x4(lsWorldOrigin - lsOffset); // using - lsOffset means that zz = 0 is closer to the light
                        mvGrid[zz, yy, xx].mPos = wsMetavoxelPos;                       
                    }
                }
            }
        }


        void GetParticleSystems()
        {
            // Add all particle systems that use the particlesLayer. This is done once at the start of the scene, which is a bad assumption.
            // To dynamically add particle systems, there needs to be a callback to update the pPSys array.
            ParticleSystem[] objs = GameObject.FindObjectsOfType<ParticleSystem>();
            
            List<ParticleSystem> lps = new List<ParticleSystem>(); // tmp storage
            int count = 0;

            foreach(ParticleSystem ps in objs)
            {
                if ((particlesLayer.value & (1 << ps.gameObject.layer)) > 0)
                {
                    count++;
                    lps.Add(ps);
                }
            }

            pPSys = new ParticleSystem[count];
            pPSys = lps.ToArray();
        }


        void BinParticlesToMetavoxels()
        {
            // Clear previous particle lists
            for (int zz = 0; zz < numMetavoxelsZ; zz++)
            {
                for (int yy = 0; yy < numMetavoxelsY; yy++)
                {
                    for (int xx = 0; xx < numMetavoxelsX; xx++)
                    {
                        mvGrid[zz, yy, xx].mParticlesCovered.Clear();
                    }
                }
            }

            // iterate over the particles and bin them to the metavoxels they cover
            foreach (ParticleSystem ps in pPSys)
            {
                ParticleSystem.Particle[] particles = new ParticleSystem.Particle[ps.maxParticles];
                numParticlesEmitted = ps.GetParticles(particles);

                for (int pp = 0; pp < numParticlesEmitted; pp++)
                {

                    // xform particle to mv space to make it a sphere-aabb intersection test
                    Vector3 wsParticlePos = ps.transform.localToWorldMatrix.MultiplyPoint3x4(particles[pp].position);
                    Vector3 lsParticlePos = worldToLight.MultiplyPoint3x4(wsParticlePos);
                    Vector3 lsMVGridCenter = worldToLight.MultiplyPoint3x4(wsGridCenter);

                    Vector3 pIndexOffset = (lsParticlePos - lsMVGridCenter) / mvScale;
                    Vector3 pIndex = pIndexOffset + new Vector3(numMetavoxelsX, numMetavoxelsY, numMetavoxelsZ) * 0.5f;

                    int pExtents = Mathf.RoundToInt((particles[pp].size / 2f) / mvScale);
                    Vector3 minIndex = pIndex - Vector3.one * pExtents,
                            maxIndex = pIndex + Vector3.one * pExtents;

                    Vector3 mvGridIndexLimit = new Vector3(numMetavoxelsX - 1, numMetavoxelsY - 1, numMetavoxelsZ - 1);

                    minIndex = Vector3.Max(Vector3.zero, minIndex);
                    maxIndex = Vector3.Min(mvGridIndexLimit, maxIndex);

                    for (int zz = (int)minIndex.z; zz <= (int)maxIndex.z; zz++)
                    {
                        for (int yy = (int)minIndex.y; yy <= (int)maxIndex.y; yy++)
                        {
                            for (int xx = (int)minIndex.x; xx <= (int)maxIndex.x; xx++)
                            {
                                Matrix4x4 worldToMetavoxelMatrix = Matrix4x4.TRS(mvGrid[zz, yy, xx].mPos,
                                                                                 lightOrientation,
                                                                                 Vector3.one * mvScaleWithBorder).inverse; // Account for the border of the metavoxel while binning

                                Vector3 mvParticlePos = worldToMetavoxelMatrix.MultiplyPoint3x4(wsParticlePos);
                                float mvParticleRadius = (particles[pp].size / 2f) / mvScaleWithBorder; // Intersection test is with the enlarged metavoxel (i.e. with the border), so 

                                bool particle_intersects_metavoxel = MathUtil.DoesBoxIntersectSphere(new Vector3(-0.5f, -0.5f, -0.5f),
                                                                                                     new Vector3(0.5f, 0.5f, 0.5f),
                                                                                                     mvParticlePos,
                                                                                                     mvParticleRadius);

                                if (particle_intersects_metavoxel)
                                {
                                    MetaVoxel.ParticleInfo pinfo;
                                    pinfo.mParticle = particles[pp];
                                    pinfo.mParent = ps;
                                    mvGrid[zz, yy, xx].mParticlesCovered.Add(pinfo);
                                }
                            }
                        }
                    }

                } // pp
            }
            
        }


        void FillMetavoxels()
        {
            // Clear the light propogation texture before we write to it
            //Graphics.SetRenderTarget(lightPropogationUAVs);
            //GL.Clear(false, true, Color.red);

            SetFillPassConstants();
            numMetavoxelsCovered = 0;

            // process the metavoxels in order of Z-slice closest to light to farthest
            for (int zz = 0; zz < numMetavoxelsZ; zz++)
            {                
                for (int yy = 0; yy < numMetavoxelsY; yy++)
                {
                    for (int xx = 0; xx < numMetavoxelsX; xx++)
                    {
                        if (zz == 0)
                        {
                            Graphics.SetRenderTarget(lightPropogationUAVs[xx, yy]);
                            GL.Clear(false, true, new Color(dirLight.intensity, 0f, 0f));
                        }

                        if (mvGrid[zz, yy, xx].mParticlesCovered.Count != 0)
                        {
                            FillMetavoxel(xx, yy, zz);
                            numMetavoxelsCovered++;
                        }
                    }
                }
            }

        }


        void SetFillPassConstants()
        {
            // metavoxel specific stuff
            matFillVolume.SetVector("_MetavoxelGridDim", new Vector3(numMetavoxelsX, numMetavoxelsY, numMetavoxelsZ));
            matFillVolume.SetFloat("_NumVoxels", numVoxelsInMetavoxel);
            matFillVolume.SetInt("_MetavoxelBorderSize", Mathf.Clamp(numBorderVoxels, 0, numVoxelsInMetavoxel - 2));
            matFillVolume.SetFloat("_MetavoxelScale", mvScaleWithBorder);

            // scene related stuff
            // -- light
            matFillVolume.SetTexture("_LightDepthMap", lightDepthMap);
            matFillVolume.SetMatrix("_WorldToLight", lightCamera.transform.worldToLocalMatrix);
            matFillVolume.SetVector("_LightForward", dirLight.transform.forward.normalized);
            matFillVolume.SetVector("_LightColor", dirLight.color);
            matFillVolume.SetVector("_AmbientColor", ambientColor);
            matFillVolume.SetFloat("_InitLightIntensity", dirLight.intensity);
            matFillVolume.SetFloat("_NearZ", shadowCam.nearClipPlane);
            matFillVolume.SetFloat("_FarZ", shadowCam.farClipPlane);

            Camera c = shadowCam;
            matFillVolume.SetMatrix("_LightProjection", c.projectionMatrix);

            // -- particles
            matFillVolume.SetFloat("_OpacityFactor", opacityFactor);
            matFillVolume.SetFloat("_DisplacementScale", fDisplacementScale);
            matFillVolume.SetFloat("_ParticleGreyscale", fParticleGreyscale);

            //if (bUsePerlinNoise)
            //{
            //    matFillVolume.SetTexture("_DisplacementTexture", perlinDispTexture);
            //}


            int fadeParticles = 0;
            if (fadeOutParticles)
                fadeParticles = 1;

            matFillVolume.SetInt("_FadeOutParticles", fadeParticles);
        }


        // Each metavoxel that has particles in it needs to be filled with volume info (opacity for now)
        // Since nothing is rendered to the screen while filling the metavoxel volume textures up, we have to resort to
        void FillMetavoxel(int xx, int yy, int zz)
        {
            //Graphics.ClearRandomWriteTargets();

            // Note: Calling Create lets you create it up front. Create does nothing if the texture is already created.
            // file:///C:/Program%20Files%20(x86)/Unity/Editor/Data/Documentation/html/en/ScriptReference/RenderTexture.Create.html
            if (!mvFillTextures[zz, yy, xx].IsCreated())
                mvFillTextures[zz, yy, xx].Create();

            // don't need to clear the mv fill texture since we write to every pixel on it (on every depth slice)
            // regardless of whether that pixel is covered or not.   
            Graphics.SetRenderTarget(fillMetavoxelRT);
            Graphics.SetRandomWriteTarget(1, mvFillTextures[zz, yy, xx]);
            Graphics.SetRandomWriteTarget(2, lightPropogationUAVs[xx, yy]);

            // fill structured buffer
            int numParticles = mvGrid[zz, yy, xx].mParticlesCovered.Count;
            SphericalParticle[] particles = new SphericalParticle[numParticles];

            int index = 0;

            foreach (MetaVoxel.ParticleInfo pinfo in mvGrid[zz, yy, xx].mParticlesCovered)
            {
                ParticleSystem.Particle p = pinfo.mParticle;                
                ParticleSystem psys = pinfo.mParent;

                Vector3 wsPos = psys.transform.localToWorldMatrix.MultiplyPoint3x4(p.position);
                particles[index].mWorldToLocal  = Matrix4x4.TRS(wsPos, Quaternion.AngleAxis(p.rotation, psys.transform.forward), new Vector3(p.size, p.size, p.size)).inverse;
                particles[index].mWorldPos      = wsPos;
                particles[index].mRadius        = p.size / 2f; // size is diameter
                particles[index].mPercLifeLeft  = p.lifetime / p.startLifetime; // [time-particle-will-remain-alive / particle-lifetime] use this to make particles less "dense" as they meet their end.
                index++;
            }

            ComputeBuffer particlesBuffer = new ComputeBuffer(numParticles,
                                                        Marshal.SizeOf(particles[0]));
            particlesBuffer.SetData(particles);

            
            matFillVolume.SetMatrix("_MetavoxelToWorld", Matrix4x4.TRS(mvGrid[zz, yy, xx].mPos,
                                                                       lightOrientation,
                                                                       Vector3.one * mvScaleWithBorder)); // need to fill the border voxels of this metavoxel too (so we need to make it "seem" bigger)
            matFillVolume.SetVector("_MetavoxelIndex", new Vector3(xx, yy, zz));
            matFillVolume.SetInt("_NumParticles", numParticles);
            matFillVolume.SetBuffer("_Particles", particlesBuffer);

            Graphics.Blit(fillMetavoxelRT1, fillMetavoxelRT1, matFillVolume, 0);
            // cleanup
            particlesBuffer.Release();
            Graphics.ClearRandomWriteTargets();
        }


        // The ray march step uses alpha blending and imposes order requirements 
        List<MetavoxelSortData> SortMetavoxelSlicesFarToNearFromEye()
        {
            List<MetavoxelSortData> mvPerSliceFarToNear = new List<MetavoxelSortData>();
            Vector3 cameraPos = Camera.main.transform.position;

            for (int yy = 0; yy < numMetavoxelsY; yy++)
            {
                for (int xx = 0; xx < numMetavoxelsX; xx++)
                {
                    Vector3 distFromEye = mvGrid[0, yy, xx].mPos - cameraPos;
                    //Debug.Log("Distance from mv " + xx + ", " + yy + "to camera is " + Vector3.Dot(distFromEye, distFromEye));
                    mvPerSliceFarToNear.Add(new MetavoxelSortData(xx, yy, Vector3.Dot(distFromEye, distFromEye)));
                }
            }

            mvPerSliceFarToNear.Sort();
            mvPerSliceFarToNear.Reverse();

            return mvPerSliceFarToNear;
        }


        // Submit a cube per metavoxel from the perspective of the main camera, to ray march its contents and blend with previously raymarched metavoxels
        public void RaymarchMetavoxels()
        {
            // Copy the camera's depth buffer into a texture to sample from during the ray march
            // We need to do this since the depth buffer is also bound (even though we don't write to it, the API doesn't allow it to be bound this way)
            //RenderTexture tmpDepth = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32); // just color, no depth
            //tmpDepth.Create();
            //Graphics.SetRenderTarget(mainSceneRT); // The RT was changed in the Fill pass, so reset it
            //Graphics.Blit(mainSceneRT, tmpDepth, matCopyDepthBufferToTexture);

            // Use the camera's existing depth buffer to depth-test the particles, while
            // writing the ray marched volume into a separate color buffer (particlesRT),
            // that is later composited with the main scene
            Graphics.SetRenderTarget(particlesRT.colorBuffer, mainSceneRT.depthBuffer);

            //matRayMarch.SetTexture("_CameraDepth", tmpDepth);
            // Set constant buffers that are shared by all the metavoxels
            SetRaymarchPassConstants();

            // A slice here refers to an XY layer of metavoxels in light space (i.e. they have a constant light space Z)
            List<MetavoxelSortData> mvPerSliceFarToNear = SortMetavoxelSlicesFarToNearFromEye();

            // Find the slice boundary that segregates the blend order requirements for metavoxels in the grid
            Vector3 lsCameraPos = worldToLight.MultiplyPoint3x4(Camera.main.transform.position);
            float lsFirstZSlice = worldToLight.MultiplyPoint3x4(mvGrid[0, 0, 0].mPos).z;
            float mvBlendOverIndex = (lsCameraPos.z - lsFirstZSlice) / mvScale;

            int zBoundary = Mathf.Clamp( Mathf.RoundToInt(mvBlendOverIndex), -1, numMetavoxelsZ - 1);

            int mvCount = 0;

            if (zBoundary >= 0)
            {
                // Render metavoxel slices with back to front blending
                // (a) increasing order along the direction of the light
                // (b) farthest-to-nearest from the camera per slice

                // Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha // Back to Front blending (blend over)
                matRayMarch.SetInt("SrcFactor", (int)UnityEngine.Rendering.BlendMode.One);
                matRayMarch.SetInt("DstFactor", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                matRayMarch.SetInt("SrcFactorA", (int)UnityEngine.Rendering.BlendMode.One);
                matRayMarch.SetInt("DstFactorA", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

                matRayMarch.SetInt("_RayMarchBlendOver", 1);


                for (int zz = 0; zz <= zBoundary; zz++)
                {
                    foreach (MetavoxelSortData vv in mvPerSliceFarToNear)
                    {
                        int xx = (int)vv.x, yy = (int)vv.y;

                        if (mvGrid[zz, yy, xx].mParticlesCovered.Count != 0)
                        {
                            RaymarchMetavoxel(xx, yy, zz, mvCount++);
                        }

                    }
                }
            }


            if ((zBoundary + 1) < numMetavoxelsZ)
            {
                // Render metavoxel slices with front-to-back blending
                // (a) increasing order along the direction of the light
                // (b) nearest-to-farthest from the camera per slice

                // Blend OneMinusDstAlpha One, OneMinusDstAlpha One // Front to Back blending (blend under)
                matRayMarch.SetInt("SrcFactor", (int)UnityEngine.Rendering.BlendMode.OneMinusDstAlpha);
                matRayMarch.SetInt("DstFactor", (int)UnityEngine.Rendering.BlendMode.One);
                matRayMarch.SetInt("SrcFactorA", (int)UnityEngine.Rendering.BlendMode.OneMinusDstAlpha);
                matRayMarch.SetInt("DstFactorA", (int)UnityEngine.Rendering.BlendMode.One);

                matRayMarch.SetInt("_RayMarchBlendOver", 0); // BlendUnder

                mvPerSliceFarToNear.Reverse(); // make it nearest-to-farthest

                for (int zz = zBoundary + 1; zz < numMetavoxelsZ; zz++)
                {
                    foreach (MetavoxelSortData vv in mvPerSliceFarToNear)
                    {
                        int xx = (int)vv.x, yy = (int)vv.y;

                        if (mvGrid[zz, yy, xx].mParticlesCovered.Count != 0)
                        {
                            RaymarchMetavoxel(xx, yy, zz, mvCount++);
                        }
                    }
                }
            }

            //tmpDepth.Release();
        }


        void SetRaymarchPassConstants()
        {
            // Metavoxel grid (volume) rendering uniforms
            matRayMarch.SetVector("_MetavoxelGridDim", new Vector3(numMetavoxelsX, numMetavoxelsY, numMetavoxelsZ));            
            matRayMarch.SetVector("_MetavoxelGridCenter", wsGridCenter);
            matRayMarch.SetFloat("_MetavoxelScale", mvScale);
            matRayMarch.SetFloat("_NumVoxels", numVoxelsInMetavoxel);                                   
            matRayMarch.SetInt("_MetavoxelBorderSize", numBorderVoxels);
            matRayMarch.SetInt("_NumRaymarchStepsPerMV", rayMarchSteps);
            matRayMarch.SetInt("_SoftDistance", softParticleStepDistance);
            //m.SetVector("_AABBMin", pBounds.aabb.min);
            //m.SetVector("_AABBMax", pBounds.aabb.max);


            // Camera uniforms
            matRayMarch.SetFloat("_Fov", Mathf.Deg2Rad * Camera.main.fieldOfView);
            matRayMarch.SetFloat("_NearZ", Camera.main.nearClipPlane);
            matRayMarch.SetFloat("_FarZ", Camera.main.farClipPlane);
          
            
            // Debug/Tmp constants
            SetKeyword(matRayMarch, bShowMetavoxelDrawOrder,    "DBG_ON_DRAW_ORDER", "DBG_OFF_DRAW_ORDER");
            SetKeyword(matRayMarch, bShowRayMarchBlendFunc,     "DBG_ON_BLEND_FUNC", "DBG_OFF_BLEND_FUNC");
            SetKeyword(matRayMarch, bShowRayMarchSamplesPerPixel, "DBG_ON_NUM_SAMPLES", "DBG_OFF_NUM_SAMPLES");
            matRayMarch.SetInt("_NumMetavoxelsCovered", numMetavoxelsCovered);            
        }


        void RaymarchMetavoxel(int xx, int yy, int zz, int orderIndex)
        {            
            mvFillTextures[zz, yy, xx].filterMode = FilterMode.Bilinear;
            mvFillTextures[zz, yy, xx].wrapMode = TextureWrapMode.Repeat;
            mvFillTextures[zz, yy, xx].anisoLevel = volumeTextureAnisoLevel;

            matRayMarch.SetTexture("_VolumeTexture", mvFillTextures[zz, yy, xx]);
            Matrix4x4 mvToWorld = Matrix4x4.TRS(mvGrid[zz, yy, xx].mPos,
                                                lightOrientation,
                                                Vector3.one * mvScale); // border should NOT be included here. we want to rasterize only the pixels covered by the metavoxel

            // Set metavoxel specific constants
            matRayMarch.SetMatrix("_MetavoxelToWorld", mvToWorld);
            matRayMarch.SetMatrix("_CameraToMetavoxel", mvToWorld.inverse * Camera.main.transform.localToWorldMatrix);
            matRayMarch.SetVector("_MetavoxelIndex", new Vector3(xx, yy, zz));            
            
            matRayMarch.SetInt("_OrderIndex", orderIndex); // tmp/debug constant
            
            // Absence of the line below caused several hours of debugging madness.
            // SetPass needs to be called AFTER all material properties are set prior to every DrawMeshNow call.
            // Under the hood, seems like Unity creates a material instance with different resources/constant buffers bound that is "flushed" with SetPass
            bool setPass = matRayMarch.SetPass(0);
            if (!setPass)
            {
                Debug.LogError("material set pass returned false;..");
            }

            Graphics.DrawMeshNow(cubeMesh, Vector3.zero, Quaternion.identity);
        }


        void CreateMeshes()
        {
            CreateCubeMesh();
            CreateQuadMesh();
        }


        void CreateCubeMesh()
        {
            cubeMesh = new Mesh();

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

            cubeMesh.vertices = vertices;
            cubeMesh.normals = normales;
            cubeMesh.uv = uvs;
            cubeMesh.triangles = triangles;

            cubeMesh.RecalculateBounds();
            cubeMesh.Optimize();
        }


        void CreateQuadMesh()
        {
            quadMesh = new Mesh();

            Vector3 botleft = new Vector3(-1f, -1f, 0f),
                    botRight = new Vector3(1f, -1f, 0f),
                    topRight = new Vector3(1f, 1f, 0f),
                    topLeft = new Vector3(-1f, 1f, 0f);

            quadMesh.vertices = new Vector3[] {
                botleft, botRight, topRight, topLeft                
            };

            quadMesh.triangles = new int[] {
                                            0, 1, 2,
                                            0, 2, 3
                                        };
        }


        public void DrawMetavoxelGrid()
        {
            for (int zz = 0; zz < numMetavoxelsZ; zz++)
            {
                for (int yy = 0; yy < numMetavoxelsY; yy++)
                {
                    for (int xx = 0; xx < numMetavoxelsX; xx++)
                    {
                        if (mvGrid[zz, yy, xx].mParticlesCovered.Count != 0)
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
            Quaternion q = lightOrientation;


            float h = mvScale * 0.5f;
            // back, front --> Z ; top, bot --> Y ; left, right --> X
            Vector3 offBotLeftBack = q * new Vector3(-h, -h, h),
                    offBotLeftFront = q * new Vector3(-h, -h, -h),
                    offTopLeftBack = q * new Vector3(-h, h, h),
                    offTopLeftFront = q * new Vector3(-h, h, -h),
                    offBotRightBack = q * new Vector3(h, -h, h),
                    offBotRightFront = q * new Vector3(h, -h, -h),
                    offTopRightBack = q * new Vector3(h, h, h),
                    offTopRightFront = q * new Vector3(h, h, -h);

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


        void SetKeyword(Material m, bool firstOn, string firstKeyword, string secondKeyword)
        {
            m.EnableKeyword(firstOn ? firstKeyword : secondKeyword);
            m.DisableKeyword(firstOn ? secondKeyword : firstKeyword);
        }

        /*********************** Gui callback setters **************************************
         *  Functions below have been hooked to the appropriate GUI element in the inspector
         */        
        //-- render options
        public void SetDisplacementScale(float ds)
        {
            fDisplacementScale = ds;
        }


        public void SetRayMarchSteps(float steps)
        {
            rayMarchSteps = (int) steps;
        }


        public void SetGridDimensions(float a)
        {
            // Free up current grid resources, if need be.            

            // Resize grid
            CreateMetavoxelGrid((int)a, (int)a, (int)a);
        }

        public void SetGridScale(float s)
        {
            mvScale = s;
            UpdateMetavoxelPositions();
        }

        //-- particle options
        public void SetFadeOutParticles(bool fade)
        {
            fadeOutParticles = fade;
        }

        public void SetParticleOpacityFactor(float f)
        {
            opacityFactor = f;
        }

        public void SetNumParticles(float n)
        {
            foreach(ParticleSystem p in pPSys)
                p.maxParticles = (int)n;
        }

        public void SetFadeParticles(bool fade)
        {
            fadeOutParticles = fade;
        }

        //-- render debug options
        public void SetShowMetavoxelGrid(bool show)
        {
            bShowMetavoxelGrid = show;
        }

        public void SetShowRayMarchSamples(bool show)
        {
            bShowRayMarchSamplesPerPixel = show;
        }

        public void SetShowMetavoxelDrawOrder(bool show)     
        {
            bShowMetavoxelDrawOrder = show;
        }

        public void SetShowRayMarchBlendFunc(bool show)
        {
            bShowRayMarchBlendFunc = show;
        }

        public void SetUpdateInterval(float interval)
        {
            updateInterval = (int) interval;
        }

        public void SetTimeScale(float ts)
        {
            Time.timeScale = ts;
        }

        public void SetSoftParticleDistance(float stepDistance)
        {
            softParticleStepDistance = (int) stepDistance;
        }

        public void SetParticleGreyscale(float gs)
        {
            fParticleGreyscale = gs;
        }

        #if UNITY_EDITOR
        void OnDrawGizmos()
        {
            Gizmos.color = Color.blue;
            Vector3 lsWorldOrigin = dirLight.transform.worldToLocalMatrix.MultiplyPoint3x4(wsGridCenter); // xform origin to light space

            for (int zz = 0; zz < numMetavoxelsZ; zz++)
            {
                for (int yy = 0; yy < numMetavoxelsY; yy++)
                {
                    for (int xx = 0; xx < numMetavoxelsX; xx++)
                    {                        
                        // if the scene isn't playing, the metavoxel grid wouldn't have been created.
                        // recalculate metavoxel positions and rotations based on the light
                        Vector3 lsOffset = new Vector3(numMetavoxelsX / 2 - xx, numMetavoxelsY / 2 - yy, numMetavoxelsZ / 2 - zz) * mvScale;
                        Vector3 wsMetavoxelPos = dirLight.transform.localToWorldMatrix.MultiplyPoint3x4(lsWorldOrigin - lsOffset); // using - lsOffset means that zz = 0 is closer to the light
                        Quaternion q = new Quaternion();
                        q.SetLookRotation(dirLight.transform.forward, dirLight.transform.up);
                        Gizmos.matrix = Matrix4x4.TRS(wsMetavoxelPos, q, Vector3.one * mvScale);
                        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
                    }
                }
            }
        }
        #endif        
    }



}