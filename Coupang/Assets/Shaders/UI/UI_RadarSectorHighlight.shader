Shader "Unlit/UI_RadarSectorHighlight"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _HighlightColor ("Highlight Color (RGBA)", Color) = (1,1,1,0.6)
        _HighlightStrength ("Highlight Strength", Range(0,2)) = 0.6
        _YawDeg ("Yaw (Deg)", Range(-180,180)) = 0
        _HalfFovDeg ("Half FOV (Deg)", Range(0,180)) = 60
        _SoftEdgeDeg ("Soft Edge (Deg)", Range(0,30)) = 6

        _CircleClip ("Circle Clip", Range(0,1)) = 0
        _CircleRadius ("Circle Radius (UV)", Range(0,0.5)) = 0.5

        // UI Mask / Stencil
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
Ref[ _Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
ReadMask[ _StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

Cull Off

Lighting Off

ZWrite Off

ZTest Always

Blend SrcAlpha
OneMinusSrcAlpha
        ColorMask[_ColorMask]

        Pass
        {
Name"Default"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #pragma multi_compile __ UNITY_UI_ALPHACLIP

#include "UnityCG.cginc"
#include "UnityUI.cginc"

struct appdata_t
{
    float4 vertex : POSITION;
    float4 color : COLOR;
    float2 texcoord : TEXCOORD0;
};

struct v2f
{
    float4 vertex : SV_POSITION;
    fixed4 color : COLOR;
    float2 texcoord : TEXCOORD0;
    float4 worldPosition : TEXCOORD1;
};

sampler2D _MainTex;
fixed4 _Color;
fixed4 _TextureSampleAdd;

fixed4 _HighlightColor;
float _HighlightStrength;
float _YawDeg;
float _HalfFovDeg;
float _SoftEdgeDeg;

float _CircleClip;
float _CircleRadius;

float4 _ClipRect;

v2f vert(appdata_t v)
{
    v2f o;
    o.worldPosition = v.vertex;
    o.vertex = UnityObjectToClipPos(v.vertex);
    o.texcoord = v.texcoord;
    o.color = v.color * _Color;
    return o;
}

fixed4 frag(v2f IN) : SV_Target
{
    fixed4 c = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                // Optional hard circle clip in UV space (use if sprite isn't transparent outside circle)
    float2 p = IN.texcoord - 0.5;
    float r = length(p);
    if (_CircleClip > 0.5 && r > _CircleRadius)
        c.a = 0;

                // Angle from "up" direction (0 deg at +Y, positive to the right)
    float ang = degrees(atan2(p.x, p.y)); // [-180..180]

                // delta angle to heading
    float delta = ang - _YawDeg;
    delta = fmod(delta + 180.0, 360.0) - 180.0;

    float ad = abs(delta);
    float w = max(_SoftEdgeDeg, 0.0001);

                // inside = 1 near center, fades out at boundary
    float inside = 1.0 - smoothstep(_HalfFovDeg, _HalfFovDeg + w, ad);

                // brighten in-sector
    float blend = inside * _HighlightColor.a;
    c.rgb = lerp(c.rgb, c.rgb * (1.0 + _HighlightColor.rgb * _HighlightStrength), blend);

#ifdef UNITY_UI_CLIP_RECT
                c.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
#endif

#ifdef UNITY_UI_ALPHACLIP
                clip(c.a - 0.001);
#endif

    return c;
}
            ENDHLSLPROGRAM
        }
    }
}
