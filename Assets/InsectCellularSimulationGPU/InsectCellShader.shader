Shader "Custom/InsectCellShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _EmissionMap ("Emission Map", 2D) = "black" {}
        _EmissionColor ("Emission Color", Color) = (0,0,0,0)
        _EmissionPower ("Emission Power", Range(0,10)) = 1.0
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _NormalScale ("Normal Scale", Range(0,3)) = 1.0
        _CellTransparency ("Cell Transparency", Range(0,1)) = 0.7
        _PulsateSpeed ("Pulsate Speed", Range(0,10)) = 3.0
        _PulsateAmount ("Pulsate Amount", Range(0,1)) = 0.2
    }
    
    SubShader
    {
        Tags {"RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline"}
        
        LOD 300
        
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        ENDHLSL
        
        Pass
        {
            Name "ForwardLit"
            Tags{"LightMode" = "UniversalForward"}
            
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:SetupProcedural
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            
            struct ParticleData
            {
                float3 position;
                float3 velocity;
                int typeIndex;
                float mass;
                float radius;
                float padding;
            };
            
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_EmissionMap);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _EmissionMap_ST;
                float4 _EmissionColor;
                float _EmissionPower;
                float _Glossiness;
                float _Metallic;
                float _NormalScale;
                float _CellTransparency;
                float _PulsateSpeed;
                float _PulsateAmount;
            CBUFFER_END
            
            StructuredBuffer<ParticleData> _ParticleBuffer;
            StructuredBuffer<float4> _ColorBuffer;
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                float4 color : TEXCOORD4;
                uint cellType : TEXCOORD5;
                float cellActivity : TEXCOORD6;
            };
            
            void SetupProcedural()
            {
                uint id = unity_InstanceID;
                ParticleData particle = _ParticleBuffer[id];
                
                // Modify the scale based on cell type and pulsation
                float scale = particle.radius * 2.0;
                
                unity_ObjectToWorld = float4x4(
                    scale, 0, 0, particle.position.x,
                    0, scale, 0, particle.position.y,
                    0, 0, scale, particle.position.z,
                    0, 0, 0, 1
                );
            }
            
            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                
                UNITY_SETUP_INSTANCE_ID(input);
                
                uint id = unity_InstanceID;
                ParticleData particle = _ParticleBuffer[id];
                int typeIndex = particle.typeIndex;
                
                // Transform the vertex positions
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                
                // Transform the normals
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.normalWS = normalInput.normalWS;
                output.tangentWS = float4(normalInput.tangentWS, input.tangentOS.w);
                
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                
                // Get cell color from buffer
                if (typeIndex >= 0)
                {
                    output.color = _ColorBuffer[typeIndex];
                    output.cellType = typeIndex;
                    
                    // Calculate cell activity based on velocity magnitude
                    float speed = length(particle.velocity);
                    output.cellActivity = saturate(speed / 5.0); // Normalize for visualization
                }
                else
                {
                    output.color = float4(1,1,1,1);
                    output.cellType = 0;
                    output.cellActivity = 0;
                }
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                // Sample the texture
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 emissionMap = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv);
                
                // Base color from the particle type
                half4 baseColor = input.color * texColor;
                
                // Cell-type specific visual effects
                float cellPulsate = 0;
                float cellTransparency = _CellTransparency;
                float3 emissionColor = _EmissionColor.rgb;
                
                // Different visual effects based on cell type
                switch(input.cellType) {
                    case 0: // Stem
                        // Pulsating effect for stem cells
                        cellPulsate = sin(_Time.y * _PulsateSpeed) * _PulsateAmount + 1.0;
                        emissionColor = float3(1.0, 1.0, 1.0) * input.cellActivity * 2.0;
                        break;
                        
                    case 1: // Epithelial
                        // More transparent for epithelial cells
                        cellTransparency *= 0.8;
                        emissionColor = float3(0.8, 0.2, 0.2) * input.cellActivity;
                        break;
                        
                    case 2: // Neural
                        // Highly emissive neural cells with electric-like effect
                        emissionColor = float3(0.2, 0.2, 1.0) * (sin(_Time.y * 8.0 + input.positionWS.x * 5.0) * 0.5 + 1.5);
                        break;
                        
                    case 3: // Muscle
                        // Dense, fibrous appearance for muscle
                        cellTransparency *= 0.9;
                        baseColor.rgb *= 0.8 + 0.2 * sin(input.positionWS.y * 20.0);
                        break;
                        
                    case 4: // Tracheal
                        // More transparent, tube-like structures
                        cellTransparency *= 0.6;
                        emissionColor = float3(0.4, 0.7, 1.0) * input.cellActivity;
                        break;
                        
                    case 5: // Fat
                        // Glossy, dense cells
                        cellPulsate = 0.2 * sin(_Time.y + input.positionWS.z) + 1.0;
                        break;
                        
                    case 6: // Cuticle
                        // Hard outer shell appearance
                        cellTransparency *= 0.95;
                        baseColor.rgb *= 0.9 + 0.1 * cos(input.positionWS.x * 10.0 + input.positionWS.z * 10.0);
                        break;
                        
                    case 7: // Hemolymph
                        // Fluid-like transparent cells
                        cellTransparency *= 0.5;
                        cellPulsate = 0.1 * sin(_Time.y * 2.0) + 1.0;
                        emissionColor = float3(0.1, 0.5, 0.1) * input.cellActivity;
                        break;
                        
                    case 8: // Segment
                        // Structured, boundary-like cells
                        cellPulsate = 0.05 * sin(_Time.y) + 1.0;
                        emissionColor = float3(0.5, 0.5, 0.8) * input.cellActivity;
                        break;
                        
                    case 9: // Appendage
                        // Elongated, growing cells
                        cellPulsate = 0.3 * sin(_Time.y * 3.0) + 1.0;
                        emissionColor = float3(0.1, 0.8, 0.2) * input.cellActivity;
                        break;
                }
                
                // Apply pulsating effect to brightness
                baseColor.rgb *= cellPulsate;
                
                // Calculate lighting
                InputData lightingInput = (InputData)0;
                lightingInput.positionWS = input.positionWS;
                lightingInput.normalWS = normalize(input.normalWS);
                lightingInput.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                lightingInput.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                
                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = baseColor.rgb;
                surfaceData.metallic = _Metallic;
                surfaceData.smoothness = _Glossiness;
                surfaceData.emission = emissionColor * emissionMap.rgb * _EmissionPower;
                surfaceData.occlusion = 1.0;
                
                // Apply lighting and return final color with transparency
                half4 finalColor = UniversalFragmentPBR(lightingInput, surfaceData);
                finalColor.a = baseColor.a * cellTransparency;
                
                return finalColor;
            }
            ENDHLSL
        }
        
        // Shadow casting pass
        Pass
        {
            Name "ShadowCaster"
            Tags{"LightMode" = "ShadowCaster"}
            
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:SetupProcedural
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            struct ParticleData
            {
                float3 position;
                float3 velocity;
                int typeIndex;
                float mass;
                float radius;
                float padding;
            };
            
            StructuredBuffer<ParticleData> _ParticleBuffer;
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                uint instanceID : SV_InstanceID;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };
            
            void SetupProcedural()
            {
                uint id = unity_InstanceID;
                ParticleData particle = _ParticleBuffer[id];
                float scale = particle.radius * 2.0;
                
                unity_ObjectToWorld = float4x4(
                    scale, 0, 0, particle.position.x,
                    0, scale, 0, particle.position.y,
                    0, 0, scale, particle.position.z,
                    0, 0, 0, 1
                );
            }
            
            float4 GetShadowPositionHClip(Attributes input)
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, 0));
                
                #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                
                return positionCS;
            }
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = GetShadowPositionHClip(input);
                return output;
            }
            
            half4 frag(Varyings input) : SV_TARGET
            {
                return 0;
            }
            ENDHLSL
        }
    }
    
    FallBack "Universal Render Pipeline/Lit"
}