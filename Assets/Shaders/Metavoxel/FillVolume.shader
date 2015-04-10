Shader "Custom/Fill Volume" {
Properties {
	_DisplacementTexture("Displaced sphere", Cube) = "white" {}
	_RampTexture("Ramp Texture", 2D) = "" {}
}
SubShader {
	Pass{
	Cull Off ZWrite Off ZTest Off
	CGPROGRAM
	#pragma target 5.0
	#pragma enable_d3d11_debug_symbols
	#pragma vertex vert_img		
	//#pragma vertex vert
	#pragma fragment frag
	#define NUM_VOXELS 32

	#include "UnityCG.cginc"
		
	// If filling the metavoxels via DrawMeshNow, we'll need to use a standard VS instead of the built-in vert_img
	// Note: DrawMeshNow doesn't work with multiple UAV writes. Hence going with Graphics.Blit in the main script
	//struct vs_input {
	//	float4 vertex : POSITION;
	//};

	//struct v2f {
	//	float4 pos : SV_POSITION;
	//};
		
	//v2f vert(vs_input i) {
	//	v2f o;
	//	o.pos = i.vertex;
	//	return o;
	//}

	// particle
	struct Particle {
		float4x4 mWorldToLocal;
		float3	mWorldPos;
		float	mRadius;
		float	mLifetimeOpacity; // [0.0, 1.0]
	};

	struct Voxel {
		half density; // affects opacity
		half ao; // affects color
	};
		
	// UAVs
	RWTexture3D<float4> volumeTex; // out
	RWTexture2D<float> lightPropogationTex; // in-out

	// Particles covering the current metavoxel
	StructuredBuffer<Particle> _Particles; // in

	// Textures
	samplerCUBE _DisplacementTexture;
	sampler2D _RampTexture;
	sampler2D _LightDepthMap;
		
	// Current metavoxel's constants
CBUFFER_START(MetavoxelConstants)
	float4x4 _MetavoxelToWorld;	
	float3 _MetavoxelIndex;
	int _NumParticles;		
CBUFFER_END
		
	// Uniforms over entire fill pass
	// -- light stuff
CBUFFER_START(LightConstants)
	float4x4 _WorldToLight;		
	float4x4 _LightProjection;
	float3 _LightForward; // worldspace
	float3 _LightColor;			
	float3 _AmbientColor;
	float _InitLightIntensity;
	float _NearZ, _FarZ;	
CBUFFER_END

	// -- metavoxel stuff
CBUFFER_START(VolumeConstants)
	float3 _MetavoxelGridDim;		
	float _NumVoxels; // (nv) each metavoxel is made up of nv * nv * nv voxels
	int _MetavoxelBorderSize;
	float _MetavoxelScaleZ;
CBUFFER_END
	
	// -- particle stuff
CBUFFER_START(ParticleConstants)
	float _OpacityFactor;
	int _FadeOutParticles;
	float _DisplacementScale;
	float _ParticleGreyscale;	
CBUFFER_END	

	// helper methods
	float4 
	get_voxel_world_pos(float2 svPos, float zSlice)
	{
		// svPos goes from [0, numVoxels]. transform to a [-0.5, 0.5] range w.r.t metavoxel centered at origin
		// the metavoxel grid follows the light space -- i.e, x0, y0, z0 is left-bot-front (and closest to the light)
		// see VolumetricParticleRenderer::UpdateMetavoxelPositions()
		float3 voxelPos = float3(svPos, zSlice);
		float4 normPos = float4((svPos - _NumVoxels/2) / _NumVoxels, (zSlice - _NumVoxels/2) / _NumVoxels, 1.0);

		// with all dimensions in normalizaed unit cube (metavoxel) space, transform to world space
		return mul(_MetavoxelToWorld, normPos);
	}


	void 
	compute_voxel_color(float3 psVoxelPos	/*voxel position in particle space*/, 
						float fadeFactor,		/*particle opacity -- particle fades away as it dies*/
						out Voxel v)
	{
		// sample the displacement noise texture for the particle
		half rawDisplacement = texCUBE(_DisplacementTexture,  2*psVoxelPos).x; // [todo] -- is the texcoord correct for cube sampling??

		// the scale factor can be used to weigh the above calculated noise displacement. 
		half netDisplacement = _DisplacementScale * rawDisplacement + (1.0 - _DisplacementScale); // disp. from center in the range [0, 1]

		float voxelParticleDistSq = dot(2 * psVoxelPos,  2 * psVoxelPos); // make it [0, 1]

		// how dense is this particle at the current voxel? (density falls quickly as we move to the outer surface of the particle)
		// voxelParticleDistSq < 0.7 * netDisplacement ==> baseDensity = 1, voxelParticleDistSq > netDisplacement ==> baseDensity = 0
		// 0.7 * netDisplacement < voxelParticleDistSq < netDisplacement ==> baseDensity ==> linear drop
		half baseDensity = smoothstep(netDisplacement, 0.7 * netDisplacement, voxelParticleDistSq); // [0.0, 1.0]
		half density = baseDensity *  _OpacityFactor * _OpacityFactor * _OpacityFactor; 
			
		// factor in the particle's lifetime fade factor
		if (_FadeOutParticles == 1)
			density *= fadeFactor;

		v.density = density;
		v.ao = netDisplacement;
	}


	// return a lightTransmission factor in the range [0.0, 1.0] for hardShadows .. softShadows
	half 
	shadowDropOff(int voxelIndex, int shadowIndex)
	{
		// the closer the voxel is to a shadow caster, the lesser light it receives and the "harder" the shadow casted on it looks
		// we know for sure that shadowIndex <= voxelIndex (since hte voxel is in shadow)
		 
		return ( 1 - rcp(1 + (voxelIndex - shadowIndex)) );
	}


	// Fragment shader fills a "voxel column" of the metavoxel's volume texture
	// For each voxel in the voxel column of our metavoxel, we iterate through all the displaced particles covered by the metavoxel and test for coverage. 
	// If a displaced particle covers the voxel's center, we calculate its contribution to "density" and "ao". 
	half4 
	frag(v2f_img i) : COLOR
	{
		int slice, pp; // loop counters  			
		Voxel voxelColumn[NUM_VOXELS]; // temporary storage for the voxel column associated with the current fragment

		// Iterate through the voxel column with the first particle, clearing the voxel if it isn't covered
		// (This saves us a conditional check for every other particle)
		float oneVoxelSize = _MetavoxelScaleZ / _NumVoxels; // assuming scale is same in X and Y below (this scale includes the border)
		float3 voxel0WorldPos = get_voxel_world_pos(i.pos.xy, 0);
		float3 voxelWorldPos = voxel0WorldPos;

		for (slice = 0; slice < _NumVoxels; slice++)
		{
			Particle p = _Particles[0];
				
			// get world pos for the current voxel accounting for the "scaled" metavoxel			
			float3 psVoxelPos = mul( p.mWorldToLocal, float4(voxelWorldPos, 1.0) ); // voxel position in particle space
			float dist2 = dot(psVoxelPos, psVoxelPos);
				
			if (dist2 <= 0.25) // 0.5 * 0.5 -- if the voxel center is within the particle it'd be less than 0.5 units away from the particle center in particle space
			{
				compute_voxel_color(psVoxelPos, p.mLifetimeOpacity, voxelColumn[slice]);				
			}
			else 
			{
				// particle doesn't cover voxel. clear it.
				voxelColumn[slice].density = 0.0;
				voxelColumn[slice].ao      = 0.0;
			}				

			voxelWorldPos += _LightForward * oneVoxelSize;
		}

		voxelWorldPos = voxel0WorldPos; // reset
		// Iterate through all the remaining particles for every voxel in the voxel column
		for (slice = 0; slice < _NumVoxels; slice++)
		{
			for (pp = 1; pp < _NumParticles; pp++)
			{
				Particle p = _Particles[pp];

				float3 psVoxelPos = mul( p.mWorldToLocal, float4(voxelWorldPos, 1.0) ); // voxel position in particle space
				float dist2 = dot(psVoxelPos, psVoxelPos);
				Voxel v;

				if (dist2 <= 0.25) // 0.5 * 0.5 -- if the voxel center is within the particle it'd be less than 0.5 units away from the particle center in particle space
				{
					compute_voxel_color(psVoxelPos, p.mLifetimeOpacity, v);

					voxelColumn[slice].density += v.density;
					voxelColumn[slice].ao	= max(voxelColumn[slice].ao, v.ao);
				}
			} // per particle

			voxelWorldPos += _LightForward * oneVoxelSize;
		} // per slice
			
		// Account for occlusion of the voxel column by objects in the scene
		float4 lsVoxel0 =  mul(_WorldToLight, get_voxel_world_pos(i.pos.xy, 0));							
		
		// don't need to convert voxel to light clip space since our shadow map is constrained to the exact dimensions of a "slice" of the volume grid			
		float2 lsTexCoord = (i.pos.xy + _MetavoxelIndex.xy * _NumVoxels) / (_MetavoxelGridDim.xy * _NumVoxels);
		// don't need to invert the y component of UV (even though Unity renders to the depth texture with Y inverted).
		float d = tex2D(_LightDepthMap, lsTexCoord); // [0,1]						
		float a = rcp(_FarZ - _NearZ), b = -_NearZ * a;			
		float lsSceneDepth = (d - b) * rcp(a);
		float lsVoxelColumnStart = lsVoxel0.z;

		int shadowIndex = (lsSceneDepth - lsVoxelColumnStart) / (oneVoxelSize);	

		// transmitted light is used to shade a voxel (and is attenuated by density or occlusion)
		float transmittedLight = (_MetavoxelIndex.z == 0)? _InitLightIntensity  :  lightPropogationTex[i.pos.xy];
		// propagated light is what ends up written to the light propagation texture (duh!). it represents the amount of light that made it through the volume
		// prior to occlusion. this way, we can project the light propagation texture on to the scene to correctly influence lighting (and thus have shadows cast by the volume)
		float propagatedLight = transmittedLight;
		float diffuseColor = _ParticleGreyscale; // constant "color" if not emissive
		int borderVoxelIndex = _NumVoxels - _MetavoxelBorderSize;

		for (slice = 0; slice < borderVoxelIndex; slice++)
		{
			bool inShadow = (slice >= shadowIndex);

			if (inShadow) {
				//transmittedLight = propagatedLight * shadowDropOff(slice, shadowIndex);
				transmittedLight = 0.0;
			}
			else
				propagatedLight = transmittedLight;

			half3 finalColor = diffuseColor * _LightColor * transmittedLight /* direct lighting */ +
								(_AmbientColor * voxelColumn[slice].ao);	  /* indirect lighting */

			transmittedLight *= rcp(1.0 + voxelColumn[slice].density);

			volumeTex[int3(i.pos.xy, slice)]	= half4(finalColor, voxelColumn[slice].density);
		}

		lightPropogationTex[i.pos.xy] = propagatedLight;

		// go over border voxels in the "far" end of the voxel column (this can be simplified if border is restricted to 0 or 1)
		// light transmitted by the "far border voxels" isn't written to the light propagation texture to ensure correct light transmittance to
		// the "near border voxels" of the voxel column behind this one 

		for(slice = borderVoxelIndex; slice < _NumVoxels; slice++) 
		{
			bool inShadow = (slice >= shadowIndex);

			if (inShadow)
				transmittedLight = 0.0;
						
			half3 finalColor = diffuseColor * _LightColor * transmittedLight /* direct lighting */ +
								(_AmbientColor * voxelColumn[slice].ao);	  /* indirect lighting */

			transmittedLight *= rcp(1.0 + voxelColumn[slice].density);

			volumeTex[int3(i.pos.xy, slice)]	= half4(finalColor, voxelColumn[slice].density);
		}						

		/* this fragment shader does NOT return anything. it's merely used for filling a voxel column with color and density values by propagating light through it*/
		discard;
		return half4(d,  lsVoxel0.z, lsSceneDepth, 1.0f);	// return statement is required by the compiler					
	}

	ENDCG
	}
}FallBack Off
}



			