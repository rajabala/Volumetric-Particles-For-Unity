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

Shader "Hidden/Composite Particles" {
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
