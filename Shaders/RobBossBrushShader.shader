Shader "Rob Boss/Brush" {
  Properties {
    _MainTex ("Canvas", 2D) = "white" {}
    _Brush ("Brush", 2D) = "white" {}
    _Color ("Color", Color) = (1,1,1,1)
    _ColorMask ("Color Mask", Color) = (1,1,1,1)
    _Transform ("UV Coord (X,Y) Angle (Z) Size (W)", Vector) = (0, 0, 0, 1)
    _Operation ("0=Norm, 1=Add, 2=Sub, 3=Mult", Int) = 0
  }
  SubShader {
    Tags { 
      "Queue"="Transparent" 
      "IgnoreProjector"="True" 
      "RenderType"="Transparent" 
      "PreviewType"="Plane"
      "CanUseSpriteAtlas"="True"
    }

    Cull Off
    Lighting Off
    ZWrite Off
    Fog { Mode Off }
    Blend One Zero
    
    Pass {
      CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #include "UnityCG.cginc"

        struct appdata_t {
          float4 pos : POSITION;
          float2 uv : TEXCOORD0;
        };

        struct v2f {
          float4 pos : SV_POSITION;
          float2 uv_MainTex : TEXCOORD0;
        };
            
        sampler2D _MainTex;
        float4 _MainTex_ST;

        v2f vert(appdata_t v) {
          v2f o;
          o.pos = UnityObjectToClipPos(v.pos);
          o.uv_MainTex = TRANSFORM_TEX(v.uv, _MainTex);
          return o;
        }
      
        sampler2D _Brush;
        float4 _Color;
        float4 _ColorMask;
        float4 _Transform;
        int _Operation;
      
        float4 frag(v2f IN) : COLOR {
          float2 uv = (IN.uv_MainTex - _Transform.xy);
          float a = radians(_Transform.z);
          uv = float2(uv.x * cos(a) - uv.y * sin(a), uv.y * cos(a) + uv.x * sin(a));
          uv = uv / _Transform.w + 0.5;

          half4 canvas = tex2D(_MainTex, IN.uv_MainTex);
          half4 brush = tex2D(_Brush, uv);

          half4 c = _Color;
          if (_Operation == 1)
            c = canvas + _Color;
          else if (_Operation == 2)
            c = canvas - _Color;
          else if (_Operation == 3)
            c = canvas * _Color;

          return lerp(canvas, saturate(c), _ColorMask * brush.a);
        }
      ENDCG
    }
  }
}
