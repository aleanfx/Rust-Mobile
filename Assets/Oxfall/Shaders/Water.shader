// The sea: two layers of ripples drifting in different directions, the sky reflected more strongly
// at grazing angles (Fresnel), a glint where the sun reflects, and the fog. Over the island a depth
// map (baked once by the game from the island's shape) turns shallow water turquoise, lets the sand
// show through right at the beach and paints moving foam along the shore. The sky colours are
// global shader values set by the game (WorldBuilder), so the reflection matches the real sky.
// Opaque on purpose: cheap on phones, with no need for the camera's depth texture.
Shader "Oxfall/Water"
{
    Properties
    {
        _DeepColor ("Deep colour", Color) = (0.04, 0.15, 0.19, 1)
        _ShallowColor ("Shallow colour", Color) = (0.12, 0.38, 0.36, 1)
        _SandColor ("Sand seen through the water", Color) = (0.58, 0.52, 0.40, 1)
        _FoamColor ("Foam", Color) = (0.86, 0.88, 0.86, 1)
        _DepthMap ("Depth map (red = depth / range)", 2D) = "white" {}
        _DepthMapScale ("Depth map: 1/size (x), offset (y), range in metres (z)", Vector) = (0.00208, 0.5, 9, 0)
        _RippleMap ("Ripple normals (linear RGB)", 2D) = "bump" {}
        _RippleTiling ("Ripple tiling: metres per tile (x, y), speed (z)", Vector) = (14, 37, 0.025, 0)
        _RippleStrength ("Ripple strength", Range(0, 1.5)) = 0.35
        _Reflection ("Reflection", Range(0, 1)) = 0.6
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
            TEXTURE2D(_DepthMap); SAMPLER(sampler_DepthMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _ShallowColor;
                half4 _SandColor;
                half4 _FoamColor;
                float4 _DepthMap_ST;
                float4 _DepthMapScale;
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
                // The horizon is reflected a little darker than it looks, or near the shore the sea
                // reads as the same pale grey as the sky and the fog: a hole in the world.
                half3 sky = lerp(_OxfallSkyHorizon.rgb * 0.8h, _OxfallSkyTop.rgb, pow(saturate(reflected.y), 0.4h));
                half facing = saturate(dot(normalWS, viewWS));
                half fresnel = 0.04h + 0.96h * pow(1.0h - facing, 5.0h);

                // Depth of the water here, in metres.
                float2 depthUv = xz * _DepthMapScale.x + _DepthMapScale.y;
                half depth = SAMPLE_TEXTURE2D(_DepthMap, sampler_DepthMap, depthUv).r * _DepthMapScale.z;

                Light mainLight = GetMainLight();
                half lambert = saturate(dot(normalWS, mainLight.direction));
                half3 lit = mainLight.color * lambert * 0.35h + SampleSH(normalWS);
                half3 water = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(depth / 4.0h));
                half3 body = water * lit;
                // Right at the beach the sand shows through the thin water.
                half clear = saturate(1.0h - depth / 0.9h);
                half3 upLight = mainLight.color * saturate(mainLight.direction.y) + SampleSH(half3(0, 1, 0));
                body = lerp(body, _SandColor.rgb * upLight * 0.8h, clear * clear * 0.75h);

                // Less mirror-like in the shallows, where the bottom shows.
                half reflection = fresnel * _Reflection * lerp(1.0h, 0.5h, clear);
                half3 colour = lerp(body, sky, saturate(reflection + 0.08h));
                half glint = pow(saturate(dot(reflected, mainLight.direction)), 220.0h);
                colour += mainLight.color * glint * _Glint;

                // Foam along the shore: bands rolling in towards the beach, broken up by the ripples.
                half shore = saturate(1.0h - depth / 0.8h);
                half bands = 0.5h + 0.5h * sin(_Time.y * 1.4h + depth * 9.0h);
                half noise = (a.x + b.x) * 0.5h + 0.5h;
                half foam = shore * saturate((bands * 0.6h + noise * 0.9h + shore * 0.5h) - 0.75h) * 2.2h;
                colour = lerp(colour, _FoamColor.rgb * upLight, saturate(foam));

                colour = MixFog(colour, input.fogFactor);
                return half4(colour, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
