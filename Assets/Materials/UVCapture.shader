Shader "Custom/UVCapture"
{
    // Outputs mesh UV coordinates as color: R=U, G=V, B=0, A=1.
    // Works with Built-in render pipeline + Skinned Mesh Renderers.
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        ZWrite On
        ZTest LEqual
        Cull Back
        Lighting Off
        Fog { Mode Off }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return fixed4(i.uv.x, i.uv.y, 0.0, 1.0);
            }
            ENDCG
        }
    }

    // Fallback so it still renders if subshader fails
    Fallback "Unlit/Color"
}
