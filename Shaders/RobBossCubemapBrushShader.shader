Shader "Rob Boss/Cubemap Brush" {
	Properties {
		_MainTex ("Canvas", CUBE) = "white" {}
		_Brush ("Brush", 2D) = "white" {}
		_Color ("Color", Color) = (1,1,1,1)
		_Transform ("Polar Coord (X,Y,Z) Size (W)", Vector) = (1, 0, 0, 1)
	}
	SubShader {
		Tags { "Queue"="Background" "RenderType"="Transparent" } 

		Cull Front
		Lighting Off
		ZWrite Off
		Fog { Mode Off }
		// Blend One OneMinusSrcAlpha

		Pass {
			CGPROGRAM
				#pragma vertex vert
				#pragma fragment frag
				#include "UnityCG.cginc"

				struct v2f {
					float4 vertex : POSITION;
					float3 viewDir : TEXCOORD0;
				};
						
				samplerCUBE _MainTex;

				v2f vert(appdata_base v) {
					v2f o;
					o.vertex = UnityObjectToClipPos(v.vertex);
					// o.viewDir = -WorldSpaceViewDir(v.vertex);
					o.viewDir = normalize(v.vertex);
					return o;
				}
			
				sampler2D _Brush;
				float4 _Color;
				float4 _Transform;
			
				float4 frag(v2f IN) : COLOR {
					float d = dot(IN.viewDir, _Transform.xyz);
					float x = atan2(
						_Transform.x * IN.viewDir.z - _Transform.z * IN.viewDir.x,
						_Transform.x * IN.viewDir.x + _Transform.z * IN.viewDir.z
					);
					float y = asin(IN.viewDir.y - _Transform.y);
					float2 uv = float2(x,y) / _Transform.w;

					float4 canvas = texCUBE(_MainTex, IN.viewDir);
					float4 brush = tex2D(_Brush, uv) * _Color;
					canvas.rgb = lerp(canvas.rgb, brush.rgb, brush.a);
					canvas.a = canvas.a + brush.a;

					return canvas;
				}
			ENDCG
		}
	}
}
