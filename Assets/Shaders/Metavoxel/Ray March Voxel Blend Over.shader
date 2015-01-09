Shader "Custom/RayMarchMetavoxelBlendOver" {
	Properties{
		_VolumeTexture("Metavoxel fill data", 3D) = "" {}
		_LightPropogationTexture("Light Propogation", 2D) = "" {}
	}
SubShader
		{
			Tags {"Queue" = "Transparent"}
			Pass
			{
				Cull Front ZWrite Off ZTest Less
				// Syntax: Blend SrcFactor DstFactor, SrcFactorA DstFactorA
				Blend SrcAlpha OneMinusSrcAlpha, One One // Back to Front blending (blend over)
				BlendOp Add

				CGPROGRAM
#pragma target 5.0
#pragma exclude_renderers flash gles opengl
#pragma enable_d3d11_debug_symbols
#pragma vertex vert
#pragma fragment frag

#include "UnityCG.cginc"
#define green float4(0.0, 0.5, 0.0, 0.5)
#define yellow float4(0.5, 0.5, 0.0, 0.5)
#define orange float4(0.5, 0.4, 0.0, 0.5)
#define red float4(0.5, 0.0, 0.0, 0.5)
#define seethrough float4(0.0, 0.0, 0.0, 0.0)


				sampler3D _VolumeTexture;
				sampler2D _LightPropogationTexture;

				// Raymarch pass constants
				float _NumVoxels; // metavoxel's voxel dimensions
				int _MetavoxelBorderSize;
				//float3 _MetavoxelScale;

				// Metavoxel uniforms
				float4x4 _MetavoxelToWorld;
				float4x4 _WorldToMetavoxel;
				float4 _MetavoxelIndex;
				float3 _MetavoxelGridDim;
				float3 _MetavoxelSize;
				float _ParticleCoverageRatio; 

				// Camera uniforms
				float4x4 _CameraToWorldMatrix; // need to explicitly define this to get the main camera's matrices
				float4x4 _WorldToCameraMatrix;
				float4 _CameraWorldPos;
				float _Fov;
				float _Near;
				float _Far;
				float4 _ScreenRes;

				// Ray march constants
				int _NumSteps;
				float4 _AABBMin;
				float4 _AABBMax;

				// tmp
				int _ShowPrettyColors;
				int _ShowNumSamples;

				struct v2f {
					float4 pos : SV_POSITION;
					float3 worldPos : TEXCOORD;
				};

				v2f
					vert(appdata_base i) {
					// every vertex submitted is in a unit-metavoxel space
					// transform from model -> world -> eye -> proj -> clip space
					v2f o;

					// can't use the default UNITY_MATRIX_MVP since the draw is made using Graphics.DrawMeshNow
					o.pos = mul(mul(UNITY_MATRIX_VP, _MetavoxelToWorld), i.vertex); // clip space
					o.worldPos = mul(_MetavoxelToWorld, i.vertex); // world space
					return o;
				}

				struct Ray {
					float3 o; // origin
					float3 d; // direction (normalized)
				};
				
				
				bool
				IntersectBox(Ray r, float3 boxmin, float3 boxmax, 
							 out float tnear, out float tfar)
				{
					// compute intersection of ray with all six bbox planes
					float3 invR = 1.0 / r.d;
					float3 tbot = invR * (boxmin.xyz - r.o);
					float3 ttop = invR * (boxmax.xyz - r.o);
					// re-order intersections to find smallest and largest on each axis
					float3 tmin = min(ttop, tbot);
					float3 tmax = max(ttop, tbot);
					// find the largest tmin and the smallest tmax
					float2 t0 = max(tmin.xx, tmin.yz);
					tnear = max(t0.x, t0.y);
					t0 = min(tmax.xx, tmax.yz);
					tfar = min(t0.x, t0.y);
					// check for hit
					bool hit;
					if ((tnear > tfar))
						hit = false;
					else
						hit = true;
					return hit;
				}

				// [-1, 1] to [0, 1]
				float 
				normToUV(float x)
				{
					return (x + 1.0) / 2.0;
				}

				// Fragment shader
				// For each fragment, we have to iterate through all the particles covered
				// by the MV and fill the voxel column by iterating through each voxel slice.
				// [todo] this can be parallelized.
								float4
				frag(v2f i) : COLOR
				{			
					//return green;
					if (_ShowPrettyColors == 1) // Color metavoxels that are covered by particles 
					{
						if (_ParticleCoverageRatio < 0.15)
							return green;
						else if (_ParticleCoverageRatio < 0.35)
							return yellow;
						else if (_ParticleCoverageRatio < 0.55)
							return orange;
						else
							return red;
					}

					 
					// Find ray direction from camera through this pixel
					// -- Normalize the pixel position to a [-1, 1] range to help find its world space position
					// Note that view space uses a RHS system (looks down -Z) while Unity's editor uses a LHS system
					float3 csRayDir;
					csRayDir.xy = (2.0 * i.pos.xy / _ScreenRes) - 1.0; // [0, wh] to [-1, 1];
					csRayDir.x *= (_ScreenRes.x / _ScreenRes.y); // account for aspect ratio
					csRayDir.z = -rcp(tan(_Fov / 2.0)); // tan(fov_y / 2) = 1 / (norm_z)
					csRayDir = normalize(csRayDir);
							
					// Holy fucking balls, it took forever to find that aspect ratio bug.			
					//float3 csRayDir2 = normalize(mul(_WorldToCameraMatrix, float4(i.worldPos - _CameraWorldPos, 0)));
					//return float4(csRayDir2 - csRayDir, 1.0);

					// Find camera space intersections of the ray with the camera-AABB of the volume
					//float3 csAABBStart	= csRayDir * (_AABBMin.z / csRayDir.z);
					//float3 csAABBEnd	= csRayDir * (_AABBMax.z / csRayDir.z);
			
					float3 csVolOrigin = mul(_WorldToCameraMatrix, float4(0, 0, 0, 1));
					
					float2 n = max(_MetavoxelGridDim.xx, _MetavoxelGridDim.yz);
					float csVolHalfZ = sqrt(3) * 0.5 * max(n.x, n.y) * _MetavoxelSize.x;
					float csZVolMin = csVolOrigin.z + csVolHalfZ,
						  csZVolMax = csVolOrigin.z - csVolHalfZ;
					float3 csAABBStart	= csRayDir * (csZVolMin / csRayDir.z);
					float3 csAABBEnd	= csRayDir * (csZVolMax / csRayDir.z);
				
					// Xform to the current metavoxel's space
					float4x4 CameraToMetavoxel = mul(_WorldToMetavoxel, _CameraToWorldMatrix);
					float3 mvAABBStart	= mul(CameraToMetavoxel, float4(csAABBStart, 1));
					float3 mvAABBEnd	= mul(CameraToMetavoxel, float4(csAABBEnd, 1));

					float3 mvRay = mvAABBEnd - mvAABBStart;
					float totalRayMarchSteps = n * float(_NumSteps);
					float oneOverTotalRayMarchSteps = rcp(totalRayMarchSteps);

					float stepSize = sqrt(dot(mvRay, mvRay)) * oneOverTotalRayMarchSteps;
					float3 mvRayStep = mvRay * oneOverTotalRayMarchSteps;
					float3 mvRayDir = normalize(mvRay);

					float3 mvMin = float3(-0.5, -0.5, -0.5), mvMax = -1.0 * mvMin;				
					float t1, t2;
					Ray mvRay1;
					mvRay1.o = mvAABBStart;
					mvRay1.d = mvRayDir;
					bool intersects = IntersectBox(mvRay1, mvMin, mvMax, t1, t2);
					if (!intersects)					
						return red;
					
					// if the volume AABB's near plane is within the metavoxel, t1 will be negative. clamp to 0				
					int tstart = ceil(t1 / stepSize), tend = floor(t2 / stepSize);
					tstart = max(0, tstart);
					tend   = min(totalRayMarchSteps - 1, tend);

					float3 result = float3(0, 0, 0);
					float transmittance = 1.0f;
					float borderVoxelOffset = rcp(_NumVoxels) * _MetavoxelBorderSize;
					float3 mvRayPos = mvAABBStart + tend * mvRayStep;
					int samples = 0;
					int step;
					// Sample uniformly along the ray starting from the current metavoxel's exit index (along the ray), 
					// and moving towards the camera while stopping once we're no longer within the current metavoxel.
					// Blend the samples back-to-front in the process										
					for (step = tend; step >= tstart; step--) {			
						float3 samplePos = mvRayPos + 0.5; //[-0.5, 0.5] -->[0, 1]
						// the metavoxel texture's Z follows the light direction, while the actual metavoxel orientation is towards the light
						// see get_voxel_world_pos(..) in Fill Volume.shader ; we're mapping slice [0, n-1] to [+0.5, -0.5] in mv space
						samplePos.z = 1.0 - samplePos.z; 


						//// adjust for the metavoxel border -- the border voxels are only for filtering
						samplePos = samplePos * (1.0 - 2.0 * borderVoxelOffset) + borderVoxelOffset;  // [0, 1] ---> [offset, 1 - offset]

						// supply 0 derivatives when sampling -- this ensures that the loop doesn't have to unrolled
						// due to a gradient instruction (such as tex3D)
						float4 voxelColor = tex3D(_VolumeTexture, samplePos, float3(0,0,0), float3(0,0,0));
						float3 color = voxelColor.rgb;
						float  density = voxelColor.a;
						float blendFactor = rcp(1.0 + density);

						result.rgb = lerp(color, result.rgb, blendFactor);
						transmittance *= blendFactor;
						
						mvRayPos -= mvRayStep;
						samples++;
					}

					if (_ShowNumSamples == 1) {
						int stepstaken = samples;
						if (stepstaken < 2)
							return green;
						if (stepstaken < 10)
							return yellow;
						if (stepstaken < 25)
							return orange;
						return red;
					}

					return float4(result.rgb, 1 - transmittance);			
				} // frag

				ENDCG
			} // Pass
		}FallBack Off
}
