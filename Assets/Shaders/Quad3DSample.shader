
Shader "Custom/Quad Sample 3D Texture" {
	Properties{
		_Volume("Volume Texture", 3D) = "blue" {}
		_Slice("Slice number", Int) = 0
		_NumVoxels("Num Voxels per Metavoxel dir", Int) = 0
	}
		SubShader{
			Tags{ "RenderType" = "Transparent" }
		Pass{
			Cull Off ZWrite Off
			Blend SrcAlpha OneMinusSrcAlpha
			CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma exclude_renderers flash gles

#include "UnityCG.cginc"

			struct vs_input {
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct ps_input {
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
			};


			ps_input vert(vs_input v)
			{
				ps_input o;
				o.pos = mul(UNITY_MATRIX_MVP, v.vertex);
				o.uv = v.uv;
				return o;
			}

			sampler3D _Volume;
			int _Slice;
			int _NumVoxels;

			float4 frag(ps_input i) : COLOR
			{
				//return fixed4(0.6f, 0.60f, 0.0f, 1.0f);
				return tex3D(_Volume, float3(i.uv, _Slice / (float) _NumVoxels));
			}

				ENDCG

		}
	}

	Fallback Off
}
