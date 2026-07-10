Shader "UI/TailDilatedOutline" {
    // Uniform-width outline generated from the sprite's alpha silhouette by 8-direction
    // dilation — unlike scaling a second sprite copy, the stroke width is constant along
    // the whole silhouette regardless of the shape's distance from its center.
    //
    // The quad is LARGER than the art by a margin (so the dilated stroke has room to
    // spill outside the silhouette): _UVInset is the margin as a fraction of the quad,
    // and the art is sampled un-stretched inside it. _OuterUV supports atlased sprites.
    // Vertex color (the Image tint) is the stroke color.
    Properties {
        [HideInInspector] _MainTex ("Texture", 2D) = "white" {}
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [HideInInspector] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
        _OuterUV ("Sprite outer UV", Vector) = (0, 0, 1, 1)
        _UVInset ("Quad margin (fraction)", Vector) = (0, 0, 0, 0)
        _DilateUV ("Stroke radius (art-UV)", Vector) = (0.023, 0.031, 0, 0)
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

            sampler2D _MainTex;
            float4 _ClipRect;
            float4 _OuterUV;
            float4 _UVInset;
            float4 _DilateUV;
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

            static const float2 kDirs[8] = {
                float2( 1, 0), float2(-1, 0), float2(0,  1), float2(0, -1),
                float2( 0.7071,  0.7071), float2(-0.7071,  0.7071),
                float2( 0.7071, -0.7071), float2(-0.7071, -0.7071)
            };

            float SampleArtAlpha(float2 artUV) {
                if (artUV.x < 0.0 || artUV.x > 1.0 || artUV.y < 0.0 || artUV.y > 1.0) return 0.0;
                float2 suv = lerp(_OuterUV.xy, _OuterUV.zw, artUV);
                return tex2D(_MainTex, suv).a;
            }

            fixed4 frag (v2f i) : SV_Target {
                // Quad UV -> normalized [0,1] across the quad (atlas-safe) -> art UV inside the margin.
                float2 nuv = (i.uv - _OuterUV.xy) / (_OuterUV.zw - _OuterUV.xy);
                float2 artUV = (nuv - _UVInset.xy) / (1.0 - 2.0 * _UVInset.xy);

                float a = SampleArtAlpha(artUV);
                [unroll]
                for (int k = 0; k < 8; k++)
                    a = max(a, SampleArtAlpha(artUV + kDirs[k] * _DilateUV.xy));

                fixed4 col = i.color;
                col.a *= a;

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif
                return col;
            }
            ENDCG
        }
    }
}
