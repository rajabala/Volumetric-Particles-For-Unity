Shader "Custom/Depth Blend" {
	Properties {
		_MainTex ("Base (RGB)", 2D) = "white" {}
	}
	SubShader {
		Tags { "RenderType"="Opaque" }
		Cull Off ZWrite Off ZTest Always // ZTest Always => Don't perform ZTest
		// Syntax: Blend SrcFactor DstFactor, SrcFactorA DstFactorA
		Blend One OneMinusSrcAlpha, One One
		BlendOp Add
		LOD 200
		
		CGPROGRAM
		#pragma surface surf Lambert

		sampler2D _MainTex;
		//sampler2D _DepthTex;

		struct Input {
			float2 uv_MainTex;
		};

		void surf (Input IN, inout SurfaceOutput o) {
			half4 c = tex2D (_MainTex, IN.uv_MainTex);
			//half4 d = tex2D(_DepthTex, IN.uv_MainTex);
			o.Albedo = c.rgb;
			o.Alpha = c.a;
		}
		ENDCG
	} 
	FallBack "Diffuse"
}
