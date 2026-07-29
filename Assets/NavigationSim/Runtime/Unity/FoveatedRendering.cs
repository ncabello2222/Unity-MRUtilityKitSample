using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace NavigationSim.UnityLayer
{
    /// <summary>
    /// Turns on Fixed Foveated Rendering. The "Meta XR Foveation" OpenXR feature
    /// being enabled in project settings only exposes the extension — nothing
    /// applies a level, so without this the scene renders every peripheral pixel
    /// at full resolution and pays for it.
    ///
    /// Dynamic foveation is the part that matters for judder: it lets the OS raise
    /// the level on its own when the GPU falls behind, which buys frame time back
    /// exactly when the alternative is a missed frame reprojected without any
    /// translation correction.
    /// </summary>
    public sealed class FoveatedRendering : MonoBehaviour
    {
        /// <summary>
        /// Ceiling for the level, not a fixed value: with dynamic foveation the
        /// runtime picks anything up to this and settles lower when it has GPU
        /// headroom. On device it measured mostly at Low with excursions to Medium.
        /// Raise to High only if the periphery softening is acceptable.
        /// </summary>
        [SerializeField] private OVRManager.FoveatedRenderingLevel level =
            OVRManager.FoveatedRenderingLevel.Medium;

        [SerializeField] private bool dynamicFoveation = true;

        /// <summary>
        /// Interval for the state heartbeat. Set to 0 to log once and stop.
        /// </summary>
        [SerializeField] private float heartbeatSeconds = 5f;

        private IEnumerator Start()
        {
            // The feature caches the session handle in OnSessionCreate, and the
            // setter is a no-op until it has one.
            XRDisplaySubsystem display = null;
            for (var i = 0; i < 300 && display == null; i++)
            {
                var manager = XRGeneralSettings.Instance != null
                    ? XRGeneralSettings.Instance.Manager
                    : null;
                var loader = manager != null ? manager.activeLoader : null;
                display = loader != null ? loader.GetLoadedSubsystem<XRDisplaySubsystem>() : null;

                if (display == null)
                {
                    yield return null;
                }
            }

            if (display == null)
            {
                Debug.LogWarning("[FoveatedRendering] No XRDisplaySubsystem; leaving foveation alone.");
                yield break;
            }

            try
            {
                Meta.XR.MetaXRFoveationFeature.foveatedRenderingLevel = level;
                Meta.XR.MetaXRFoveationFeature.useDynamicFoveatedRendering = dynamicFoveation;
            }
            catch (System.Exception e)
            {
                // A throw here (missing native entry point, no session handle) would
                // otherwise kill the coroutine silently and look identical to the
                // component never having run at all.
                Debug.LogError($"[FoveatedRendering] Applying {level} threw: {e}");
                yield break;
            }

            // logcat on Quest rotates in seconds under system noise, and the app
            // suspends whenever the headset comes off, so a single startup line is
            // routinely gone before it can be read. Repeating the state means any
            // grep, at any moment, sees the truth.
            while (true)
            {
                var applied = Meta.XR.MetaXRFoveationFeature.foveatedRenderingLevel;
                if (applied == level)
                {
                    Debug.Log($"[FoveatedRendering] level={applied} dynamic={dynamicFoveation}");
                }
                else
                {
                    Debug.LogWarning(
                        $"[FoveatedRendering] asked={level} but runtime reports {applied} " +
                        "(is 'Meta XR Foveation' enabled for Android?)");
                }

                if (heartbeatSeconds <= 0f)
                {
                    yield break;
                }

                yield return new WaitForSecondsRealtime(heartbeatSeconds);
            }
        }
    }
}
