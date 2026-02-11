Shader "Hidden/DepthOnly"
{
    SubShader
    {
        Tags { "Queue"="Geometry-1" "RenderType"="Opaque" }

        Pass
        {
            ZWrite On
            ColorMask 0
            // Push depth slightly away from camera to prevent z-fighting
            // with equipment that shares similar geometry
            Offset 1, 1
        }
    }
}
