Shader "ArcheroII/Sea/Transparent" {
	Properties {
		_MainColor_1 ("MainColor_1", Vector) = (1,1,1,0)
		_MainColor_2 ("MainColor_2", Vector) = (0.5,0.5,0.5,0)
		_WaveTex ("WaveTex", 2D) = "white" {}
		_WaterAlpha ("WaterAlpha", Float) = 1
		_FoamDistance ("FoamDistance", Float) = 1
		_FoamColorRGBA ("FoamColor(RGBA)", Vector) = (1,1,1,1)
		_FoamPower ("FoamPower", Float) = 1
		_DepthDistance ("DepthDistance", Float) = 1
		_DepthPower ("DepthPower", Float) = 1
		_WaveSpeedU ("WaveSpeed(U)", Float) = 0
		_WaveSpeedV ("WaveSpeed(V)", Float) = 0
		_WaveHight ("WaveHight", Vector) = (0,0,0,0)
		_TexColor ("TexColor", Vector) = (0,0,0,0)
		[HideInInspector] _texcoord2 ("", 2D) = "white" {}
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