Shader "Custom/Generate Light Depth Map" {
	Properties {
	}
	SubShader {
		Pass{
		Cull Front ZWrite On ZTest Less

		CGPROGRAM
		#pragma target 4.0		
		#pragma enable_d3d11_debug_symbols
		#pragma vertex vert		
		#pragma fragment frag
		
		#include "UnityCG.cginc"

		struct v2f {
			float4 pos : SV_POSITION;
		};


		v2f vert(appdata_base i) 
		{
			v2f o;					
			o.pos = mul(UNITY_MATRIX_MVP, i.vertex);	
			return o;
		}

		float4 frag(v2f i) : COLOR		
		{					
			return float4(1,1,1,1); // Invoking this shader with only a depth texture bound makes this line useless (i.e., only the depth is written to. Color isn't even bound)
		}

		ENDCG
		} // Pass
	}FallBack Off // Subshader
} // Shader			