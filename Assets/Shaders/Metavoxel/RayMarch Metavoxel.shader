Shader "Custom/RayMarch Metavoxel" {
		Properties{
			_VolumeTexture("Metavoxel fill data", 3D) = "" {}
			_LightPropogationTexture("Light Propogation", 2D) = "" {}
			_NumVoxels("Num voxels in metavoxel", Float) = 8				
			_NumSteps("Num steps in raymarch", Int) = 8	
		}
		SubShader
		{
		//Tags {"Queue" = "Geometry"}
		Pass
		{
			Cull Front ZWrite Off ZTest Less			
			// Syntax: Blend SrcFactor DstFactor, SrcFactorA DstFactorA
			Blend OneMinusDstAlpha One, One One // Back to Front blending (blend over)
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
			float3 _CameraWorldPos;
			float _Fov;
			float _Near;
			float _Far;
			float2 _ScreenRes;

			// Ray march constants
			int _NumSteps;

			int _NumParticles;

			struct v2f {
				float4 pos : SV_POSITION;
				float3 worldPos : TEXCOORD;
			};

			v2f vert(appdata_base i) {
				// transform metavoxel from model -> world -> eye -> proj space
				v2f o;								
				o.pos = mul( mul( UNITY_MATRIX_VP , _MetavoxelToWorld), i.vertex);
				o.worldPos = mul(_MetavoxelToWorld, i.vertex);
				return o;
			}

			// Fragment shader
			// For each fragment, we have to iterate through all the particles covered
			// by the MV and fill the voxel column by iterating through each voxel slice.
			// [todo] this can be parallelized.
			float4 frag(v2f i) : COLOR
			{
				//return float4(_MetavoxelIndex.xyz * 0.3f, 0.7f);
				// Find ray direction from camera through this pixel
				// -- Find width and height of the near plane in world units
				float screenHalfHeight = 2 * _Near * tan(radians(_Fov/2));
				float screenHalfWidth = (_ScreenRes.x/_ScreenRes.y) * screenHalfHeight;

				float2 pixelNormPos = (2 * i.pos.xy - _ScreenRes) / (2 * _ScreenRes); // [0, w] to [-1, 1]
				float3 pixelWorldPos = _CameraWorldPos + float3(pixelNormPos * float2(screenHalfWidth, screenHalfHeight), _Near);				
				float3 rayDir = normalize(pixelWorldPos - _CameraWorldPos);	
				float3 rayExit = i.worldPos;
				
				// we need sampling points w.r.t mv, so may be, xforming to mv might be a better choice?
				float4 mvRayDir = mul(_WorldToMetavoxel, float4(rayDir, 0)); // w = 0 for vectors since you don't want them to tbe translated!
				float4 mvRayOrigin = mul(_WorldToMetavoxel, float4(_CameraWorldPos, 1)); // w = 1 for points since they do need to be translated!
				float4 mvRayExit = mul(_WorldToMetavoxel, float4(rayExit, 1));

				bool eyeInsideMv = true;
				if (abs(mvRayOrigin.x) > _MetavoxelSize.x/2 || abs(mvRayOrigin.y) > _MetavoxelSize.y/2 || abs(mvRayOrigin.z) > _MetavoxelSize.z/2)
					eyeInsideMv = false;
				
				if (!eyeInsideMv) {
					// find first intersection point of ray & mv [todo -- bad approx below]
					mvRayOrigin = mvRayExit - mvRayDir * _MetavoxelSize.x;
				}			
				
				

				// Find intersection of the aaaaray with the metavoxel -- do we need to do this?
				// isn't the interpolated worldPos from VS the point of exit?
				// [todo] we could be inside metavoxel.. account for that...
				float4 finalColor = float4(0,0,0,0);
				
				int step;
				float3 mvRayPos = mvRayOrigin;
				for(step = 0; step < _NumSteps; step++) {
					mvRayPos += mvRayDir * (_MetavoxelSize.x / _NumSteps);
					// convert from mv space to sampling space, i.e., [-mvSize/2, mvSize/2] -> [0,1]
					float3 samplePos = (mvRayPos + _MetavoxelSize)/(2 * _MetavoxelSize);
					finalColor += tex3D(_VolumeTexture, samplePos); // [todo] is addition the best "combine" operation for each ray step color?

					

				}

				return finalColor;
				
				

	
			//	return float4(1.0f, 1.0f, 1.0f, 1.0f);
			//	return float4(_MetavoxelIndex.xyz * 0.3f, 0.7f);
			}
					
			ENDCG
		}
	}FallBack Off
}
