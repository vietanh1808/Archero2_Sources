Shader "FX/ConmonEffect" {
	Properties {
		_MainTex ("Main Tex", 2D) = "white" {}
		_SecondaryTex ("Secondary Tex", 2D) = "white" {}
		_MainTexUVAni ("MainTex Ani", Vector) = (0,0,0,0)
		_DistortMap ("Distort Tex", 2D) = "white" {}
		_SecondaryDistortMap ("Secondary Dissolve Tex", 2D) = "white" {}
		[Toggle] _SecondAdd ("第二张图相加", Float) = 0
		_MaskMap ("Mask Tex", 2D) = "white" {}
		_DistortFactor ("扭曲强度", Range(0, 0.5)) = 0.15
		_UVAnination ("Distort Ani", Vector) = (0,0,0,0)
		[HDR] _TintColor ("Color", Vector) = (1,1,1,1)
		_ColorFactor ("Color Factor", Range(0, 5)) = 1.5
		_rimPower ("Rim Power", Range(0, 10)) = 5
		_rimFactor ("Rim Factor", Range(0, 10)) = 1
		_rimColor ("rimColor", Vector) = (1,1,1,1)
		_DissolveFactor ("dissolve factor", Range(0, 1.01)) = 0.5
		_DissolveEdge ("dissolve Edge", Range(0, 0.3)) = 0.1
		_DissolveEdgeColor ("dissolve Edge Color", Vector) = (1,1,1,1)
		_DissolveMap ("Dissolve Tex", 2D) = "white" {}
		[Enum(Off,4,On,0)] _Ztest ("Z Test", Float) = 4
		[Toggle(ENABLE_SECONDARYTEX)] _EnableSecondaryTex ("贴图2", Float) = 0
		[Toggle(ENABLE_DISTORT)] _Distort ("扭曲", Float) = 0
		[Toggle(ENABLE_MASK)] _Mask ("遮罩", Float) = 0
		[Toggle(ENABLE_RIM)] _Rim ("Rim", Float) = 0
		[Toggle(ENABLE_DISSOLVE)] _Dissolve ("Dissolve", Float) = 0
		[Toggle(ENABLE_CLIP)] _Clip ("Clip", Float) = 0
		[Toggle(ISPARTICLE)] _IsParticle ("is particle", Float) = 0
		[Toggle(ENABLE_PROJECTOR)] _EnableProjector ("EnableProjector", Float) = 0
		[HideInInspector] _Mode ("__mode", Float) = 0
		[HideInInspector] _SrcBlend ("__src", Float) = 1
		[HideInInspector] _DstBlend ("__dst", Float) = 0
		[HideInInspector] _SrcBlendAlpha ("__srcA", Float) = 1
		[HideInInspector] _DstBlendAlpha ("__dstA", Float) = 10
		[HideInInspector] _CullMode ("__CullMode", Float) = 0
		[HideInInspector] _Cull ("__Cull", Float) = 0
		_StencilComp ("Stencil Comparison", Float) = 8
		_Stencil ("Stencil ID", Float) = 0
		_StencilOp ("Stencil Operation", Float) = 0
		_StencilWriteMask ("Stencil Write Mask", Float) = 255
		_StencilReadMask ("Stencil Read Mask", Float) = 255
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
	//CustomEditor "CustomFXShaderGUI"
}