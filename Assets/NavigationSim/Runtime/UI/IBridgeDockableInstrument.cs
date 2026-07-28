using UnityEngine;

namespace NavigationSim.UnityLayer.UI
{
    /// <summary>
    /// Instrument canvas that can float in front of the user or dock into a
    /// <see cref="BridgeConsoleDisplayRig"/> slot on the physical console.
    /// </summary>
    public interface IBridgeDockableInstrument
    {
        string InstrumentId { get; }
        string DisplayName { get; }
        Vector2 NativeSizePx { get; }
        bool IsReady { get; }
        bool IsOpen { get; }
        bool IsDocked { get; }
        Transform CanvasRoot { get; }

        void SetDocked(bool docked);
        void Open();
        void Close();
    }
}
