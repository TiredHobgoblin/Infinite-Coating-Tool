using System;
using System.Collections.Generic;

namespace Infinite_Coating_Tool
{
    namespace Json
    {
        public class CoatingContainer
        {
            public string grimeSwatch { get; set; }
            public string name { get; set; }
            public float emissiveAmount { get; set; }
            public Swatch[] swatches { get; set; }
            public float grimeAmount { get; set; }
            public Dictionary<string, Region> regionLayers { get; set; }
            public float scratchAmount { get; set; }
        }

        public class Swatch
        {
            public string groupName { get; set; }
            public float ior { get; set; }
            public List<float> normalTextureTransform { get; set; }
            public float roughness { get; set; }
            public float roughnessBlack { get; set; }
            public float roughnessWhite { get; set; }
            public float scratchMetallic { get; set; }
            public float scratchRoughness { get; set; }
            public float scratchAlbedoTint { get; set; }
            public float scratchBrightness { get; set; }
            public float scratchIor { get; set; }
            public float metallic { get; set; }
            public float emissiveAmount { get; set; }
            public float emissiveIntensity { get; set; }
            public ColorVariants colorVariant { get; set; }
            public List<float> scratchColor { get; set; }
            public string colorGradientMap { get; set; }
            public string normalPath { get; set; }
            public string colorVariantId { get; set; }
            public  string swatchId { get; set; }
        }

        public class ColorVariants
        {
            public float[] topColor { get; set; }
            public float[] midColor { get; set; }
            public float[] botColor { get; set; }
            public string id { get; set; }
        }

        public class Region
        {
            public Layer[] layers { get; set; }
            public string material { get; set; }
            public string bodyPart { get; set; }
        }

        public class Layer
        {
            public bool colorBlend { get; set; }
            public string swatch { get; set; }
            public bool ignoreTexelDensity { get; set; }
            public bool normalBlend { get; set; }
        }
    }
}