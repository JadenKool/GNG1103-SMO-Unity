// Day/night Earth for URP: day texture on the sunlit side, dim surface + city lights on the night side,
// soft terminator in between. Driven by the scene's main directional light (the Sun).
Shader "EOSAT/EarthDayNight"
{
    Properties
    {
        _DayTex ("Day texture", 2D) = "white" {}
        _NightTex ("Night lights", 2D) = "black" {}
        _NightLights ("Night lights strength", Range(0, 4)) = 1.4
        _NightAmbient ("Night side brightness", Range(0, 0.4)) = 0.07
        _Terminator ("Terminator softness", Range(0.01, 0.5)) = 0.12
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_DayTex);   SAMPLER(sampler_DayTex);
            TEXTURE2D(_NightTex); SAMPLER(sampler_NightTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _DayTex_ST;
                float4 _NightTex_ST;
                float _NightLights;
                float _NightAmbient;
                float _Terminator;
            CBUFFER_END

            // Instancing/stereo macros let this render in both eyes with single-pass instanced VR.
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert (Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                Light sun = GetMainLight();
                float ndl = dot(normalize(i.normalWS), sun.direction);
                float day = smoothstep(-_Terminator, _Terminator, ndl);

                half3 dayCol = SAMPLE_TEXTURE2D(_DayTex, sampler_DayTex, i.uv).rgb;
                half3 lights = SAMPLE_TEXTURE2D(_NightTex, sampler_NightTex, i.uv).rgb;

                half3 lit = dayCol * saturate(ndl * 0.8 + 0.35);
                half3 night = dayCol * _NightAmbient + lights * _NightLights * half3(1.0, 0.85, 0.6);
                return half4(lerp(night, lit, day), 1);
            }
            ENDHLSL
        }
    }
}
