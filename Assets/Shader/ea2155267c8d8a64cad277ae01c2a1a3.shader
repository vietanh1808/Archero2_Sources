Shader "ArcheroII/MatcapShine" {
	Properties {
		_MainColor ("MainColor", Vector) = (1,1,1,0)
		_MatTex ("MatTex", 2D) = "white" {}
		_ShineCol ("ShineCol", Vector) = (1,1,1,1)
		_Shine_Tex ("Shine_Tex", 2D) = "white" {}
		_AO_Tex ("AO_Tex", 2D) = "white" {}
		_ShineSpd ("ShineSpd", Vector) = (0,0,0,0)
		[Space(20)] [Header(Rim)] _RimPower ("边缘光亮度", Range(0, 1)) = 0
		_RimColor ("边缘光颜色", Vector) = (1,0,0,1)
		_RimScale ("边缘光渐变速率", Range(0.001, 10)) = 0.1
		_Light ("亮度", Range(0, 3)) = 1
		[HideInInspector] _texcoord2 ("", 2D) = "white" {}
		[HideInInspector] _texcoord ("", 2D) = "white" {}
		[HideInInspector] __dirty ("", Float) = 1
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
	//CustomEditor "ASEMaterialInspector"
}