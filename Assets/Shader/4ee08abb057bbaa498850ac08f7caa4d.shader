Shader "AppsTools/FastShader/Effect/Add" {
	Properties {
		_Main ("Main", 2D) = "white" {}
		[HDR] _Main_Color ("Main_Color", Vector) = (0.5,0.5,0.5,1)
		_Rongjie ("Rongjie", 2D) = "white" {}
		_souf ("souf", Float) = 0
		_Mask ("Mask", 2D) = "white" {}
		_Mask_u ("Mask_u", Float) = 0
		_Mask_v ("Mask_v", Float) = 0
		_Main_u ("Main_u", Float) = 0
		_Main_v ("Main_v", Float) = 0
		_Rongjie_u ("Rongjie_u", Float) = 0
		_Rongjie_v ("Rongjie_v", Float) = 0
		[MaterialToggle] _UV_open ("UV_open", Float) = 0
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
	Fallback "Legacy Shaders/VertexLit"
}