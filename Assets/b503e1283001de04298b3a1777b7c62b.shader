Shader "ArcheroII/UnitAlphaVOffset" {
	Properties {
		_MainColor ("MainColor", Vector) = (1,1,1,0)
		_MainTexARoughness ("MainTex(A Roughness)", 2D) = "white" {}
		_MaskTex ("MaskTex", 2D) = "white" {}
		_Vector ("Vector", Vector) = (0,0,0,0)
		_WindSpeed ("WindSpeed", Float) = 1
		_Ramp ("Ramp", Float) = -0.1
		[HideInInspector] _texcoord3 ("", 2D) = "white" {}
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
	//CustomEditor "ASEMaterialInspector"
}