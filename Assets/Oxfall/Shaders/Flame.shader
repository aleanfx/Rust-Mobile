// Fire for torches and campfires: an unlit, additive glow on a small closed flame shape, yellow
// at the root and red at the tip, brightest where it faces the camera so its edges melt away,
// licking and flickering on its own (the phase comes from where it is, so no two flames match).
Shader "Oxfall/Flame"
{
    Properties
    {
        _CoreColor ("Root", Color) = (1, 0.82, 0.42, 1)
        _TipColor ("Tip", Color) = (0.95, 0.28, 0.06, 1)
        _Intensity ("Brightness", Range(0, 4)) = 1.6
        _Height ("Flame height in object units", Float) = 0.26
        _Lick ("Lick strength", Range(0, 0.1)) = 0.03
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Blend One One
        ZWrite Off
        Cull Back

        Pass
        {
            Name "Flame"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _TipColor;
                half _Intensity;
                float _Height;
                half _Lick;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                float height : TEXCOORD2;
                float flicker : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 rootWS = TransformObjectToWorld(float3(0, 0, 0));
                float phase = dot(rootWS, float3(1.7, 2.3, 1.1));
                float t = _Time.y;
                float h = saturate(input.positionOS.y / _Height);
                float3 p = input.positionOS.xyz;
                // The tip licks sideways and stretches up and down; the root stays put.
                p.x += sin(t * 9.0 + phase + p.y * 18.0) * _Lick * h;
                p.z += sin(t * 7.3 + phase * 1.3 + p.y * 15.0) * _Lick * h;
                p.y *= 1.0 + sin(t * 11.0 + phase) * 0.12 * h;

                float3 positionWS = TransformObjectToWorld(p);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = _WorldSpaceCameraPos - positionWS;
                output.height = h;
                output.flicker = 0.85 + 0.15 * sin(t * 17.0 + phase * 2.0);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half facing = saturate(dot(normalize(input.normalWS), normalize(input.viewWS)));
                half glow = facing * facing * (1.0h - input.height * 0.55h);
                half3 colour = lerp(_CoreColor.rgb, _TipColor.rgb, saturate(input.height * 1.3h));
                return half4(colour * glow * _Intensity * input.flicker, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
