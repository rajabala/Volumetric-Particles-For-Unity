Shader "Custom/Fill Volume" {
	Properties {
		_DisplacementTexture("Displaced sphere", Cube) = "white" {}
		_RampTexture("Ramp Texture", 2D) = "" {}
		_NumVoxels("Num voxels in metavoxel", Float) = 8
		_InitLightIntensity("Init light intensity", Float) = 1.0
	}
	SubShader {
		Pass{
		Cull Off ZWrite Off ZTest Always
		CGPROGRAM
// Upgrade NOTE: excluded shader from DX11 and Xbox360; has structs without semantics (struct v2f members pos)	
		#pragma target 5.0
		#pragma exclude_renderers flash gles opengl
		#pragma enable_d3d11_debug_symbols
		#pragma vertex vert_img
		#pragma fragment frag

		#include "UnityCG.cginc"

		samplerCUBE _DisplacementTexture;
		sampler2D _RampTexture;

		// particle
		struct particle {
			float4x4 mWorldToLocal;
			float3	mWorldPos;
			float	mRadius; // 
			int		mIndex;
		};
		
		// UAVs
		RWTexture3D<float4> volumeTex;
		RWTexture2D<float> lightPropogationTex;

		// Buffers
		StructuredBuffer<particle> _Particles;						
		
		// Metavoxel uniforms
		float4x4 _MetavoxelToWorld;	
		float4x4 _WorldToLight;
		float3 _MetavoxelIndex;
		float3 _MetavoxelGridDim;
		float _NumVoxels; // metavoxel's voxel dimensions
		int _NumParticles;

		// Other uniforms
		float _InitLightIntensity;

		// helper methods
		float4 get_voxel_world_pos(float2 svPos, float zSlice)
		{
			// svPos goes from [0, numVoxels]. transform to a [-0.5, 0.5] range w.r.t metavoxel centered at origin
			// similarly for zSlice. once in normalizaed unit cube space, transform to world space using
			// the metavoxelToWorld matrix
			float3 voxelPos = float3(svPos, zSlice);
			float4 normPos = float4((voxelPos - _NumVoxels/2) / _NumVoxels, 1.0);

			return mul(normPos, transpose(_MetavoxelToWorld));
		}


		// Fragment shader
		// For each fragment, we have to iterate through all the particles covered
		// by the MV and fill the voxel column by iterating through each voxel slice.
		// [todo] this can be parallelized.
		float4 frag(v2f_img i) : COLOR
		{
			int slice, pp;

			//// i.pos.xy represents the pixel position within the metavoxel grid facing the light.
			//// convert to a [0, _NumVoxels] range for use within the metavoxel
			//float2 svpos = i.pos.xy - float2(_MetavoxelIndex.x * _NumVoxels, _MetavoxelIndex.y * _NumVoxels);
			float lightPassthrough;

			if (_MetavoxelIndex.z == 0.0)
				lightPassthrough = _InitLightIntensity;
			else
				lightPassthrough = lightPropogationTex[int2(i.pos.xy + _MetavoxelIndex.xy * _NumVoxels)];

			float4 clearColor = float4(0.0f, 0.0f, 0.0, 0);
			float4 voxelColor;
			float4 particleColor;

			// The indices to the UAV are in the range [0, width),  [0, height), [0, depth]
			// i.pos is SV_POSITION, that gives us the center of the pixel position being shaded
			// i.e., i.pos is already in "image space"
			for (slice = 0; slice < _NumVoxels; slice++) {
				voxelColor = clearColor;

				for (pp = 0; pp < _NumParticles; pp++) {
					float4 voxelWorldPos = get_voxel_world_pos(i.pos.xy, slice);
					float3 vs = _Particles[pp].mWorldPos - voxelWorldPos;
					float ri = _Particles[pp].mRadius / 4; // inner radius of sphere
					float ro = _Particles[pp].mRadius; // outer radius of sphere			
					float dSqVoxelSphere = dot(vs, vs);

					// outer coverage test -- check if sphere's outer radius covers the voxel center
					if (dSqVoxelSphere <= (ro * ro)) {						
						// use perlin noise to "displace" the sphere  [todo]

						
						// find sampling position for cube map in the particle's local space
						float3 texCoord = mul(-vs, _Particles[pp].mWorldToLocal);						
						float4 cubeColor = texCUBE(_DisplacementTexture, texCoord);

						// d = displacement of sphere from center along the voxel-sphere direction
						float d = ri + (ro - ri) * cubeColor.x;//  ro - cubeColor.x * (ro - ri); // 0.0 ---> ro, 1.0 --> ri, sample --> ?

						particleColor = tex2D(_RampTexture, float2(d / ro, 1.0));

						// actual coverage test -- check if the displaced sphere intersects voxel center
						if ((d*d) >= dSqVoxelSphere) {
							// use ramp texture to "color" voxel based on the displacement of the sphere from the center
							// [interior] white-yellow-red-black [surface]
							particleColor.xyz = tex2D(_RampTexture, float2(d/ro,1.0)); 
							particleColor.a = 1.0; // cubeColor.x;
				
							//Blend
							voxelColor.rgb += max((1 - voxelColor.a), 0) * particleColor.rgb;
							voxelColor.a += particleColor.a;	
							lightPassthrough -= particleColor.a;

						} // actual coverage test

					} // fast coverage test

				} // per particle

				volumeTex[int3(i.pos.xy, slice)] = voxelColor;

			} // per slice

			lightPropogationTex[int2(i.pos.xy + _MetavoxelIndex.xy * _NumVoxels)] = lightPassthrough;
			
			discard;
			return float4(1.0f, 0.0f, 1.0f, 1.0f);
		}


		ENDCG
		}
	}FallBack Off
}