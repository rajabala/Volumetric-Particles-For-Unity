Shader "DX11/PSFill2D3DTexture" {
	Properties{
	}
	SubShader{
	Pass{
		Cull Off ZWrite Off ZTest Always Fog{ Mode Off }

		CGPROGRAM
#pragma target 5.0

#pragma vertex vert_img
#pragma fragment frag

#include "UnityCG.cginc"

		RWTexture3D<float4> volumeTex; // 4 channel format
		RWTexture2D<float4> planeTex; // 4 channel format

		float _volDepth;
		float _time;

		float4 frag(v2f_img i) : COLOR
		{
			int zz;
			for (zz = 0; zz < _volDepth; zz++) {
				volumeTex[int3(i.pos.xy, zz)] = float4(i.pos.x / _volDepth,
													   i.pos.y * sin(_time) / _volDepth,
													   zz * cos(_time) / _volDepth, 1.0f);
			}

			planeTex[int2(i.pos.xy)] = float4(i.pos.x * sin(_time) / _volDepth, 
											  i.pos.y / _volDepth, 
											  cos(_time), 1.0f);
			
			discard; // we don't use the camera's targetTexture
			return float4(0.0f, 0.2f, 0.9f, 1.0f);
		}

		ENDCG

		}
	}

	Fallback Off
}
