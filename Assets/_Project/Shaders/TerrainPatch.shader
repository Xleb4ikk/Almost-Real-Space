Shader "Galilego/TerrainPatch"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR0;
                float3 normal : TEXCOORD0;
            };

            v2f vert(appdata_full v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 lightDir = normalize(float3(0.4, 0.8, 0.45));
                float diff = saturate(dot(normalize(i.normal), lightDir));
                return i.color * (0.35 + 0.65 * diff);
            }
            ENDCG
        }
    }
    Fallback "VertexLit"
}
