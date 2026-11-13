// The island's ground: four photo textures (grass, dirt, sand, rock) blended by a splat map that
// the game paints from the island's shape, tinted by a colour map for large-scale variation, and
// lit by the sun (with shadows), the ambient light and the fog. Textures are mapped from above in
// world space, so they keep their real size on every chunk.
Shader "Oxfall/Ground"
{
    Properties
    {
        _BaseMap ("Colour map", 2D) = "grey" {}
        _SplatMap ("Splat map (R grass, G dirt, B sand, A rock)", 2D) = "red" {}
        _Grass ("Grass", 2D) = "grey" {}
        _Dirt ("Dirt", 2D) = "grey" {}
        _Sand ("Sand", 2D) = "grey" {}
        _Rock ("Rock", 2D) = "grey" {}
        [Normal] _GrassNormal ("Grass normal", 2D) = "bump" {}
        [Normal] _DirtNormal ("Dirt normal", 2D) = "bump" {}
        [Normal] _SandNormal ("Sand normal", 2D) = "bump" {}
        [Normal] _RockNormal ("Rock normal", 2D) = "bump" {}
        _Tiling ("Metres per tile: grass, dirt, sand, rock", Vector) = (5, 4, 6, 7)
        _TintStrength ("Colour map strength", Range(0, 1)) = 0.3
        _NormalStrength ("Normal strength", Range(0, 2)) = 1
        _Brightness ("Brightness", Range(0.5, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            // Normal maps cost four more texture reads: the game turns them off on the Low level.
            #pragma multi_compile_local _ _GROUND_NORMALS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_SplatMap); SAMPLER(sampler_SplatMap);
            TEXTURE2D(_Grass); SAMPLER(sampler_Grass);
            TEXTURE2D(_Dirt);
            TEXTURE2D(_Sand);
            TEXTURE2D(_Rock);
            TEXTURE2D(_GrassNormal); SAMPLER(sampler_GrassNormal);
            TEXTURE2D(_DirtNormal);
            TEXTURE2D(_SandNormal);
            TEXTURE2D(_RockNormal);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _SplatMap_ST;
                float4 _Tiling;
                half _TintStrength;
                half _NormalStrength;
                half _Brightness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 xz = input.positionWS.xz;
                float2 uvGrass = xz / _Tiling.x;
                float2 uvDirt = xz / _Tiling.y;
                float2 uvSand = xz / _Tiling.z;
                float2 uvRock = xz / _Tiling.w;

                half4 splat = SAMPLE_TEXTURE2D(_SplatMap, sampler_SplatMap, input.uv);
                splat /= max(dot(splat, half4(1, 1, 1, 1)), 0.001h);

                // Grass is most of the island: a second, larger copy breaks up the repeating tile.
                half3 grass = SAMPLE_TEXTURE2D(_Grass, sampler_Grass, uvGrass).rgb;
                half3 grassFar = SAMPLE_TEXTURE2D(_Grass, sampler_Grass, uvGrass * 0.27 + 0.37).rgb;
                grass = lerp(grass, grassFar, 0.35h);

                half3 albedo = grass * splat.r
                    + SAMPLE_TEXTURE2D(_Dirt, sampler_Grass, uvDirt).rgb * splat.g
                    + SAMPLE_TEXTURE2D(_Sand, sampler_Grass, uvSand).rgb * splat.b
                    + SAMPLE_TEXTURE2D(_Rock, sampler_Grass, uvRock).rgb * splat.a;

                half3 tint = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                albedo *= lerp(half3(1, 1, 1), tint * 2.2h, _TintStrength) * _Brightness;

                float3 normalWS = normalize(input.normalWS);
            #if defined(_GROUND_NORMALS)
                half3 n = UnpackNormalScale(SAMPLE_TEXTURE2D(_GrassNormal, sampler_GrassNormal, uvGrass), _NormalStrength) * splat.r
                    + UnpackNormalScale(SAMPLE_TEXTURE2D(_DirtNormal, sampler_GrassNormal, uvDirt), _NormalStrength) * splat.g
                    + UnpackNormalScale(SAMPLE_TEXTURE2D(_SandNormal, sampler_GrassNormal, uvSand), _NormalStrength) * splat.b
                    + UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_GrassNormal, uvRock), _NormalStrength) * splat.a;
                // Textures are mapped from above: tangent along +X, bitangent along +Z.
                float3 tangentWS = normalize(float3(1, 0, 0) - normalWS * normalWS.x);
                float3 bitangentWS = cross(tangentWS, normalWS);
                normalWS = normalize(tangentWS * n.x + bitangentWS * n.y + normalWS * max(n.z, 0.001h));
            #endif

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half lambert = saturate(dot(normalWS, mainLight.direction));
                half3 lighting = mainLight.color * (lambert * mainLight.shadowAttenuation) + SampleSH(normalWS);

                half3 colour = albedo * lighting;
                colour = MixFog(colour, input.fogFactor);
                return half4(colour, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
