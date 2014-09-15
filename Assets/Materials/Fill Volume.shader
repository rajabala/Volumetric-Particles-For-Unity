Shader "Custom/Fill Volume" {
	Properties {
		//_ParticleDisplacement("Displaced sphere", Cube) = "white" {}
		_NumVoxels("Num voxels in metavoxel", Float) = 8
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

		//samplerCUBE _ParticleDisplacement;			

		// particle
		struct particle {
			float4x4 mParticleToWorld;
			float radius;			
		};
		
		struct cbLight {
		};

		// UAVs
		RWTexture3D<float4> volumeTex;
		RWTexture2D<float4> lightPropogationTex;

		// Buffers
		StructuredBuffer<particle> particles;						
		
		// constants
		float4x4 _ModelToWorld;	
		float4x4 _WorldToLight;
		float4x4 _LightOrthoProj;
		Vector _MetavoxelIndex;
		float _NumVoxels; // metavoxel's voxel dimensions


		// For each fragment, we have to iterate through all the particles covered
		// by the MV and fill the voxel column by iterating through each voxel slice.
		// [todo] this can be parallelized.
		float4 frag(v2f_img i) : COLOR
		{
			int slice;
					
			// The indices to the UAV are in the range [0, width),  [0, height), [0, depth]
			// i.pos is SV_POSITION, that gives us the center of the pixel position being shaded
			// i.e., i.pos is already in "image space"
			for (slice = 0; slice < 32; slice++) {
				volumeTex[int3(i.pos.xy, slice)] = float4(1.0f, 0.5f, 0.0f, 1.0f);
			}

			lightPropogationTex[int2(i.pos.xy + _MetavoxelIndex.xy * _NumVoxels)] = float4(1.0f, 0.0f, 1.0f, 1.0f);
			
			discard;
			return float4(1.0f, 0.0f, 0.0f, 1.0f);
		}
		ENDCG
		}
	}FallBack Off
}