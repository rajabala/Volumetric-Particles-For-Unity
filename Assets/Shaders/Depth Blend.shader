Shader "Custom/Depth Blend" {
	Properties {
		_MainTex ("Particle Texture", 2D) = "white" {}
	}
	SubShader {
		Tags{ "Queue" = "Transparent"}
		Pass{
			Cull Off ZWrite Off ZTest Always // ZTest Always => Don't perform ZTest
			// Syntax: Blend SrcFactor DstFactor, SrcFactorA DstFactorA
			Blend One OneMinusSrcAlpha, One One
			BlendOp Add

			CGPROGRAM
#pragma vertex vert_img
#pragma fragment frag
#include "UnityCG.cginc"

			sampler2D _MainTex;

			float4 frag(v2f_img i) : COLOR{
				float4 c = tex2D(_MainTex, i.uv);
				return c;
			}
				ENDCG
		}
	}FallBack Off
}
