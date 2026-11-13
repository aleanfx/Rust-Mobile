// Leaves, needles and grass on cards: cut out by the alpha of a photo atlas, both faces drawn, lit
// softly (light wraps a little around the crown and shines through from behind), with shadows and
// fog. The wind sways each vertex by the weight in its second UV channel (0 = still, at the trunk
// or the root; 1 = free, at the tips): slow gusts that roll across the island plus a quick flutter.
Shader "Oxfall/Foliage"
{
    Properties
    {
        _BaseMap ("Atlas (alpha = cut-out)", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha cut-off", Range(0, 1)) = 0.5
        _WindStrength ("Wind: metres at the tips", Range(0, 0.5)) = 0.12
        _WindSpeed ("Wind speed", Range(0, 3)) = 1
        _Translucency ("Light through the leaves", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff;
            half _WindStrength;
            half _WindSpeed;
            half _Translucency;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            float2 wind : TEXCOORD1;
        };

        /// The vertex in world space, moved by the wind.
        float3 Sway(float3 positionOS, float weight)
        {
            float3 positionWS = TransformObjectToWorld(positionOS);
            float3 rootWS = TransformObjectToWorld(float3(0, 0, 0));
            float t = _Time.y * _WindSpeed;
            // Gusts roll across the island (the phase follows the position of the plant), and every
            // leaf flutters on its own.
            float phase = dot(rootWS.xz, float2(0.11, 0.07));
            float gust = sin(t * 0.8 - phase) * 0.6 + sin(t * 2.1 - phase * 1.6) * 0.3 + 0.35;
            float flutter = sin(t * 5.3 + dot(positionWS, float3(2.1, 1.7, 2.9))) * 0.25;
            float amount = (gust + flutter) * _WindStrength * weight;
            positionWS.xz += float2(0.92, 0.38) * amount;
            positionWS.y -= abs(amount) * 0.25;
            return positionWS;
        }

        half Alpha(float2 uv)
        {
            return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a;
        }
        ENDHLSL

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
                float3 positionWS = Sway(input.positionOS.xyz, input.wind.x);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                clip(texel.a - _Cutoff);
                half3 albedo = texel.rgb * _BaseColor.rgb;

                float3 normalWS = normalize(input.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half ndl = dot(normalWS, mainLight.direction);
                // Wrapped light: the side of the crown away from the sun isn't black.
                half wrap = saturate(ndl * 0.65h + 0.35h);
                // Light through the leaves, seen against the sun.
                float3 viewWS = normalize(_WorldSpaceCameraPos - input.positionWS);
                half through = saturate(dot(-viewWS, mainLight.direction)) * _Translucency;
                half3 direct = mainLight.color * (wrap + through) * mainLight.shadowAttenuation;
                half3 colour = albedo * (direct + SampleSH(normalWS));
                colour = MixFog(colour, input.fogFactor);
                return half4(colour, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // Set by URP for the light whose shadows are being drawn.
            float3 _LightDirection;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = Sway(input.positionOS.xyz, input.wind.x);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                output.positionCS = positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                clip(Alpha(input.uv) - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(Sway(input.positionOS.xyz, input.wind.x));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                clip(Alpha(input.uv) - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
