Shader "Custom/RayMarch Metavoxel" {
	Properties{
		_VolumeTexture("Metavoxel fill data", 3D) = "" {}
		_LightPropogationTexture("Light Propogation", 2D) = "" {}
		_NumVoxels("Num voxels in metavoxel", Float) = 8
		_NumSteps("Num steps in raymarch", Int) = 8
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

				sampler3D _VolumeTexture;
				sampler2D _LightPropogationTexture;

				// Metavoxel uniforms
				float4x4 _MetavoxelToWorld;
				float4x4 _WorldToMetavoxel;
				float3 _MetavoxelIndex;
				float3 _MetavoxelGridDim;
				float3 _MetavoxelSize;
				float _NumVoxels; // metavoxel's voxel dimensions

				// Camera uniforms
				float4x4 _CameraToWorld;
				float3 _CameraWorldPos;
				float _Fov;
				float _Near;
				float _Far;
				float2 _ScreenRes;

				// Ray march constants
				int _NumSteps;

				// tmp
				int _NumParticles;
				int _ShowPrettyColors;

				struct v2f {
					float4 pos : SV_POSITION;
					float3 worldPos : TEXCOORD;
				};

				v2f vert(appdata_base i) {
					// every vertex submitted is in a unit-metavoxel space
					// transform from model -> world -> eye -> proj space
					v2f o;
					o.pos = mul(mul(UNITY_MATRIX_VP, _MetavoxelToWorld), i.vertex); // clip space
					o.worldPos = mul(_MetavoxelToWorld, i.vertex); // world space
					return o;
				}

				// Fragment shader
				// For each fragment, we have to iterate through all the particles covered
				// by the MV and fill the voxel column by iterating through each voxel slice.
				// [todo] this can be parallelized.
				float4 frag(v2f i) : COLOR
				{
					if (_ShowPrettyColors == 1)
						return float4(_MetavoxelIndex.xyz * 0.3f, 0.7f);


					// Find ray direction from camera through this pixel
					// -- Find half width and height of the near plane in world units
					float screenHalfHeight = _Near * tan(radians(_Fov / 2));
					float screenHalfWidth = (_ScreenRes.x / _ScreenRes.y) * screenHalfHeight;

					// -- Normalize the pixel position to a [-1, 1] range to help find its world space position
					float2 pixelNormPos = (2 * i.pos.xy - _ScreenRes) / _ScreenRes; // [0, wh] to [-1, 1]
					float3 pixelWorldPos = _CameraWorldPos + mul(_CameraToWorld, float3(pixelNormPos * float2(screenHalfWidth, screenHalfHeight), _Near)); // pixel lies on the near plane
					float3 rayDir = normalize(i.worldPos - _CameraWorldPos);
					// Since we cull front-facing triangles, the geometry corresponding to this fragment is a back-facing one and thus
					// represents the ray's world space exit position for this metavoxel
					float3 rayExit = i.worldPos;

					// we need sampling points w.r.t mv, so may be, xforming to mv-space might be a better choice?
					float3 mvRayDir = normalize(mul(_WorldToMetavoxel, float4(rayDir, 0))); // w = 0 for vectors since you don't want them to tbe translated!
					float3 mvRayOrigin = mul(_WorldToMetavoxel, float4(pixelWorldPos, 1)); // w = 1 for points since they do need to be translated!
					float3 mvRayExit = mul(_WorldToMetavoxel, float4(rayExit, 1));

					bool eyeInsideMv = true;
					if (abs(mvRayOrigin.x) > 0.5 || abs(mvRayOrigin.y) > 0.5 || abs(mvRayOrigin.z) > 0.5)
						eyeInsideMv = false;

					// Find the first intersection of the ray with the metavoxel -- do we need to do this?
					// [todo] we could be inside metavoxel.. account for that...
					float4 result = float4(0, 0, 0, 0);

					int step;
					float3 mvRayPos = mvRayExit;
					float transmittance = 1.0f;
					int outsideCounter = 0;
					int transparentVoxel = 0;

					for (step = 0; step < 50; step++) {
						float blendFactor;

						if (abs(mvRayPos.x) > 0.5 || abs(mvRayPos.y) > 0.5 || abs(mvRayPos.z) > 0.5)
						{
							// point outside mv
							outsideCounter++;
						}
						else {
							// convert from mv space to sampling space, i.e., [-mvSize/2, mvSize/2] -> [0,1]
							//float3 samplePos = (2 * mvRayPos + _MetavoxelSize) / (2 * _MetavoxelSize);
							float3 samplePos = (2 * mvRayPos + 1.0) / 2.0; //[-0.5, 0.5] -->[0, 1]
							float4 voxelColor = tex3D(_VolumeTexture, float3(samplePos.x, samplePos.y, samplePos.z));

							if (voxelColor.a < 0.05)
								transparentVoxel++;

							// blending samples back-to-front, so use the `over` operator
							result.rgb = voxelColor.a * voxelColor.rgb + (1 - voxelColor.a) * result.rgb; // a1*C1 + (1 - a1)*C0  (C1,a1) over (C0,a0)
							transmittance *= (1 - voxelColor.a);

							// use the `under` operator to blend result
							//result.rgb += transmittance * voxelColor;						
							//transmittance *= (1 - voxelColor.a);
						}
						mvRayPos -= mvRayDir * (1/50.0);
					}

					return float4(result.rgb, 1 - transmittance);
					//return float4(outsideCounter / 50.0, 1 - (transparentVoxel / (50.0 - outsideCounter)), transparentVoxel/50.0 , 0.5); // visualize ray march & sampling behavior
				
				} // frag

					ENDCG
			} // Pass
		}FallBack Off
}
