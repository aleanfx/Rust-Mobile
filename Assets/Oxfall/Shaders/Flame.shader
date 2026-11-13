// Fire for torches and campfires: an unlit, additive tongue of flame drawn on a small quad that
// always turns to face the camera (around the vertical, so it stays upright). The shape is a soft
// teardrop torn by rising noise, white-yellow in the core and red at the ragged tip, licking and
// flickering on its own; the phase comes from where it is, so no two flames match.
Shader "Oxfall/Flame"
{
    Properties
    {
        _CoreColor ("Root", Color) = (1, 0.82, 0.42, 1)
        _TipColor ("Tip", Color) = (0.95, 0.28, 0.06, 1)
        _Intensity ("Brightness", Range(0, 4)) = 1.6
        _Height ("Unused (kept for saved materials)", Float) = 0.26
        _Lick ("Lick strength", Range(0, 0.1)) = 0.03
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Flame"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
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
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float phase : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 originWS = TransformObjectToWorld(float3(0, 0, 0));
                float scaleX = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
                float scaleY = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));

                // Upright billboard: sideways is across the line of sight, up stays up.
                float3 toCamera = _WorldSpaceCameraPos - originWS;
                toCamera.y = 0;
                float3 right = normalize(cross(float3(0, 1, 0), toCamera + float3(1e-4, 0, 1e-4)));
                float phase = dot(originWS, float3(1.7, 2.3, 1.1));
                float h = saturate(input.uv.y);
                // The tip sways a little; the root stays put.
                float sway = sin(_Time.y * 5.0 + phase) * _Lick * 2.0 * h * h;
                float3 positionWS = originWS
                    + right * (input.positionOS.x * scaleX + sway * scaleY)
                    + float3(0, 1, 0) * input.positionOS.y * scaleY;

                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.phase = phase;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float t = _Time.y + input.phase;
                float rising = Noise(float2(uv.x * 3.0 + input.phase, uv.y * 3.5 - t * 3.2));
                float fine = Noise(float2(uv.x * 7.0 + 5.0, uv.y * 6.0 - t * 5.5));

                // Across the flame from -1 to 1, bent by the rising noise more towards the tip.
                float x = (uv.x - 0.5) * 2.0;
                x += (rising - 0.5) * 0.7 * uv.y + sin(t * 7.0 + uv.y * 6.0) * 0.07 * uv.y;
                // Widest a third of the way up, pinched at the root, pointed at the tip.
                float width = pow(saturate(sin(3.14159 * pow(uv.y, 0.55))), 0.9) * (1.0 - 0.45 * uv.y);
                float body = saturate(1.0 - abs(x) / max(width, 1e-3));
                // A ragged top: tongues tear off where the noise is low.
                body *= saturate((1.0 - uv.y) * 1.5 + (fine - 0.5) * 0.9);

                float glow = body * body;
                float core = body * body * body * saturate(1.0 - uv.y * 1.7);
                float flicker = 0.85 + 0.15 * sin(t * 17.0) * sin(t * 6.3 + 1.0);
                half3 colour = lerp(_TipColor.rgb, _CoreColor.rgb, saturate(body * 1.3 - uv.y * 0.5)) * glow
                               + core * half3(1.0, 0.92, 0.75) * 0.9;
                colour *= _Intensity * flicker;
                // Additive: fog fades the flame towards nothing rather than tinting it.
                colour = MixFogColor(colour, half3(0, 0, 0), input.fogFactor);
                return half4(colour, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
