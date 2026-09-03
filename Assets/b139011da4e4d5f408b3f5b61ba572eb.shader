Shader "Archero2/Character/MonsterSpecular" {
	Properties {
		_MainTexARoughness ("MainTex(A Roughness)", 2D) = "write" {}
		_MainColor ("MainColor(RGB)(叠加)", Vector) = (1,1,1,1)
		_SpecularColor ("SpecularColor(RGB)", Vector) = (1,1,1,0)
		[NoScaleOffset] _SpecularColorTex ("SpecularColorTex(RGB)", 2D) = "write" {}
		_Gloss ("Gloss", Range(8, 256)) = 20
		_SpecIntensity ("SpecularIntensity", Range(0, 3)) = 1
		[Space(20)] [Header(Rim)] _RimPower ("边缘光亮度", Range(0, 1)) = 0
		_RimColor ("边缘光颜色", Vector) = (1,0,0,1)
		_RimScale ("边缘光渐变速率", Range(0.001, 10)) = 0.1
		_Light ("亮度", Range(0, 3)) = 1
		[Space(20)] [Header(Special)] [Toggle(_SPECIAL)] _SpecialToggle ("SPECIALToggle", Float) = 0
		[Enum(Single Sided, 2, Double Sided, 0)] _CullMode ("Render Sides", Float) = 2
		_SpecialAlpha ("特殊状态透明度", Range(0, 1)) = 1
	}
	//DummyShaderTextExporter
	SubShader{
		Tags { "RenderType" = "Opaque" }
		LOD 200

		Pass
		{
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			float4x4 unity_ObjectToWorld;
			float4x4 unity_MatrixVP;

			struct Vertex_Stage_Input
			{
				float4 pos : POSITION;
			};

			struct Vertex_Stage_Output
			{
				float4 pos : SV_POSITION;
			};

			Vertex_Stage_Output vert(Vertex_Stage_Input input)
			{
				Vertex_Stage_Output output;
				output.pos = mul(unity_MatrixVP, mul(unity_ObjectToWorld, input.pos));
				return output;
			}

			float4 frag(Vertex_Stage_Output input) : SV_TARGET
			{
				return float4(1.0, 1.0, 1.0, 1.0); // RGBA
			}

			ENDHLSL
		}
	}
	Fallback "Diffuse"
}