Shader "Custom/RayMarch Metavoxel" {
		Properties{
			_VolumeTexture("Metavoxel fill data", 3D) = "" {}
			_LightPropogationTexture("Light Propogation", 2D) = "" {}
			_NumVoxels("Num voxels in metavoxel", Float) = 8				
		}
		SubShader
		{
			//Tags {"Queue" = "Geometry"}
			Pass
			{
				//Cull Off ZWrite Off ZTest Always
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
				float4x4 _WorldToMainCamera;
				float4x4 _Projection;
				float3 _MetavoxelIndex;
				float3 _MetavoxelGridDim;
				float _NumVoxels; // metavoxel's voxel dimensions
				int _NumParticles;

				struct v2f {
					float4 pos : SV_POSITION;
				};

				v2f vert(appdata_base i) {
					// transform metavoxel from model -> world -> eye -> proj space
					v2f o;								
					o.pos = mul( mul( UNITY_MATRIX_VP , _MetavoxelToWorld), i.vertex);
					return o;
				}

				// helper methods
				float4 get_metavoxel_world_pos(float2 svPos, float zSlice)
				{
					// need ray start point and direction
					//
					/// todo
				}

				// Vertex shader



				// Fragment shader
				// For each fragment, we have to iterate through all the particles covered
				// by the MV and fill the voxel column by iterating through each voxel slice.
				// [todo] this can be parallelized.
				float4 frag(v2f i) : COLOR
				{
				//	return float4(1.0f, 1.0f, 1.0f, 1.0f);
					return float4(_MetavoxelIndex.xyz * 0.3f, 0.7f);
				}
					
				ENDCG
			}
		}FallBack Off
}
