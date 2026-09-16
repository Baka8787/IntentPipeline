using System;
using UnityEngine;

namespace Project.Core.Environment
{
    [Serializable]
    public struct TraversalProbeSettings
    {
        [SerializeField] private LayerMask obstacleMask;
        [SerializeField, Min(0.01f)] private float scanStep;
        [SerializeField, Min(0f)] private float scanCeiling;
        [SerializeField, Min(0f)] private float forwardScanDistance;
        [SerializeField, Min(0f)] private float vault1mMaxDepth;
        [SerializeField, Min(0f)] private float climb1mMaxHeight;
        [SerializeField, Min(0f)] private float climb2mMaxHeight;
        [SerializeField, Min(0f)] private float heightHysteresis;
        [SerializeField, Min(0f)] private float depthHysteresis;
        [SerializeField, Min(0f)] private float probeMinSpeed;
        [SerializeField, Min(0f)] private float destinationClearanceMargin;

        public LayerMask ObstacleMask => obstacleMask;
        public float ScanStep => Mathf.Max(0.01f, scanStep);
        public float ScanCeiling => Mathf.Max(0f, scanCeiling);
        public float ForwardScanDistance => Mathf.Max(0f, forwardScanDistance);
        public float Vault1mMaxDepth => Mathf.Max(0f, vault1mMaxDepth);
        public float Climb1mMaxHeight => Mathf.Max(0f, climb1mMaxHeight);
        public float Climb2mMaxHeight => Mathf.Max(0f, climb2mMaxHeight);
        public float HeightHysteresis => Mathf.Max(0f, heightHysteresis);
        public float DepthHysteresis => Mathf.Max(0f, depthHysteresis);
        public float ProbeMinSpeed => Mathf.Max(0f, probeMinSpeed);
        public float DestinationClearanceMargin => Mathf.Max(0f, destinationClearanceMargin);

        public static TraversalProbeSettings Default => new TraversalProbeSettings(
            ~(1 << 2),
            0.1f,
            2.7f,
            1.25f,
            0.6f,
            1.25f,
            2.1f,
            0.1f,
            0.08f,
            0.1f,
            0.05f);

        public TraversalProbeSettings(
            LayerMask obstacleMask,
            float scanStep,
            float scanCeiling,
            float forwardScanDistance,
            float vault1mMaxDepth,
            float climb1mMaxHeight,
            float climb2mMaxHeight,
            float heightHysteresis,
            float depthHysteresis,
            float probeMinSpeed,
            float destinationClearanceMargin)
        {
            this.obstacleMask = obstacleMask;
            this.scanStep = scanStep;
            this.scanCeiling = scanCeiling;
            this.forwardScanDistance = forwardScanDistance;
            this.vault1mMaxDepth = vault1mMaxDepth;
            this.climb1mMaxHeight = climb1mMaxHeight;
            this.climb2mMaxHeight = climb2mMaxHeight;
            this.heightHysteresis = heightHysteresis;
            this.depthHysteresis = depthHysteresis;
            this.probeMinSpeed = probeMinSpeed;
            this.destinationClearanceMargin = destinationClearanceMargin;
        }
    }
}
