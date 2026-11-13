// The sea: two layers of ripples drifting in different directions, the sky reflected more strongly
// at grazing angles (Fresnel), a glint where the sun reflects, and the fog. The sky colours are
// global shader values set by the game (WorldBuilder), so the reflection matches the real sky.
// Opaque on purpose: cheap on phones, and the sea floor is too deep to see anyway.
Shader "Oxfall/Water"
{
    Properties
    {
        _DeepColor ("Deep colour", Color) = (0.07, 0.17, 0.19, 1)
        _RippleMap ("Ripple normals (linear RGB)", 2D) = "bump" {}
        _RippleTiling ("Ripple tiling: metres per tile (x, y), speed (z)", Vector) = (9, 23, 0.03, 0)
        _RippleStrength ("Ripple strength", Range(0, 1.5)) = 0.6
        _Reflection ("Reflection", Range(0, 1)) = 0.85
        _Glint ("Sun glint", Range(0, 8)) = 3
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
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_RippleMap); SAMPLER(sampler_RippleMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                float4 _RippleMap_ST;
                float4 _RippleTiling;
                half _RippleStrength;
                half _Reflection;
                half _Glint;
            CBUFFER_END

            // Set from code for every material: the sky's zenith and horizon colours.
            half4 _OxfallSkyTop;
            half4 _OxfallSkyHorizon;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fogFactor : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half3 Ripple(float2 uv)
            {
                half3 n = SAMPLE_TEXTURE2D(_RippleMap, sampler_RippleMap, uv).rgb * 2.0h - 1.0h;
                return n;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float t = _Time.y * _RippleTiling.z;
                float2 xz = input.positionWS.xz;
                half3 a = Ripple(xz / _RippleTiling.x + float2(t, t * 0.6));
                half3 b = Ripple(xz / _RippleTiling.y + float2(-t * 0.7, t * 0.9));
                half3 n = normalize(half3((a.xy + b.xy) * _RippleStrength, 1.0h));
                // Tangent space of a flat sea: x along +X, y along +Z, z up.
                float3 normalWS = normalize(float3(n.x, n.z, n.y));

                float3 viewWS = normalize(_WorldSpaceCameraPos - input.positionWS);
                float3 reflected = reflect(-viewWS, normalWS);
                half3 sky = lerp(_OxfallSkyHorizon.rgb, _OxfallSkyTop.rgb, pow(saturate(reflected.y), 0.6h));
                half facing = saturate(dot(normalWS, viewWS));
                half fresnel = 0.04h + 0.96h * pow(1.0h - facing, 5.0h);

                Light mainLight = GetMainLight();
                half lambert = saturate(dot(normalWS, mainLight.direction));
                half3 body = _DeepColor.rgb * (mainLight.color * lambert * 0.35h + SampleSH(normalWS));
                half3 colour = lerp(body, sky, saturate(fresnel * _Reflection + 0.12h));
                half glint = pow(saturate(dot(reflected, mainLight.direction)), 220.0h);
                colour += mainLight.color * glint * _Glint;

                colour = MixFog(colour, input.fogFactor);
                return half4(colour, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
