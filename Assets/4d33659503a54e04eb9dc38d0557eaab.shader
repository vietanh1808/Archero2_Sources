Shader "Effects/RimForRt" {
	Properties {
		_TintColor ("主颜色", Vector) = (1,1,1,1)
		_MainTex ("主纹理", 2D) = "white" {}
		_InnerPower ("内边缘强度", Range(0, 8)) = 4
		_AlphaPower ("内边缘宽度", Range(0, 8)) = 4
		_RimColor ("外边缘颜色", Vector) = (0.5,0.5,0.5,0.5)
		_RimPower ("外边缘宽度", Range(0, 5)) = 2.5
		_AllPower ("外边缘强度", Range(0, 10)) = 1
		_Alpha ("透明度", Range(0, 1)) = 1
		[HideInInspector] _ColorMode ("ColorMode", Float) = 0
		[HideInInspector] _SrcBlend ("__src", Float) = 1
		[HideInInspector] _DstBlend ("__dst", Float) = 0
		[HideInInspector] _TwoSide ("TwoSide", Float) = 0
		[HideInInspector] _Ztest ("Ztest", Float) = 2
	}
	//DummyShaderTextExporter
	SubShader{
		Tags { "RenderType"="Opaque" }
		LOD 200

		Pass
		{
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			float4x4 unity_ObjectToWorld;
			float4x4 unity_MatrixVP;
			float4 _MainTex_ST;

			struct Vertex_Stage_Input
			{
				float4 pos : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct Vertex_Stage_Output
			{
				float2 uv : TEXCOORD0;
				float4 pos : SV_POSITION;
			};

			Vertex_Stage_Output vert(Vertex_Stage_Input input)
			{
				Vertex_Stage_Output output;
				output.uv = (input.uv.xy * _MainTex_ST.xy) + _MainTex_ST.zw;
				output.pos = mul(unity_MatrixVP, mul(unity_ObjectToWorld, input.pos));
				return output;
			}

			Texture2D<float4> _MainTex;
			SamplerState sampler_MainTex;

			struct Fragment_Stage_Input
			{
				float2 uv : TEXCOORD0;
			};

			float4 frag(Fragment_Stage_Input input) : SV_TARGET
			{
				return _MainTex.Sample(sampler_MainTex, input.uv.xy);
			}

			ENDHLSL
		}
	}
	//CustomEditor "CustomBaseParticleGUI"
}