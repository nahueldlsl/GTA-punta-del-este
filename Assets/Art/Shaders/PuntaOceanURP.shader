Shader "PuntaDelEste/OceanURP"
{
    Properties
    {
        _MansaShallowColor ("Mansa Shallow Color", Color) = (0.14, 0.60, 0.70, 0.92)
        _MansaDeepColor ("Mansa Deep Color", Color) = (0.06, 0.30, 0.48, 0.98)
        _BravaShallowColor ("Brava Shallow Color", Color) = (0.10, 0.48, 0.64, 0.95)
        _BravaDeepColor ("Brava Deep Color", Color) = (0.04, 0.18, 0.38, 0.99)
        _FoamColor ("Foam Color", Color) = (0.95, 0.98, 1.0, 1.0)
        _SkyReflectColor ("Sky Reflection Color", Color) = (0.45, 0.74, 0.95, 1.0)
        _HorizonGlowColor ("Horizon / Sun Tint", Color) = (1.0, 0.93, 0.80, 1.0)
        _NormalMapA ("Ocean Normal A", 2D) = "bump" {}
        _NormalMapB ("Ocean Normal B", 2D) = "bump" {}
        _NormalTiling ("Normal Tiling", Float) = 0.018
        _NormalStrength ("Normal Strength", Range(0.05, 1.5)) = 0.58
        _WaveSpeed ("Wave Speed", Float) = 0.95
        _MansaWaveAmp ("Mansa Wave Amplitude", Float) = 0.06
        _BravaWaveAmp ("Brava Wave Amplitude", Float) = 0.34
        _FoamDepth ("Shoreline Foam Depth", Float) = 3.2
        _BravaFoamBoost ("Brava Foam Boost", Float) = 1.85
        _Smoothness ("Smoothness", Range(0.1, 0.99)) = 0.94
        _FresnelPower ("Fresnel Power", Range(1.0, 8.0)) = 4.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_NormalMapA); SAMPLER(sampler_NormalMapA);
            TEXTURE2D(_NormalMapB); SAMPLER(sampler_NormalMapB);

            CBUFFER_START(UnityPerMaterial)
                half4 _MansaShallowColor;
                half4 _MansaDeepColor;
                half4 _BravaShallowColor;
                half4 _BravaDeepColor;
                half4 _FoamColor;
                half4 _SkyReflectColor;
                half4 _HorizonGlowColor;
                float _NormalTiling;
                float _NormalStrength;
                float _WaveSpeed;
                float _MansaWaveAmp;
                float _BravaWaveAmp;
                float _FoamDepth;
                float _BravaFoamBoost;
                float _Smoothness;
                float _FresnelPower;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                float bravaFactor : TEXCOORD2;
                float fogCoord : TEXCOORD3;
            };

            float ComputeBravaFactor(float2 xz)
            {
                float2 p = xz - float2(-480.0, -750.0);
                float2 bravaDir = normalize(float2(1400.0, -930.0));
                float side = dot(p, bravaDir);
                return saturate((side + 120.0) / 420.0);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float brava = ComputeBravaFactor(posWS.xz);

                // Smooth long-wavelength swells (zero mesh-grid aliasing at any distance!)
                float amp = lerp(_MansaWaveAmp, _BravaWaveAmp, brava);
                float t = _Time.y * _WaveSpeed;
                float w1 = sin(dot(posWS.xz, float2(-0.014, 0.011)) + t * 1.35);
                float w2 = sin(dot(posWS.xz, float2(-0.021, -0.008)) + t * 1.75 + 1.4);
                posWS.y += (w1 * 0.65 + w2 * 0.35) * amp;

                OUT.positionWS = posWS;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                OUT.bravaFactor = brava;
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Multi-scale organic scrolling wave normals (far + detail layers, zero diagonal moire!)
                float t = _Time.y * _WaveSpeed;
                float2 uvA = IN.positionWS.xz * _NormalTiling + float2(-t * 0.022, t * 0.015);
                float2 uvB = IN.positionWS.xz * (_NormalTiling * 1.73) + float2(-t * 0.017, -t * 0.024);
                float2 uvC = IN.positionWS.xz * (_NormalTiling * 0.42) + float2(t * 0.009, t * 0.011);

                half3 nA = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMapA, sampler_NormalMapA, uvA));
                half3 nB = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMapB, sampler_NormalMapB, uvB));
                half3 nC = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMapA, sampler_NormalMapA, uvC));
                half3 nTS = normalize(half3((nA.xy + nB.xy) * 0.55 + nC.xy * 0.45, 1.0));

                // Distance normal attenuation to keep distant horizon silky-smooth!
                float camDist = length(_WorldSpaceCameraPos - IN.positionWS);
                float distFade = saturate(1.0 - (camDist - 120.0) / 1400.0);
                float nStr = _NormalStrength * lerp(0.45, 1.15, IN.bravaFactor) * (0.28 + 0.72 * distFade);
                half3 normalWS = normalize(half3(nTS.x * nStr, 1.0, nTS.y * nStr));

                // Depth-based coastal shallows & breaking surf foam
                float2 screenUV = IN.screenPos.xy / max(IN.screenPos.w, 0.0001);
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float waterDepth = max(0.0, sceneEyeDepth - IN.screenPos.w);

                float depthT = saturate(waterDepth / 12.0);
                half4 shallowCol = lerp(_MansaShallowColor, _BravaShallowColor, IN.bravaFactor);
                half4 deepCol = lerp(_MansaDeepColor, _BravaDeepColor, IN.bravaFactor);
                half4 waterCol = lerp(shallowCol, deepCol, pow(depthT, 0.60));

                half3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));
                Light mainLight = GetMainLight();
                half3 lightDir = normalize(mainLight.direction);

                half ndotv = saturate(dot(normalWS, viewDirWS));
                half fresnel = pow(1.0 - ndotv, _FresnelPower);

                // Real URP HDRI Environment Cubemap reflection (reflects PureSky clouds, sunset coral & night stars!)
                half3 reflectDir = reflect(-viewDirWS, normalWS);
                reflectDir.y = max(reflectDir.y, 0.04);
                half envRough = lerp(0.04, 0.22, IN.bravaFactor);
                half3 envRefl = GlossyEnvironmentReflection(reflectDir, envRough, 1.0);
                half3 fallbackSky = lerp(_SkyReflectColor.rgb, _HorizonGlowColor.rgb, saturate(pow(1.0 - ndotv, 2.2) * 0.55));
                half3 reflColor = lerp(fallbackSky, envRefl, 0.78);

                // Beer-Lambert subtle subsurface turquoise scatter in translucent shallow waves
                half sss = pow(saturate(dot(viewDirWS, -lightDir + normalWS * 0.45)), 3.0) * (1.0 - depthT * 0.65) * 0.35;
                half3 sssCol = lerp(half3(0.10, 0.58, 0.64), half3(0.06, 0.46, 0.56), IN.bravaFactor) * mainLight.color * sss;

                half3 halfVec = normalize(lightDir + viewDirWS);
                float specPow = lerp(340.0, 125.0, IN.bravaFactor) * _Smoothness;
                half spec = pow(saturate(dot(normalWS, halfVec)), specPow) * 2.8;
                half3 specCol = mainLight.color * spec * _HorizonGlowColor.rgb;

                // Animated shoreline breaking surf bands (dramatically richer multi-band whitewash on Playa Brava!)
                float foamRange = _FoamDepth * lerp(0.55, _BravaFoamBoost * 1.15, IN.bravaFactor);
                float shoreMask = 1.0 - saturate(waterDepth / max(foamRange, 0.1));
                float surfWave1 = sin(waterDepth * 3.1 - t * (2.3 + IN.bravaFactor * 1.5) + (nA.x + nB.y) * 3.4);
                float surfWave2 = sin(waterDepth * 5.8 - t * (3.1 + IN.bravaFactor * 1.8) - nC.x * 4.0);
                float foamBands = smoothstep(0.12, 0.68, shoreMask + (surfWave1 * 0.26 + surfWave2 * 0.12) * shoreMask);
                float contactFoam = smoothstep(0.70, 0.0, waterDepth);
                float whitecaps = smoothstep(0.54, 0.84, nA.x * 0.6 + nB.y * 0.5 + 0.35) * IN.bravaFactor * 0.44 * distFade;
                float foam = saturate(max(max(foamBands, contactFoam), whitecaps));

                half ndotl = saturate(dot(normalWS, lightDir)) * 0.52 + 0.48;
                half3 col = waterCol.rgb * ndotl + sssCol;
                col = lerp(col, reflColor, fresnel * lerp(0.84, 0.62, IN.bravaFactor));
                col += specCol;
                col = lerp(col, _FoamColor.rgb * (mainLight.color * 0.72 + 0.28), foam * 0.94);

                half alpha = saturate(max(waterCol.a * smoothstep(0.0, 0.42, waterDepth), foam * 0.94));
                col = MixFog(col, IN.fogCoord);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
