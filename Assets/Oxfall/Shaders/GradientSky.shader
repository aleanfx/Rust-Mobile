// The sky: a gradient from the horizon to the zenith, with a glow and a disc for the sun.
// The horizon colour is also the fog colour, so fogged land and sea melt into the sky.
// Colours and the sun direction are set from code at start-up (WorldBuilder).
Shader "Oxfall/GradientSky"
{
    Properties
    {
        _TopColor ("Zenith", Color) = (0.35, 0.49, 0.66, 1)
        _HorizonColor ("Horizon", Color) = (0.81, 0.77, 0.69, 1)
        _BottomColor ("Below the horizon", Color) = (0.62, 0.60, 0.55, 1)
        _SunColor ("Sun", Color) = (1, 0.84, 0.66, 1)
        _SunDirection ("Towards the sun", Vector) = (0, 0.5, 0.85, 0)
        _SunSize ("Sun size", Float) = 1
        _Exponent ("Gradient curve", Float) = 0.55
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half4 _TopColor;
            half4 _HorizonColor;
            half4 _BottomColor;
            half4 _SunColor;
            float4 _SunDirection;
            half _SunSize;
            half _Exponent;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                half3 above = lerp(_HorizonColor.rgb, _TopColor.rgb, pow(max(h, 1e-4), _Exponent));
                half3 below = lerp(_HorizonColor.rgb, _BottomColor.rgb, saturate(-h * 6.0));
                half3 col = h > 0.0 ? above : below;

                float s = max(dot(d, normalize(_SunDirection.xyz)), 1e-4);
                col += _SunColor.rgb * (pow(s, 8.0) * 0.22 + pow(s, 80.0) * 0.45);
                col += _SunColor.rgb * smoothstep(1.0 - 0.0009 * _SunSize, 1.0 - 0.0004 * _SunSize, s) * 3.0;
                return half4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
