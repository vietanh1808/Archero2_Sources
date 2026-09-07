Shader "ArcheroII/RimRollAdd" {
	Properties {
		_MainColor ("MainColor", Vector) = (0,0,0,1)
		_RollTex ("RollTex", 2D) = "white" {}
		_RollIntensity ("RollIntensity", Float) = 1
		_SpeedXY ("Speed(XY)", Vector) = (0,0,0,0)
		_RimScale1 ("光遮罩范围", Range(0, 10)) = 0
		_RimScale2 ("边缘光变化率", Range(0, 10)) = 0
		_RimScale3 ("边缘光递进", Range(0, 1)) = 0
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