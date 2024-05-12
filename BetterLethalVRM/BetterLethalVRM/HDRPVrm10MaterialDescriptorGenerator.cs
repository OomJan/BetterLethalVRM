using System;
using System.Collections.Generic;
using UniGLTF;
using UnityEngine;
using VRMShaders;

namespace OomJan.BetterLethalVRM;

public sealed class HDRPVrm10MaterialDescriptorGenerator : IMaterialDescriptorGenerator
{
    public MaterialDescriptor Get(GltfData Data, int i)
    {
        MaterialDescriptor tMatDesc;
        if (HDRPVrm10MToonMaterialImporter.TryCreateParam(Data, i, out tMatDesc) ||
            HDRPVrm10MToonMaterialImporter.TryCreateParam(Data, i, out tMatDesc) ||
            HDRPVrm10MToonMaterialImporter.TryCreateParam(Data, i, out tMatDesc))
            return tMatDesc;
        Debug.LogWarning(string.Format("vrm material: {0} out of range. fallback", i));
        Debug.LogError(
            "BetterLethalVRM fallback materials are not supported, try exporting your VRM again using the MToon shader");
        return new MaterialDescriptor(GltfMaterialImportUtils.ImportMaterialName(i, null),
            HDRPGltfPbrMaterialImporter.Shader, new int?(), new Dictionary<string, TextureDescriptor>(),
            new Dictionary<string, float>(), new Dictionary<string, Color>(), new Dictionary<string, Vector4>(),
            new Action<Material>[0]);
    }

    public MaterialDescriptor GetGltfDefault()
    {
        return UrpGltfDefaultMaterialImporter.CreateParam();
    }
}