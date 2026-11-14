// Rain round the camera, moved entirely on the GPU: one static mesh of thin streaks, each with its
// own spot in a box; the shader drops every streak through the box with time and wraps it round the
// camera, so the rain is always about the viewer, wherever they walk, for one draw call. _Amount
// (0..1) says how much of it falls (streaks whose own number is above it are not drawn), _Wind
// leans it, _Color tints it to the light. Streaks fade near the camera and towards the box's edge.
Shader "Oxfall/Rain"
{
    Properties
    {
        _Color ("Colour (alpha: strength)", Color) = (0.7, 0.75, 0.8, 0.35)
        _Amount ("How much rain (0..1)", Range(0, 1)) = 1
        _Speed ("Fall speed (m/s)", Float) = 16
        _Wind ("Wind (x, z in m/s)", Vector) = (2, 0, 1, 0)
        _Box ("Box size (m)", Float) = 24
        _Length ("Streak length (m)", Float) = 0.7
        _Width ("Streak width (m)", Float) = 0.012
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Rain"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Amount;
                float _Speed;
                float4 _Wind;
                float _Box;
                float _Length;
                float _Width;
            CBUFFER_END

            // The game's clock for the rain, wrapped every few minutes so it stays precise.
            float _OxfallRainTime;

            struct Attributes
            {
                // Every corner of a streak carries the streak's spot in the box (0.._Box) ...
                float4 positionOS : POSITION;
                // ... which corner it is (x: -1 or 1 across, y: 0 bottom, 1 top) ...
                float2 uv : TEXCOORD0;
                // ... and the streak's own number (0..1) and a speed variation.
                float2 uv2 : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half fade : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 camera = _WorldSpaceCameraPos;
                float speed = _Speed * (0.85 + 0.3 * input.uv2.y);
                float3 velocity = float3(_Wind.x, -speed, _Wind.z);
                // Fallen this far by now; wrapped into the box centred on the camera.
                float3 p = input.positionOS.xyz + velocity * _OxfallRainTime;
                p = camera + (frac((p - camera) / _Box + 0.5) - 0.5) * _Box;
                // Across: square to the view; along: the way it falls.
                float3 along = normalize(velocity);
                float3 toCamera = normalize(camera - p);
                float3 across = normalize(cross(along, toCamera));
                float3 world = p + across * (input.uv.x * _Width) - along * (input.uv.y * _Length);
                // Streaks beyond the rain's amount collapse to nothing.
                float on = step(input.uv2.x, _Amount);
                world = lerp(p, world, on);
                output.positionCS = TransformWorldToHClip(world);
                output.uv = input.uv;
                float distance = length(p - camera);
                float edge = 1.0 - saturate((distance - _Box * 0.3) / (_Box * 0.2));
                float near = saturate((distance - 0.6) / 1.4);
                output.fade = edge * near * on;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Soft at the sides and the ends.
                half side = 1.0 - abs(input.uv.x);
                half ends = sin(input.uv.y * 3.14159);
                return half4(_Color.rgb, _Color.a * side * ends * input.fade);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
