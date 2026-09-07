Shader "Hovl/Particles/AddTrail" {
	Properties {
		_MainTexture ("MainTexture", 2D) = "white" {}
		_SpeedMainTexUVNoiseZW ("Speed MainTex U/V + Noise Z/W", Vector) = (0,0,0,0)
		_StartColor ("StartColor", Vector) = (1,0,0,1)
		_EndColor ("EndColor", Vector) = (1,1,0,1)
		_Colorpower ("Color power", Float) = 1
		_Colorrange ("Color range", Float) = 1
		_Noise ("Noise", 2D) = "white" {}
		_Emission ("Emission", Float) = 2
		[Toggle] _Usedark ("Use dark", Float) = 1
		[Toggle] _Mask ("Mask", Float) = 0
		_Maskpower ("Mask power", Float) = 10
		[MaterialToggle] _Usedepth ("Use depth?", Float) = 0
		_Depthpower ("Depth power", Float) = 1
		[Enum(Cull Off,0, Cull Front,1, Cull Back,2)] _CullMode ("Culling", Float) = 2
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
}