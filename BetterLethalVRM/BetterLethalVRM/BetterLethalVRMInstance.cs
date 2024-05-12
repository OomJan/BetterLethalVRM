using GameNetcodeStuff;
using System;
using System.Collections.Generic;
using System.Text;
using UniGLTF;
using UnityEngine;
using UniVRM10;

namespace OomJan.BetterLethalVRM
{
    internal class BetterLethalVRMInstance
    {
        private const int FirstPersonLayer = 23;
        private const int ThirdPersonLayer = 0;

        public readonly HashSet<Renderer> Renderers = new();
        public HashSet<(Transform target, Transform source, Quaternion localRotation)> BoneTranslation = new();
        public Transform DeadBodyRoot;
        public Dictionary<Transform, Transform> DeadMap;
        public float HipOffset;
        public PlayerControllerB PlayerControllerB;

        public Vrm10Instance Vrm10Instance;

        public void SetSkeletonMimic(Transform Root)
        {
            DeadBodyRoot = Root;
            DeadMap = new Dictionary<Transform, Transform>();

            if (PlayerControllerB.deadBody != null && PlayerControllerB.deadBody.transform == Root) Root.name = "spine";
            foreach (var tBoneTranslation in BoneTranslation)
            {
                var tTransform = Root.FindDescendant(tBoneTranslation.source.parent.name);
                var tNewBone = new GameObject("VRM Rotation Bone").transform;

                tNewBone.parent = tTransform;
                tNewBone.position = tTransform.position;
                tNewBone.localRotation = tBoneTranslation.localRotation;

                DeadMap[tBoneTranslation.source] = tNewBone;
            }

            foreach (var tRenderer in Root.GetComponentsInChildren<Renderer>())
                if ((PlayerControllerB.deadBody != null && PlayerControllerB.deadBody.transform == Root) ||
                    tRenderer.name is "LOD1" or "LOD2" or "LOD3" or "LevelSticker" or "BetaBadge")
                    tRenderer.enabled = false;
        }

        public void UpdateVisibility()
        {
            var tDeadShouldRender = !PlayerControllerB.isPlayerDead ||
                                   (DeadBodyRoot != null && PlayerControllerB.deadBody != null);

            var tLocalShouldRender = !PlayerControllerB.gameplayCamera.enabled;
            foreach (var tRenderer in Renderers)
            {
                tRenderer.gameObject.layer = tLocalShouldRender ? ThirdPersonLayer : FirstPersonLayer;
                tRenderer.enabled = tDeadShouldRender;
            }
        }
    }
}
