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
		#pragma exclude_renderers flash gles opengl
		#pragma enable_d3d11_debug_symbols
		#pragma vertex vert_img
		#pragma fragment frag
		#define NUM_VOXELS 32

		#include "UnityCG.cginc"


		// particle
		struct Particle {
			float4x4 mWorldToLocal;
			float3	mWorldPos;
			float	mRadius;
			float	mOpacity; // [0.0, 1.0]
		};

		struct Voxel {
			float density; // affects opacity
			float ao; // affects color
		};
		
		// UAVs
		RWTexture3D<float4> volumeTex; // out
		RWTexture2D<float> lightPropogationTex; // in-out

		// Buffers
		StructuredBuffer<Particle> _Particles; // in

		// Textures
		samplerCUBE _DisplacementTexture;
		sampler2D _RampTexture;
		
		// Metavoxel specific uniforms
		float4x4 _MetavoxelToWorld;	
		float4x4 _WorldToLight; // [unused]
		float3 _MetavoxelIndex;
		int _NumParticles;

		// Uniforms over entire fill pass
		float _NumVoxels; // (nv) each metavoxel is made up of nv * nv * nv voxels
		float _InitLightIntensity;
		float _LightColor;
		float _OpacityFactor;
		float3 _MetavoxelGridDim;
		float _DisplacementScale;

		// helper methods
		float4 
		get_voxel_world_pos(float2 svPos, float zSlice)
		{
			// svPos goes from [0, numVoxels]. transform to a [-0.5, 0.5] range w.r.t metavoxel centered at origin
			// since we xform from mv to world space, we need to ensure zSlice = 0 is the closest to the light (+Z in metavoxel space)
			// with all dimensions in normalizaed unit cube (metavoxel) space, transform to world space
			float3 voxelPos = float3(svPos, zSlice);
			float4 normPos = float4((svPos - _NumVoxels/2) / _NumVoxels, (_NumVoxels/2 - zSlice) / _NumVoxels, 1.0);

			return mul(_MetavoxelToWorld, normPos);
		}


		void 
		compute_voxel_color(float3 psVoxelPos /*voxel position in particle space*/, 
							float opacity, /*particle opacity -- particle fades away as it dies*/
							out Voxel v)
		{
			// sample the displacement noise texture for the particle
			float rawDisplacement = texCUBE(_DisplacementTexture,  2*psVoxelPos).x; // [todo] -- is the texcoord correct for cube sampling??

			// the scale factor can be used to weigh the above calculated noise displacement. 
			float netDisplacement = _DisplacementScale * rawDisplacement + (1.0 - _DisplacementScale); // disp. from center in the range [0, 1]

			float voxelParticleDistSq = dot(2 * psVoxelPos,  2 * psVoxelPos); // make it [0, 1]
			float baseDensity = smoothstep(netDisplacement, 0.7 * netDisplacement, voxelParticleDistSq); // how dense is this particle at the current voxel? (density decreases as we move to the edge of the particle)
			float density = baseDensity * opacity * _OpacityFactor;

			v.density = density;
			v.ao = netDisplacement;
		}

		// Fragment shader fills a "voxel column" of the metavoxel's volume texture
		// Iterate through all the particles covered by the MV and fill the voxel column by iterating through each voxel slice.
		float4 
		frag(v2f_img i) : COLOR
		{
			int slice, pp;
			float lightIncidentOnVoxel, lightIncidentOnPreviousVoxel;

			lightIncidentOnPreviousVoxel = lightIncidentOnVoxel = _InitLightIntensity * lightPropogationTex[int2(i.pos.xy + _MetavoxelIndex.xy * _NumVoxels)];

			float3 ambientColor = float3(0.2, 0.2, 0.0);
			
			Voxel voxelColumn[NUM_VOXELS]; // temporary storage for the voxel column associated with the current fragment

			// Iterate through the voxel column with the first particle, clearing the voxel if it isn't covered
			// (This saves us a conditional check for every other particle)
			for (slice = 0; slice < _NumVoxels; slice++)
			{
				Particle p = _Particles[0];
				
				// get world pos for the current voxel accounting for the "scaled" metavoxel
				float4 voxelWorldPos = get_voxel_world_pos(i.pos.xy, slice);

				float3 psVoxelPos = mul(p.mWorldToLocal, voxelWorldPos); // voxel position in particle space
				float dist2 = dot(psVoxelPos, psVoxelPos);
				
				if (dist2 < 0.25) // 0.5 * 0.5 -- if the voxel center is within the particle it'd be less than 0.5 units away from the particle center in particle space
				{
					compute_voxel_color(psVoxelPos, p.mOpacity, voxelColumn[slice]);				
				}
				else 
				{
					// particle doesn't cover voxel. clear it.
					voxelColumn[slice].density = 0.0;
					voxelColumn[slice].ao      = 0.0;
				}				
			}


			// i.pos is SV_POSITION, that gives us the center of the pixel position being shaded
			for (slice = 0; slice < _NumVoxels; slice++)
			{

				float4 voxelWorldPos = get_voxel_world_pos(i.pos.xy, slice);
				
				for (pp = 1; pp < _NumParticles; pp++)
				{
					Particle p = _Particles[pp];

					float3 psVoxelPos = mul(p.mWorldToLocal, voxelWorldPos); // voxel position in particle space
					float dist2 = dot(psVoxelPos, psVoxelPos);
					Voxel v;

					if (dist2 < 0.25) // 0.5 * 0.5 -- if the voxel center is within the particle it'd be less than 0.5 units away from the particle center in particle space
					{
						compute_voxel_color(psVoxelPos, p.mOpacity, v);

						voxelColumn[slice].density += v.density;
						voxelColumn[slice].ao		= max(voxelColumn[slice].ao, v.ao);
					}
				} // per particle

				// use density and ao info to light the voxel and propagate light to the next one
				float3 rampColor = tex2D(_RampTexture, float2(voxelColumn[slice].ao, 1.0));
				float diffuseCoeff = 0.5;
				float4 voxelColor;
	
				voxelColor = float4(lightIncidentOnVoxel * _LightColor * diffuseCoeff  +  voxelColumn[slice].ao * ambientColor, voxelColumn[slice].density);
				
				volumeTex[int3(i.pos.xy, slice)]	=	voxelColor;

				lightIncidentOnPreviousVoxel = lightIncidentOnVoxel;
				lightIncidentOnVoxel *= rcp(1 + voxelColumn[slice].density);
			} // per slice

			lightPropogationTex[int2(i.pos.xy + _MetavoxelIndex.xy * _NumVoxels)] = lightIncidentOnPreviousVoxel; // exclude the exit border voxel to prevent inconsistencies in next metavoxel
		
			discard;
			return float4(1.0f, 0.0f, 1.0f, 1.0f);
			
		}

		ENDCG
		}
	}FallBack Off
}