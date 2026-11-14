// A puff of fire or smoke for blasts: a quad that always faces the camera, drawn as a soft, lumpy
// ball torn at the edge by noise, lit a little from above. Its colour and fade come per puff
// (_Color, alpha for the fade) and _Seed varies the lumps. One shader, two materials: additive for
// the fireball (hot white-yellow core) and alpha-blended for the smoke and dust.
Shader "Oxfall/Puff"
{
    Properties
    {
        _Color ("Colour (alpha fades)", Color) = (1, 1, 1, 1)
        _Seed ("Seed", Float) = 0
        _Additive ("Additive (1) or smoke (0)", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Puff"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Seed;
                half _Additive;
                float _SrcBlend;
                float _DstBlend;
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
                half fogFactor : TEXCOORD1;
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
                float scale = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
                // Facing the camera fully: the view's right and up in the world.
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float3 positionWS = originWS + (right * input.positionOS.x + up * input.positionOS.y) * scale;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float r = length(p);
                float2 s = float2(_Seed * 3.17, _Seed * 1.93);
                float lumps = Noise(p * 2.6 + s) * 0.65 + Noise(p * 5.3 - s) * 0.35;
                // A ragged round edge, softer for smoke.
                float edge = saturate((1.0 - r - (lumps - 0.5) * 0.55) * (_Additive > 0.5 ? 2.6 : 2.0));
                // Lighter on top, as if the sky lit it.
                float shade = 0.72 + 0.28 * saturate(p.y * 0.5 + 0.5) + (lumps - 0.5) * 0.3;
                half alpha = edge * _Color.a;
                half3 colour;
                if (_Additive > 0.5)
                {
                    // Hot in the middle: towards white-yellow where it is thickest.
                    float core = edge * edge * saturate(1.0 - r * 1.2);
                    colour = (_Color.rgb * shade + core * half3(1.0, 0.85, 0.55)) * alpha;
                    colour = MixFogColor(colour, half3(0, 0, 0), input.fogFactor);
                    return half4(colour, 1);
                }
                colour = _Color.rgb * shade;
                colour = MixFog(colour, input.fogFactor);
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
