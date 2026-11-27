// Cuvara/VFX/Grid: the procedural grid of the Showcase room and the capture stage.
//
// A neutral, lit, opaque surface with world-space grid lines: a thin line every cell (1 m by
// default) and a stronger line every _MajorEvery cells (5 m). The lines are a scale reference
// behind an effect, so their contrast is low. No texture.
//
// - Lit by URP 17.6 in Forward and Forward+ (main light, additional lights through the cluster
//   loop, the ambient probe), receives and casts shadows, writes depth and depth-normals.
// - SRP Batcher compatible: every material property is in UnityPerMaterial, the same in all passes.
// - Lines are anti-aliased with screen-space derivatives: a line is never drawn thinner than one
//   pixel (it fades instead), and where a cell gets smaller than about two pixels the line fades
//   to its average coverage, so the grid neither shimmers at a distance nor vanishes up close.
// - Lines follow the world axes on every face: a horizontal face shows X and Z lines, a wall
//   facing Z shows X and Y lines, a wall facing X shows Y and Z lines.
//
// Written for com.cuvara.vfx; no Unity sample or template asset was copied.
Shader "Cuvara/VFX/Grid"
{
    Properties
    {
        _BaseColor("Albedo", Color) = (0.30, 0.30, 0.30, 1)
        _LineColor("Line Colour", Color) = (0.10, 0.10, 0.10, 1)
        _CellSize("Cell Size (m)", Float) = 1
        _MajorEvery("Major Line Every (cells)", Float) = 5
        _LineWidth("Line Width (m)", Range(0.001, 0.25)) = 0.015
        _MajorLineWidth("Major Line Width (m)", Range(0.001, 0.5)) = 0.035
        _MinorLineStrength("Minor Line Strength", Range(0, 1)) = 0.75
        _MajorLineStrength("Major Line Strength", Range(0, 1)) = 0.8
        _Smoothness("Smoothness", Range(0, 1)) = 0.15
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _LineColor;
            float _CellSize;
            float _MajorEvery;
            float _LineWidth;
            float _MajorLineWidth;
            half _MinorLineStrength;
            half _MajorLineStrength;
            half _Smoothness;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual
            Blend One Zero

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex GridVertex
            #pragma fragment GridFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                half3 vertexLight : TEXCOORD3;
            #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings GridVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = half3(TransformObjectToWorldNormal(input.normalOS));
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                output.vertexLight = VertexLighting(positions.positionWS, output.normalWS);
            #endif
                return output;
            }

            // Coverage of one family of grid lines along one world axis. coord is in cells, width
            // is the line width in cells. The line is never thinner than one pixel: below that it
            // keeps one pixel and fades by width / pixel. Where a cell spans fewer than about two
            // pixels the result fades to the line's average coverage, so far lines do not alias.
            float GridLine(float coord, float width)
            {
                float derivative = max(length(float2(ddx(coord), ddy(coord))), 1e-6);
                float drawWidth = clamp(width, derivative, 0.5);
                float antiAlias = derivative * 1.5;
                float distanceToLine = 1.0 - abs(frac(coord) * 2.0 - 1.0);
                float coverage = smoothstep(drawWidth + antiAlias, drawWidth - antiAlias, distanceToLine);
                coverage *= saturate(width / drawWidth);
                return lerp(coverage, width, saturate(derivative * 2.0 - 1.0));
            }

            // Coverage of the lines along every world axis that lies in the face (its normal is
            // not along that axis).
            float GridCoverage(float3 positionWS, half3 normalWS, float cellSize, float width)
            {
                float3 coord = positionWS / cellSize;
                float cellWidth = width / cellSize;
                float3 inFace = saturate(1.5 - 2.0 * abs(float3(normalWS)));
                float x = GridLine(coord.x, cellWidth) * inFace.x;
                float y = GridLine(coord.y, cellWidth) * inFace.y;
                float z = GridLine(coord.z, cellWidth) * inFace.z;
                return 1.0 - (1.0 - x) * (1.0 - y) * (1.0 - z);
            }

            half4 GridFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                float cellSize = max(_CellSize, 1e-3);
                float majorSize = cellSize * max(_MajorEvery, 1.0);
                float minor = GridCoverage(input.positionWS, normalWS, cellSize, _LineWidth) * _MinorLineStrength;
                float major = GridCoverage(input.positionWS, normalWS, majorSize, _MajorLineWidth) * _MajorLineStrength;
                half lineMask = half(saturate(max(minor, major)));

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
            #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            #else
                inputData.shadowCoord = float4(0, 0, 0, 0);
            #endif
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                inputData.vertexLighting = input.vertexLight;
            #endif
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = lerp(_BaseColor.rgb, _LineColor.rgb, lineMask);
                surface.alpha = 1.0h;
                surface.metallic = 0.0h;
                surface.specular = half3(0.0h, 0.0h, 0.0h);
                surface.smoothness = _Smoothness;
                surface.occlusion = 1.0h;
                surface.emission = half3(0.0h, 0.0h, 0.0h);
                surface.normalTS = half3(0.0h, 0.0h, 1.0h);

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0h;
                return color;
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
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex GridShadowVertex
            #pragma fragment GridShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // Set by URP for the light whose shadow map is drawn (ShadowUtils.SetupShadowCasterConstantBuffer).
            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings GridShadowVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                output.positionCS = ApplyShadowClamping(positionCS);
                return output;
            }

            half4 GridShadowFragment(Varyings input) : SV_Target
            {
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
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex GridDepthVertex
            #pragma fragment GridDepthFragment
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings GridDepthVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half GridDepthFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex GridDepthNormalsVertex
            #pragma fragment GridDepthNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings GridDepthNormalsVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = half3(TransformObjectToWorldNormal(input.normalOS));
                return output;
            }

            void GridDepthNormalsFragment(
                Varyings input
                , out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            #if defined(_GBUFFER_NORMALS_OCT)
                float3 normalWS = normalize(float3(input.normalWS));
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remapped = saturate(octNormalWS * 0.5 + 0.5);
                outNormalWS = half4(PackFloat2To888(remapped), 0.0);
            #else
                outNormalWS = half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            #endif
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
