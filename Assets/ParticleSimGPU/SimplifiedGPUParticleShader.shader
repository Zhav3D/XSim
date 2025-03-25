// Create this new simplified shader file named "SimpleGPUParticleShader.shader"
Shader "Custom/SimpleGPUParticleShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags {"RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry"}
        LOD 100
        
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:SetupProcedural
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
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
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
            CBUFFER_END
            
            StructuredBuffer<ParticleData> _ParticleBuffer;
            StructuredBuffer<float4> _ColorBuffer;
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : TEXCOORD1;
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
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                
                uint id = unity_InstanceID;
                int typeIndex = _ParticleBuffer[id].typeIndex;
                if(typeIndex >= 0)
                    output.color = _ColorBuffer[typeIndex];
                else
                    output.color = float4(1,1,1,1);
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                return input.color;
            }
            ENDHLSL
        }
    }
}