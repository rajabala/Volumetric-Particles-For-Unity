Shader "Custom/RayMarchMetavoxelBlendOver" {
	Properties{
		_VolumeTexture("Metavoxel fill data", 3D) = "" {}
		_LightPropogationTexture("Light Propogation", 2D) = "" {}
		_NumSteps("Num steps in raymarch", Int) = 50
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

				sampler3D _VolumeTexture;
				sampler2D _LightPropogationTexture;

				// Raymarch pass constants
				float _NumVoxels; // metavoxel's voxel dimensions
				int _MetavoxelBorderSize;
				//float3 _MetavoxelScale;

				// Metavoxel uniforms
				float4x4 _MetavoxelToWorld;
				float4x4 _WorldToMetavoxel;
				//float3 _MetavoxelIndex;
				//float3 _MetavoxelGridDim;
				float _ParticleCoverageRatio;

				// Camera uniforms
				float4x4 _CameraToWorldMatrix; // need to explicitly define this to get the main camera's matrices
				float4x4 _WorldToCameraMatrix;
				float3 _CameraWorldPos;
				float _Fov;
				float _Near;
				float _Far;
				float2 _ScreenRes;

				// Ray march constants
				int _NumSteps;
				float3 _AABBMin;
				float3 _AABBMax;

				// tmp
				int _ShowPrettyColors;

				struct v2f {
					float4 pos : SV_POSITION;
					float3 worldPos : TEXCOORD;
				};

				v2f	
				vert(appdata_base i) {
					// every vertex submitted is in a unit-metavoxel space
					// transform from model -> world -> eye -> proj space
					v2f o;
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


				// Fragment shader
				// For each fragment, we have to iterate through all the particles covered
				// by the MV and fill the voxel column by iterating through each voxel slice.
				// [todo] this can be parallelized.
				float4 
				frag(v2f i) : COLOR
				{					
					if (_ShowPrettyColors == 1) // Color metavoxels that are covered by particles 
					{
						if (_ParticleCoverageRatio < 0.25)
							return green;
						else if (_ParticleCoverageRatio < 0.5)
							return yellow;
						else if (_ParticleCoverageRatio < 0.75)
							return orange;
						else
							return red;
					}


					// Find ray direction from camera through this pixel
					// -- Find half width and height of the near plane in world units
					float screenHalfHeight = _Near * tan(radians(_Fov / 2));
					float screenHalfWidth  = (_ScreenRes.x / _ScreenRes.y) * screenHalfHeight;

					// -- Normalize the pixel position to a [-1, 1] range to help find its world space position
//					float2 pixelNormPos = (2 * i.pos.xy - _ScreenRes) / _ScreenRes; // [0, wh] to [-1, 1]
//					float3 pixelWorldPos = _CameraWorldPos + mul(_CameraToWorldMatrix, float3(pixelNormPos * float2(screenHalfWidth, screenHalfHeight), _Near)); // pixel lies on the near plane

					// Since we cull front-facing triangles, the geometry corresponding to this fragment is a back-facing one and thus
					// represents the ray's world space exit position for this metavoxel
					float3 rayDir = normalize(i.worldPos - _CameraWorldPos);
					
					Ray csRay; // camera space
					csRay.o = float3(0, 0, 0); // camera is at the origin in camera space.
					csRay.d = normalize(mul(_WorldToCameraMatrix, float4(rayDir, 0)));			

					float tnear, tfar;
					bool rayVolumeIntersects = IntersectBox(csRay, _AABBMin, _AABBMax, tnear, tfar);

					float3 tmvexit = mul(_WorldToCameraMatrix, float4(i.worldPos, 1)) / csRay.d;
					float stepSize = abs((tfar - tnear) / (float)(_NumSteps)); 
					int exitIndex = floor((tmvexit.x - tnear) / stepSize);

					float3 result = float3(0, 0, 0);
					float transmittance = 1.0f;
					int step;
					float4x4 CameraToMetavoxel = mul(_WorldToMetavoxel, _CameraToWorldMatrix);
					float3 csRayPos = (tnear + stepSize*exitIndex) * csRay.d;
					[unroll(64)]
					for (step = exitIndex; step >= 0; step--) {
						// convert from mv space to sampling space, i.e., [-mvSize/2, mvSize/2] -> [0,1]
						float3 mvRayPos = mul(CameraToMetavoxel, float4(csRayPos, 1));
						if (abs(mvRayPos.x) > 0.5 || abs(mvRayPos.y) > 0.5 || abs(mvRayPos.z) > 0.5)
						{
							break;  // point outside mv
						}

						float3 samplePos = (2 * mvRayPos + 1.0) / 2.0; //[-0.5, 0.5] -->[0, 1]
						// adjust for the metavoxel border -- the border voxels are only for filtering
						float borderVoxelOffset = _MetavoxelBorderSize / _NumVoxels; // [0, 1] ---> [offset, 1 - offset]

						samplePos = clamp(samplePos, borderVoxelOffset, 1.0 - borderVoxelOffset);

						float4 voxelColor = tex3D(_VolumeTexture, samplePos);

						// blending individual samples back-to-front, so use the `over` operator
						result.rgb = voxelColor.a * voxelColor.rgb + (1 - voxelColor.a) * result.rgb; // a1*C1 + (1 - a1)*C0  (C1,a1) over (C0,a0)
						transmittance *= (1 - voxelColor.a);

						csRayPos -= (stepSize * csRay.d);
					}

					return float4(result.rgb, 1 - transmittance);
				
				} // frag

					ENDCG
			} // Pass
		}FallBack Off
}
 