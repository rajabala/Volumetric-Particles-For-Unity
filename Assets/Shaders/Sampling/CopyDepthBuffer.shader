// Shader that copies the currently bound depth buffer into a texture
// Use via Graphics.Blit(RenderTexture.active, <rtDepth>, <material>)
Shader "Hidden/CopyDepthBuffer" {
SubShader {
Pass {
CGPROGRAM
#include "UnityCG.cginc"
#pragma target 4.0
#pragma vertex vert_img
#pragma fragment frag

sampler2D _MainTex;
sampler2D _CameraDepthTexture;
	
float4 frag(v2f_img i) : COLOR
{
	float2 uv;
	uv = 1 - i.pos.xy / _ScreenParams.xy; // weird that X also needs to be flipped..	
	float d = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
	return float4(d, d, d, 1);
}
ENDCG
}
} 
FallBack Off
}
