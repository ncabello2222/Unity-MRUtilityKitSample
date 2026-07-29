using System.Collections;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR.Features.Meta;

namespace NavigationSim.UnityLayer
{
    /// <summary>
    /// Requests a display refresh rate and reports what the runtime actually
    /// granted. TimeWarp reprojects head rotation but not head translation, so a
    /// faster display does shrink the render-time-to-photon-time error that reads
    /// as shimmer on the instrument panels. But that only holds while every frame
    /// lands: a missed frame is re-displayed with rotation-only reprojection, and
    /// the resulting positional judder is far worse than the interval it saved.
    ///
    /// Requires "Meta Quest: Display Utilities" under Project Settings >
    /// XR Plug-in Management > OpenXR; without it the request silently fails.
    /// </summary>
    public sealed class DisplayRefreshRate : MonoBehaviour
    {
        /// <summary>
        /// Ceiling for the request. 72 Hz buys 13.9 ms per frame against 11.1 ms
        /// at 90; this scene has not been measured inside either budget yet, and
        /// Meta's guidance is that a held 72 beats a dropped 90. Raise to 90 only
        /// after the GPU frame time is confirmed under 11.1 ms on device.
        /// </summary>
        [SerializeField] private float preferredHz = 72f;

        /// <summary>
        /// Interval for the state heartbeat. Set to 0 to log once and stop.
        /// </summary>
        [SerializeField] private float heartbeatSeconds = 5f;

        private IEnumerator Start()
        {
            XRDisplaySubsystem display = null;

            // The loader reports no running subsystems for the first few frames.
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
                Debug.LogWarning("[DisplayRefreshRate] No XRDisplaySubsystem; leaving the display rate alone.");
                yield break;
            }

            if (!display.TryGetSupportedDisplayRefreshRates(Allocator.Temp, out var rates))
            {
                Debug.LogWarning(
                    "[DisplayRefreshRate] Could not read supported rates. Enable 'Meta Quest: Display Utilities' " +
                    "under Project Settings > XR Plug-in Management > OpenXR.");
                yield break;
            }

            // Fastest rate at or below the ceiling.
            var target = 0f;
            var supported = new System.Text.StringBuilder();
            foreach (var hz in rates)
            {
                if (supported.Length > 0)
                {
                    supported.Append(", ");
                }

                supported.Append(hz.ToString("0.##"));

                if (hz <= preferredHz + 0.01f && hz > target)
                {
                    target = hz;
                }
            }

            rates.Dispose();

            if (target <= 0f)
            {
                Debug.LogWarning(
                    $"[DisplayRefreshRate] No supported rate at or below {preferredHz} Hz. Supported: {supported}");
                yield break;
            }

            var hadBefore = display.TryGetDisplayRefreshRate(out var before);
            var beforeText = hadBefore ? $"{before:0.##} Hz" : "unknown";

            if (!display.TryRequestDisplayRefreshRate(target))
            {
                Debug.LogWarning($"[DisplayRefreshRate] Request for {target} Hz was rejected (was {beforeText}).");
                yield break;
            }

            // Confirm against the target, not against the previous rate: the first
            // read can fail, and comparing "changed from what we last read" then
            // reports the stale rate as a success.
            var settled = 0f;
            for (var i = 0; i < 120 && !Mathf.Approximately(settled, target); i++)
            {
                yield return null;

                if (display.TryGetDisplayRefreshRate(out var now))
                {
                    settled = now;
                }
            }

            if (!Mathf.Approximately(settled, target))
            {
                Debug.LogWarning(
                    $"[DisplayRefreshRate] Asked for {target} Hz but the display reads {settled:0.##} Hz " +
                    $"(was {beforeText}). Supported set: {supported}");
                yield break;
            }

            // Repeat it: logcat rotates in seconds under system noise and the app
            // suspends every time the headset comes off, so a lone startup line is
            // usually gone before it can be read off the device.
            while (true)
            {
                Debug.Log($"[DisplayRefreshRate] {settled:0.##} Hz (asked {target}, was {beforeText})");

                if (heartbeatSeconds <= 0f)
                {
                    yield break;
                }

                yield return new WaitForSecondsRealtime(heartbeatSeconds);

                if (display.TryGetDisplayRefreshRate(out var now))
                {
                    settled = now;
                }
            }
        }
    }
}
