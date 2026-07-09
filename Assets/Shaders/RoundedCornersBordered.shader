Shader "UI/RoundedCorners/RoundedCornersBordered" {
    Properties {
        [HideInInspector] _MainTex ("Texture", 2D) = "white" {}
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [HideInInspector] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
        _WidthHeightRadius ("WidthHeightRadius", Vector) = (0,0,0,0)
        _OuterUV ("image outer uv", Vector) = (0, 0, 1, 1)
        _BorderColor ("Border Color", Color) = (0,0,0,1)
        _BorderWidth ("Border Width", Float) = 0
    }
    SubShader {
        Tags {
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }
        Stencil {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZTest [unity_GUIZTestMode]
        ColorMask [_ColorMask]
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        ZWrite Off

        Pass {
            CGPROGRAM
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _WidthHeightRadius;
            float4 _OuterUV;
            sampler2D _MainTex;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            fixed4 _BorderColor;
            float _BorderWidth;
            int _UIVertexColorAlwaysGammaSpace;

            v2f vert (appdata v) {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                if (_UIVertexColorAlwaysGammaSpace)
                    if (!IsGammaSpace())
                        o.color = float4(UIGammaToLinear(o.color.xyz), o.color.w);
                return o;
            }

            // Inlined SDF (from Nobi SDFUtils). NOTE: 'half' is a reserved type — use halfSize.
            float sdfRectangle(float2 p, float2 halfSize) {
                float2 d = abs(p) - halfSize;
                return length(max(d, 0)) + min(max(d.x, d.y), 0);
            }
            float sdfRoundedRect(float2 p, float radius, float2 halfSize) {
                return sdfRectangle(p, halfSize - radius) - radius;
            }
            float aaCutoff(float dist) {
                float w = fwidth(dist) * 0.5;
                return smoothstep(w, -w, dist);
            }

            fixed4 frag (v2f i) : SV_Target {
                float2 uvS = i.uv;
                uvS.x = (uvS.x - _OuterUV.x) / (_OuterUV.z - _OuterUV.x);
                uvS.y = (uvS.y - _OuterUV.y) / (_OuterUV.w - _OuterUV.y);

                fixed4 fill = (tex2D(_MainTex, i.uv) + _TextureSampleAdd) * i.color;
                #ifdef UNITY_UI_CLIP_RECT
                fill.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif

                float2 size = _WidthHeightRadius.xy;
                float radius = _WidthHeightRadius.z;
                float2 p = (uvS - 0.5) * size;
                float sd = sdfRoundedRect(p, radius * 0.5, size * 0.5);

                float fillAlpha = aaCutoff(sd);                 // outer AA edge
                float interior  = aaCutoff(sd + _BorderWidth);  // 1 deep inside, AA at inner border edge

                fixed4 col;
                col.rgb = lerp(_BorderColor.rgb, fill.rgb, interior);
                col.a   = min(lerp(_BorderColor.a, fill.a, interior), fillAlpha);

                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif
                return col;
            }
            ENDCG
        }
    }
}
