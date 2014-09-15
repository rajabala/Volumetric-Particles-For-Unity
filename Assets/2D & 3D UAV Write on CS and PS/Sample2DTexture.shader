
Shader "DX11/Sample 2D Texture" {
	Properties{
		_Plane("Plane Texture", 2D) = "blue" {}
	}
		SubShader{
		Pass{

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

			sampler2D _Plane;

			float4 frag(ps_input i) : COLOR
			{
				//return half4(0.3f, 0.0f, 0.5f, 1.0f);
				return tex2D(_Plane, i.uv);
			}

				ENDCG

		}
	}

	Fallback Off
}
