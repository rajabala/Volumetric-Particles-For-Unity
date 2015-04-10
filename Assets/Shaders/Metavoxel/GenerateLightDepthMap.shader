//
// Permission is granted to use, copy, distribute and prepare derivative works of this
// software for any purpose and without fee, provided, that the above copyright notice
// and this statement appear in all copies.  Intel makes no representations about the
// suitability of this software for any purpose.  THIS SOFTWARE IS PROVIDED "AS IS."
// INTEL SPECIFICALLY DISCLAIMS ALL WARRANTIES, EXPRESS OR IMPLIED, AND ALL LIABILITY,
// INCLUDING CONSEQUENTIAL AND OTHER INDIRECT DAMAGES, FOR THE USE OF THIS SOFTWARE,
// INCLUDING LIABILITY FOR INFRINGEMENT OF ANY PROPRIETARY RIGHTS, AND INCLUDING THE
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE.  Intel does not
// assume any responsibility for any errors which may appear in this software nor any
// responsibility to update it.
//--------------------------------------------------------------------------------------

Shader "Hidden/Generate Light Depth Map" {
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